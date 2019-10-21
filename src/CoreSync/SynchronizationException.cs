using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync
{
    public class SynchronizationException : Exception
    {
        internal SynchronizationException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
