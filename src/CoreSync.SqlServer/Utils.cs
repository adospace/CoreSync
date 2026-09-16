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
            if (dbType == SqlDbType.Time &&
                value.Value is string timeValue)
                return TimeSpan.Parse(timeValue, CultureInfo.InvariantCulture);

            //a tinyint column travels as an Int32 (or as a boolean, from MySql TINYINT(1)): SqlParameter
            //does not narrow it on its own
            if (dbType == SqlDbType.TinyInt &&
                value.Value is byte == false)
                return Convert.ToByte(value.Value, CultureInfo.InvariantCulture);

            return value.Value;
        }
    }
}
