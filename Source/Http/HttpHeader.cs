﻿/*---------------------------------------------------------------------------
RemObjects Internet Pack for .NET
  (c)opyright RemObjects Software, LLC. 2003-2016. All rights reserved.
  ---------------------------------------------------------------------------*/

using RemObjects.Elements.RTL;

namespace RemObjects.InternetPack.Http
{
	public class HttpHeader
	{
		#region Private fields
		List<String> fValues = new List<String>();
		#endregion

		public HttpHeader(String name, String value)
		{
			this.Name = name;
			this.fValues.Add(value);
		}

		public HttpHeader(String line)
		{
			Int32 lPos = line.IndexOf(":");
			if (lPos == -1)
				throw new HttpHeaderException("Invalid HTTP Header Line \"" + line + "\"");

			this.Name = line.Substring(0, lPos);
			this.fValues.Add(line.Substring(lPos + 2));
		}

		#region ToString
		[ToString]
		public override String ToString()
		{
			if (this.Count == 1)
				return String.Format("{0}: {1}", this.Name, this.fValues[0]);

			StringBuilder lResult = new StringBuilder();
			for (Int32 i = 0; i < this.Count; i++)
			{
				if (i > 0)
					lResult.Append("\r\n");

				lResult.Append(String.Format("{0}: {1}", this.Name, this.fValues[i]));
			}

			return lResult.ToString();
		}
		#endregion

		#region Properties
		public String Name { get; set; }

		public Int32 Count
		{
			get
			{
				return this.fValues.Count;
			}
		}

		public String Value
		{
			get
			{
				if (this.Count == 1)
					return this.fValues[0];

				StringBuilder lResult = new StringBuilder();
				for (Int32 i = 0; i < this.Count; i++)
				{
					if (i > 0)
						lResult.Append(",");

					lResult.Append(String.Format("\"{0}\"", this.fValues[i]));
				}

				return lResult.ToString();
			}
			set
			{
				this.fValues.RemoveAll();
				this.fValues.Add(value);
			}
		}
		#endregion

		public String Get(Int32 index)
		{
			return this.fValues[index];
		}

		public void Add(String item)
		{
			this.fValues.Add(item);
		}
	}

	public class HttpHeaders
	{
		// Cannot use Dictionary<> here because non-generic Enumerator is exposed
		//private readonly Hashtable fHeaders;
		private readonly Dictionary<String, HttpHeader>! fHeaders;

		public HttpHeaders()
		{
			//this.fHeaders = new Hashtable(StringComparer.OrdinalIgnoreCase);
			this.fHeaders = new Dictionary<String, HttpHeader>();
			this.HttpCode = HttpStatusCode.OK;
			this.Initialize();
		}

		public static HttpHeaders Create(Connection connection)
		{
			HttpHeaders lResult = new HttpHeaders();
			if (!lResult.ReadHeader(connection))
				return null;

			return lResult;
		}

		protected virtual void Initialize()
		{
			this.MaxHeaderLines = 100;
			this.MaxHeaderLinesEnabled = true;
		}

		#region Private Helper Methods
		public void ParseFirstLine()
		{
			if (this.FirstHeader.Length == 0)
				throw new HttpHeaderException("HTTP Header is empty");

			String lHeaderLine = this.FirstHeader;
			var lRequestHeaderValues = lHeaderLine.Split(" ");

			if (lRequestHeaderValues.Count < 3)
				throw new HttpHeaderException("Invalid HTTP Header Line \"" + lHeaderLine + "\"");

			if (lHeaderLine.StartsWith("HTTP/"))
			{
				// HTTP Response
				try
				{
					//this.HttpCode = (HttpStatusCode)Enum.Parse(typeof(HttpStatusCode), lRequestHeaderValues[1], true);
					this.HttpCode = (HttpStatusCode)Convert.ToInt32(lRequestHeaderValues[1]);
				}
				catch (ArgumentException)
				{
					this.HttpCode = HttpStatusCode.OK;
				}
			}
			else
			{
				// HTTP Request
				this.RequestType = lRequestHeaderValues[0];
				this.RequestPath = lRequestHeaderValues[1];
				this.RequestVersion = lRequestHeaderValues[2];

				if (this.RequestVersion.StartsWith("HTTP/"))
					this.RequestVersion = RequestVersion.Substring(5);
			}
		}
		#endregion

		#region Properties
		public String RequestType { get; set; }

		public String RequestPath { get; set; }

		public String RequestVersion { get; set; }

		[Obsolete("Access HTTP code using the HttpCode property")]
		public Int32 ResponseCode
		{
			get
			{
				return (Int32)this.HttpCode;
			}
			set
			{
				this.HttpCode = (HttpStatusCode)value;
			}
		}

		public HttpStatusCode HttpCode { get; private set; }

		public Int32 MaxHeaderLines { get; set; }

		public Boolean MaxHeaderLinesEnabled { get; set; }

		public String? ContentType
		{
			get
			{
				if (ContainsHeaderValue(HEADER_CONTENT_TYPE))
					return GetHeaderValue(HEADER_CONTENT_TYPE);
				return null;
			}
			set
			{
				SetHeaderValue(HEADER_CONTENT_TYPE, value);
			}
		}
		private const String HEADER_CONTENT_TYPE = "Content-Type";

		public String? Host
		{
			get
			{
				if (ContainsHeaderValue(HEADER_HOST))
					return GetHeaderValue(HEADER_HOST);
				return null;
			}
		}
		private const String HEADER_HOST = "Host";

		public String FirstHeader { get; set; }
		public HttpRequestMode? Mode { get; set; }

		public Int32 Count
		{
			get
			{
				return fHeaders.Count;
			}
		}

		public HttpHeader? this[String key]
		{
			get
			{
				return this.fHeaders[key?.ToLowerInvariant()];
			}
		}

		public ISequence<string>! Keys
		{
			get { return fHeaders.Values.Select(v => v.Name); }
		}

		public ISequence<HttpHeader>! Headers
		{
			get { return fHeaders.Values.ToList()!; }
		}

		#endregion

		#region Methods
		// This method returns:
		// 1. NULL if incoming datastream doesn't contain at least 4 bytes
		// 2. Empty String is HTTP method is unknown
		// 3. HTTP method name
		private static (HttpRequestMode?, string) ReadHttpMethodName(Connection connection)
		{
			Byte[] lBuffer = new Byte[4];

			switch (connection.Receive(lBuffer, 0, 4))
			{
				case 0: return (HttpRequestMode.Get /*irrelevant*/, null);
				case 1:
				case 2:
				case 3: throw new HttpRequestInvalidException(HttpStatusCode.InternalServerError, "Invalid HTTP Request Mode (incomplete header verb received).");
			}

			void ReadToSpace()
			{
				int i = 0;
				var c = chr(connection.ReadByte());
				while (c != ' ' && i++ < 5)
					c = chr(connection.ReadByte());
			}

			string lHttpMethodName = Encoding.ASCII.GetString(lBuffer, 0, 4);
			switch (lHttpMethodName)
			{
				case "GET ": return (HttpRequestMode.Get, "GET");
				case "PUT ": return (HttpRequestMode.Put, "PUT");
				case "HEAD": ReadToSpace(); return (HttpRequestMode.Head, "HEAD");
				case "POST": ReadToSpace(); return (HttpRequestMode.Post, "POST");
				case "DELE": ReadToSpace(); return (HttpRequestMode.Delete, "DELETE");
				case "OPTI": ReadToSpace(); return (HttpRequestMode.Options, "OPTIONS");
				case "PATC": ReadToSpace(); return (HttpRequestMode.Patch, "PATCH");
				case "TRAC": ReadToSpace(); return (HttpRequestMode.Trace, "TRACE");
				//case "HTTP": return (HttpRequestMode.Http, "HTTP");
				//case "MERG": return (HttpRequestMode.Merge, "MERGE");
			}
			throw new HttpRequestInvalidException(HttpStatusCode.InternalServerError, $"Invalid HTTP Request Mode (GET/OUT/HEAD/POST/DELETE/OPTIONS/PATCH/TRACE/MERGE/HTTP header expected, got '{lHttpMethodName}').");
		}

		public Boolean ReadHeader(Connection connection)
		{
			this.FirstHeader = String.Empty;

			string lStart;
			(this.Mode, lStart) = HttpHeaders.ReadHttpMethodName(connection);
			if (length(lStart) == 0)
			{
				writeLn("disconnect");
				return false;
			}
			String lHeaderLine;
			do
			{
				lHeaderLine = connection.ReadLine();
				if (!String.IsNullOrEmpty(lHeaderLine))
				{
					if (this.FirstHeader.Length == 0)
					{
						this.FirstHeader = lStart+" "+lHeaderLine;
					}
					else
					{
						Int32 lPos = lHeaderLine.IndexOf(":");
						if (lPos == -1)
						{
							throw new HttpHeaderException("Invalid HTTP Header Line \"" + lHeaderLine + "\"");
						}

						String lName = lHeaderLine.Substring(0, lPos);
						String lValue = null;

						// There should be a space after the ":" , but at least one custome said that this is not
						// always true
						// So we check if there is space after the ":"
						if (lHeaderLine.Length > lPos + 1)
						{
							if (lHeaderLine[lPos + 1] == ' ')
								lValue = lHeaderLine.Substring(lPos + 2);
							else
								lValue = lHeaderLine.Substring(lPos + 1);
						}

						HttpHeader lHeader = this[lName];
						if (lHeader == null)
						{
							lHeader = new HttpHeader(lName, lValue);
							fHeaders.Add(lName?.ToLowerInvariant(), lHeader);
						}
						else
						{
							lHeader.Add(lValue);
						}
					}
				}

				if (this.MaxHeaderLinesEnabled && this.fHeaders.Count > this.MaxHeaderLines - 1) // -1 because FirstHeader is not in hashtable
				{
					connection.Disconnect();
					throw new HttpHeaderException(String.Format("Too many header lines received (maximum is set to {0})", MaxHeaderLines));
				}
			}
			while (!String.IsNullOrEmpty(lHeaderLine));

			this.ParseFirstLine();

			return true;
		}

		public void WriteHeader(Connection connection)
		{
			connection.WriteLine(FirstHeader);

			foreach (HttpHeader header in this)
			{
				connection.WriteLine(header.ToString());
			}
			connection.WriteLine("");
		}

		#if !cooper
		[Obsolete("Provide HTTP code using a System.Net.HttpStatusCode value")]
		public void SetResponseHeader(String version, Int32 code)
		{
			this.SetResponseHeader(version, (HttpStatusCode)code);
		}
		#endif

		public void SetResponseHeader(String version, HttpStatusCode code)
		{
			this.FirstHeader = String.Format("HTTP/{0} {1} {2}", version, ((Int32)code).ToString(), code.ToString());
			this.HttpCode = code;
		}

		public void SetRequestHeader(String version, String requestType, String requestPath)
		{
			FirstHeader = String.Format("{0} {1} HTTP/{2}", requestType, requestPath, version);
		}

		public Boolean ContainsHeaderValue(String key)
		{
			return this.fHeaders.ContainsKey(key?.ToLowerInvariant());
		}

		public void SetHeaderValue(String name, String value)
		{
			HttpHeader lHeader = this[name];

			if (lHeader == null)
			{
				lHeader = new HttpHeader(name, value);
				fHeaders[name?.ToLowerInvariant()] = lHeader;
			}
			else
			{
				lHeader.Value = value;
			}
		}

		public String GetHeaderValue(String name)
		{
			HttpHeader lHeader = this[name];

			if (lHeader != null)
				return lHeader.Value;

			return null;
		}

		[ToString]
		public override String ToString()
		{
			StringBuilder lResult = new StringBuilder();
			lResult.Append(FirstHeader);
			lResult.Append("\r\n");

			foreach (HttpHeader header in this)
			{
				lResult.Append(header.ToString());
				lResult.Append("\r\n");
			}

			lResult.Append("\r\n");

			return lResult.ToString();
		}
		#endregion

		[Sequence]
		public ISequence<HttpHeader>! GetSequence()
		{
			foreach (var k in fHeaders.Keys)
				yield return fHeaders[k];
		}
	}
}