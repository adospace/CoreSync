using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace CoreSync
{
    /// <summary>
    /// Raised by <see cref="SyncAgent.SynchronizeAsync"/> for every failure, with the underlying error
    /// as <see cref="Exception.InnerException"/>, and by providers when a single change cannot be
    /// applied to a store.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The message and the inner exception are what they have always been; applications that branch on
    /// <see cref="Exception.InnerException"/> (for example <c>is UnauthorizedAccessException</c>) keep
    /// working. The classification properties (<see cref="Kind"/>, <see cref="Stage"/>,
    /// <see cref="TableName"/>, <see cref="ErrorCode"/>, <see cref="TraceId"/>,
    /// <see cref="ServerMessage"/>) are additional and default to "not known".
    /// </para>
    /// <para>
    /// Every classification value is mirrored into <see cref="Exception.Data"/> under a
    /// <c>CoreSync.*</c> key (<see cref="DataKeyKind"/> and siblings), so error reporters that capture
    /// exception data (Sentry, Application Insights, structured loggers) show it without any
    /// application code.
    /// </para>
    /// </remarks>
    public class SynchronizationException : Exception, ISyncFailureInfo
    {
        /// <summary>
        /// Key under which <see cref="Kind"/> is mirrored into <see cref="Exception.Data"/>.
        /// </summary>
        public const string DataKeyKind = "CoreSync.Kind";

        /// <summary>
        /// Key under which <see cref="Stage"/> is mirrored into <see cref="Exception.Data"/>.
        /// </summary>
        public const string DataKeyStage = "CoreSync.Stage";

        /// <summary>
        /// Key under which <see cref="ErrorCode"/> is mirrored into <see cref="Exception.Data"/>.
        /// </summary>
        public const string DataKeyErrorCode = "CoreSync.ErrorCode";

        /// <summary>
        /// Key under which <see cref="TableName"/> is mirrored into <see cref="Exception.Data"/>.
        /// </summary>
        public const string DataKeyTableName = "CoreSync.TableName";

        /// <summary>
        /// Key under which <see cref="TraceId"/> is mirrored into <see cref="Exception.Data"/>.
        /// </summary>
        public const string DataKeyTraceId = "CoreSync.TraceId";

        /// <summary>
        /// Key under which <see cref="ServerMessage"/> is mirrored into <see cref="Exception.Data"/>.
        /// </summary>
        public const string DataKeyServerMessage = "CoreSync.ServerMessage";

        private SyncFailureKind _kind;
        private SynchronizationStage _stage;
        private string? _errorCode;
        private string? _tableName;
        private string? _traceId;
        private string? _serverMessage;

        public SynchronizationException()
        {
        }

        public SynchronizationException(string message) : base(message)
        {
        }

        public SynchronizationException(string message, Exception innerException) : base($"{message}: {innerException.Message}", innerException)
        {
        }

        protected SynchronizationException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }

        /// <summary>
        /// Gets or sets why the synchronization failed. <see cref="SyncFailureKind.Unknown"/> when the
        /// failing component did not say.
        /// </summary>
        public SyncFailureKind Kind
        {
            get => _kind;
            set
            {
                _kind = value;
                SetData(DataKeyKind, value == SyncFailureKind.Unknown ? null : value.ToString());
            }
        }

        /// <summary>
        /// Gets or sets the step of <see cref="SyncAgent.SynchronizeAsync"/> that was executing when
        /// the failure occurred. <see cref="SynchronizationStage.Unknown"/> when the exception was not
        /// raised by the agent.
        /// </summary>
        public SynchronizationStage Stage
        {
            get => _stage;
            set
            {
                _stage = value;
                SetData(DataKeyStage, value == SynchronizationStage.Unknown ? null : value.ToString());
            }
        }

        /// <summary>
        /// Gets or sets the machine-readable error code reported by the remote side, when any
        /// (see <c>CoreSync.Http.SyncHttpErrorCodes</c>).
        /// </summary>
        public string? ErrorCode
        {
            get => _errorCode;
            set
            {
                _errorCode = value;
                SetData(DataKeyErrorCode, value);
            }
        }

        /// <summary>
        /// Gets or sets the table involved in the failure, when known.
        /// </summary>
        public string? TableName
        {
            get => _tableName;
            set
            {
                _tableName = value;
                SetData(DataKeyTableName, value);
            }
        }

        /// <summary>
        /// Gets or sets an identifier the remote side attached to the failure so that support can find
        /// it in the server's logs, when any.
        /// </summary>
        public string? TraceId
        {
            get => _traceId;
            set
            {
                _traceId = value;
                SetData(DataKeyTraceId, value);
            }
        }

        /// <summary>
        /// Gets or sets a short, user-safe message the remote side attached to the failure, when any.
        /// </summary>
        public string? ServerMessage
        {
            get => _serverMessage;
            set
            {
                _serverMessage = value;
                SetData(DataKeyServerMessage, value);
            }
        }

        private void SetData(string key, string? value)
        {
            // Exception.Data can legitimately be unavailable (a derived type overriding it with null);
            // the classification must never be the thing that throws while reporting a failure.
            var data = Data;
            if (data == null || data.IsReadOnly)
            {
                return;
            }

            if (value == null)
            {
                data.Remove(key);
            }
            else
            {
                data[key] = value;
            }
        }
    }
}
