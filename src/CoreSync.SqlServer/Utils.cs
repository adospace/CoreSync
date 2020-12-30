using System;
using System.Collections.Generic;
using System.Data;
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

            return value.Value;
        }
    }
}
