using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync
{
    public interface ISyncLogger
    {
        void Trace(string message);

        void Info(string message);

        void Warning(string message);

        void Error(string message);
    }
}
