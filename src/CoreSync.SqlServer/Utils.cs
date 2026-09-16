using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Text;

namespace CoreSync.SqlServer
{
    internal static class Utils
    {
        public static object ConvertToSqlType(SyncItemValue value, SqlDbType dbType)
        {
            if (value.Value == null)
                return DBNull.Value;

            if (dbType == SqlDbType.UniqueIdentifier &&
                value.Value is string)
                return Guid.Parse(value.Value.ToString());

            if (dbType == SqlDbType.Decimal &&
                value.Value is decimal == false)
                return Convert.ToDecimal(value.Value, CultureInfo.InvariantCulture);

            //a time column travels as an invariant "c" formatted string (see SyncItemValue): it has no
            //dedicated SyncItemValueType and that is also how SQLite stores it
            if (dbType == SqlDbType.Time)
            {
                if (value.Value is string timeValue)
                    return ValidateTimeOfDay(TimeSpan.Parse(timeValue, CultureInfo.InvariantCulture));

                if (value.Value is TimeSpan timeSpanValue)
                    return ValidateTimeOfDay(timeSpanValue);
            }

            //a tinyint column travels as an Int32 (or as a boolean, from MySql TINYINT(1)): SqlParameter
            //does not narrow it on its own
            if (dbType == SqlDbType.TinyInt &&
                value.Value is byte == false)
                return Convert.ToByte(value.Value, CultureInfo.InvariantCulture);

            return value.Value;
        }

        /// <summary>
        /// A SQL Server <c>time</c> column is a time of day: 00:00:00.0000000 to 23:59:59.9999999.
        /// Other stores are wider - a MySql <c>TIME</c> spans -838:59:59 to 838:59:59 and a PostgreSQL
        /// <c>interval</c> is unbounded - so a value coming from one of them can be out of range here.
        /// Fail with a message naming the value instead of letting it surface as an opaque
        /// SqlClient/SQL Server error, and never silently truncate it: that would be data loss.
        /// </summary>
        private static object ValidateTimeOfDay(TimeSpan value)
        {
            if (value < TimeSpan.Zero || value >= TimeSpan.FromDays(1))
            {
                throw new NotSupportedException(
                    $"Value '{value.ToString("c", CultureInfo.InvariantCulture)}' cannot be applied to a SQL Server 'time' column: " +
                    "it only holds a time of day, from 00:00:00.0000000 to 23:59:59.9999999. " +
                    "Map the source column to a wider type (for example bigint holding ticks) to synchronize it.");
            }

            return value;
        }
    }
}
