using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync
{
    /// <summary>
    /// Identifies the data type of a <see cref="SyncItemValue"/> for cross-database serialization.
    /// </summary>
    public enum SyncItemValueType
    {
        /// <summary>
        /// The value is null.
        /// </summary>
        Null,

        /// <summary>
        /// The value is a string.
        /// </summary>
        String,

        /// <summary>
        /// The value is a 32-bit integer.
        /// </summary>
        Int32,

        /// <summary>
        /// The value is a 64-bit integer.
        /// </summary>
        Int64,

        /// <summary>
        /// The value is a single-precision floating-point number.
        /// </summary>
        Float,

        /// <summary>
        /// The value is a double-precision floating-point number.
        /// </summary>
        Double,

        /// <summary>
        /// The value is a date and time.
        /// </summary>
        DateTime,

        /// <summary>
        /// The value is a boolean.
        /// </summary>
        Boolean,

        /// <summary>
        /// The value is a byte array (binary data).
        /// </summary>
        ByteArray,

        /// <summary>
        /// The value is a globally unique identifier.
        /// </summary>
        Guid,

        /// <summary>
        /// The value is a decimal number.
        /// </summary>
        Decimal
    }
}
