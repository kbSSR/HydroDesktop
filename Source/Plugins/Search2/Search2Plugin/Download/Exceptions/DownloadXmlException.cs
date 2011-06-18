﻿using System;
using System.Runtime.Serialization;

namespace HydroDesktop.Search.Download.Exceptions
{
    class DownloadXmlException : Exception
    {
        private const string DEFAULT_MESSAGE = "Download Xml Exception.";

        public DownloadXmlException()
            : this(DEFAULT_MESSAGE)
        {

        }
        public DownloadXmlException(Exception inner)
            : this(DEFAULT_MESSAGE, inner)
        {

        }
        public DownloadXmlException(string message)
            : this(message, null)
        {

        }
        public DownloadXmlException(string message, Exception inner)
            : base(message, inner)
        {

        }

        protected DownloadXmlException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {

        }
    }
}
