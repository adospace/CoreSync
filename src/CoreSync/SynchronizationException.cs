using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace CoreSync
{
    public class SynchronizationException : Exception
    {
        public SynchronizationException()
        {
        }

        public SynchronizationException(string message) : base(message)
        {
        }

        public SynchronizationException(string message, Exception innerException) : base($"{message}({innerException.Message})", innerException)
        {
        }

        protected SynchronizationException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
