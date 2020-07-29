using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync.Tests
{
    internal class ConsoleLogger : ISyncLogger
    {
        private readonly string _label;

        public ConsoleLogger(string label)
        {
            _label = label;
        }

        public void Error(string message) => System.Diagnostics.Debug.WriteLine($"[ERR] [{_label}] {message}");

        public void Info(string message) => System.Diagnostics.Debug.WriteLine($"[INF] [{_label}] {message}");

        public void Trace(string message) => System.Diagnostics.Debug.WriteLine($"[TRC] [{_label}] {message}");

        public void Warning(string message) => System.Diagnostics.Debug.WriteLine($"[WRN] [{_label}] {message}");
    }
}
