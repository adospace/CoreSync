using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CoreSync
{
    /// <summary>
    /// Wraps a column value along with its detected <see cref="SyncItemValueType"/> for type-safe serialization
    /// during synchronization.
    /// </summary>
    /// <remarks>
    /// Types that have no dedicated <see cref="SyncItemValueType"/> are normalized to one that does, so that
    /// the wire format stays unchanged and peers running an older version of the library keep working:
    /// <list type="bullet">
    /// <item><description><see cref="byte"/> (SQL <c>tinyint</c>), <see cref="sbyte"/> and <see cref="ushort"/>
    /// become <see cref="int"/> / <see cref="SyncItemValueType.Int32"/>;</description></item>
    /// <item><description><see cref="uint"/> becomes <see cref="long"/> / <see cref="SyncItemValueType.Int64"/>;</description></item>
    /// <item><description><see cref="TimeSpan"/> (SQL <c>time</c>) becomes an invariant <c>"c"</c> formatted
    /// <see cref="string"/> / <see cref="SyncItemValueType.String"/>, which is also how both Microsoft.Data.Sqlite
    /// and EF Core persist a <see cref="TimeSpan"/>;</description></item>
    /// <item><description><see cref="char"/> becomes a single character <see cref="string"/>.</description></item>
    /// </list>
    /// </remarks>
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
            DetectTypeOfObject(value);
        }

        private void DetectTypeOfObject(object? value)
        {
            if (value == null || value is DBNull)
            {
                Value = null;
                Type = SyncItemValueType.Null;
            }
            else if (value is string)
            {
                Value = value;
                Type = SyncItemValueType.String;
            }
            else if (value is bool)
            {
                Value = value;
                Type = SyncItemValueType.Boolean;
            }
            else if (value is byte[])
            {
                Value = value;
                Type = SyncItemValueType.ByteArray;
            }
            else if (value is DateTime)
            {
                Value = value;
                Type = SyncItemValueType.DateTime;
            }
            else if (value is double)
            {
                Value = value;
                Type = SyncItemValueType.Double;
            }
            else if (value is int)
            {
                Value = value;
                Type = SyncItemValueType.Int32;
            }
            else if (value is float)
            {
                Value = value;
                Type = SyncItemValueType.Float;
            }
            else if (value is Guid)
            {
                Value = value;
                Type = SyncItemValueType.Guid;
            }
            else if (value is long)
            {
                Value = value;
                Type = SyncItemValueType.Int64;
            }
            else if (value is short)
            {
                Value = value;
                Type = SyncItemValueType.Int32;
            }
            else if (value is decimal)
            {
                Value = value;
                Type = SyncItemValueType.Decimal;
            }
            //types below have no dedicated SyncItemValueType: they are widened/formatted to an
            //existing one so that the serialized payload remains readable by any version of the library
            else if (value is byte byteValue)
            {
                Value = (int)byteValue;
                Type = SyncItemValueType.Int32;
            }
            else if (value is sbyte sbyteValue)
            {
                Value = (int)sbyteValue;
                Type = SyncItemValueType.Int32;
            }
            else if (value is ushort ushortValue)
            {
                Value = (int)ushortValue;
                Type = SyncItemValueType.Int32;
            }
            else if (value is uint uintValue)
            {
                Value = (long)uintValue;
                Type = SyncItemValueType.Int64;
            }
            else if (value is TimeSpan timeSpanValue)
            {
                //"c" is the invariant [-][d.]hh:mm:ss[.fffffff] format used by Microsoft.Data.Sqlite
                //and EF Core to store a TimeSpan as text. Ticks must never be used: Microsoft.Data.Sqlite
                //reads an integer column into a TimeSpan as a number of days.
                Value = timeSpanValue.ToString("c", CultureInfo.InvariantCulture);
                Type = SyncItemValueType.String;
            }
            else if (value is char charValue)
            {
                Value = charValue.ToString();
                Type = SyncItemValueType.String;
            }
            else
            {
                throw new NotSupportedException($"Type of value ('{value.GetType()}') is not supported for synchronization");
            }
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
