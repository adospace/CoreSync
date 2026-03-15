using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync
{
    /// <summary>
    /// Defines logging methods used by sync providers to report diagnostic information.
    /// </summary>
    public interface ISyncLogger
    {
        /// <summary>
        /// Logs a trace-level message for detailed diagnostic output.
        /// </summary>
        /// <param name="message">The message to log.</param>
        void Trace(string message);

        /// <summary>
        /// Logs an informational message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        void Info(string message);

        /// <summary>
        /// Logs a warning message indicating a potential issue.
        /// </summary>
        /// <param name="message">The message to log.</param>
        void Warning(string message);

        /// <summary>
        /// Logs an error message indicating a failure.
        /// </summary>
        /// <param name="message">The message to log.</param>
        void Error(string message);
    }
}
