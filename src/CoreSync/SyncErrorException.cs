using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync
{
    public class SyncErrorException : Exception
    {
        public SyncErrorException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
