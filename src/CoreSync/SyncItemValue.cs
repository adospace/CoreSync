using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync
{
    /// <summary>
    /// Wraps a column value along with its detected <see cref="SyncItemValueType"/> for type-safe serialization
    /// during synchronization.
    /// </summary>
    public class SyncItemValue
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SyncItemValue"/> class for deserialization.
        /// </summary>
        public SyncItemValue()
        { }

        /// <summary>
        /// Initializes a new instance of the <see cref="SyncItemValue"/> class, automatically
        /// detecting the <see cref="Type"/> from the runtime type of <paramref name="value"/>.
        /// </summary>
        /// <param name="value">The column value. <see cref="DBNull"/> is treated as <c>null</c>.</param>
        /// <exception cref="NotSupportedException">The runtime type of <paramref name="value"/> is not supported.</exception>
        public SyncItemValue(object? value)
        {
            Value = value == DBNull.Value ? null : value;
            DetectTypeOfObject(value);
        }

        private void DetectTypeOfObject(object? value)
        {
            if (value == null || value is DBNull)
                Type = SyncItemValueType.Null;
            else if (value is string)
                Type = SyncItemValueType.String;
            else if (value is bool)
                Type = SyncItemValueType.Boolean;
            else if (value is byte[])
                Type = SyncItemValueType.ByteArray;
            else if (value is DateTime)
                Type = SyncItemValueType.DateTime;
            else if (value is double)
                Type = SyncItemValueType.Double;
            else if (value is int)
                Type = SyncItemValueType.Int32;
            else if (value is float)
                Type = SyncItemValueType.Float;
            else if (value is Guid)
                Type = SyncItemValueType.Guid;
            else if (value is long)
                Type = SyncItemValueType.Int64;
            else if (value is short)
                Type = SyncItemValueType.Int32;
            else if (value is decimal)
                Type = SyncItemValueType.Decimal;
            else
                throw new NotSupportedException($"Type of value ('{value.GetType()}') is not supported for synchronization");
        }

        /// <summary>
        /// Gets or sets the raw column value. May be <c>null</c> for NULL database values.
        /// </summary>
        public object? Value { get; set; }

        /// <summary>
        /// Gets or sets the detected type of the value for serialization purposes.
        /// </summary>
        public SyncItemValueType Type { get; set; }
    }
}
