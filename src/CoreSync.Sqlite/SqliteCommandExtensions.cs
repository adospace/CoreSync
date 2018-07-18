using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace CoreSync.Sqlite
{
    internal static class SqliteCommandExtensions
    {
        public static async Task<long> ExecuteLongScalarAsync(this SqliteCommand cmd)
        {
            long version = 0;
            var res = await cmd.ExecuteScalarAsync();
            if (!(res is DBNull))
                version = (long)res;

            return version;
        }
    }
}
