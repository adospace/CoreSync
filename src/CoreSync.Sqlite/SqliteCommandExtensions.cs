using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CoreSync.Sqlite
{
    internal static class SqliteCommandExtensions
    {
        public static async Task<long> ExecuteLongScalarAsync(this SqliteCommand cmd, CancellationToken cancellationToken)
        {
            long version = 0;
            var res = await cmd.ExecuteScalarAsync(cancellationToken);
            if (!(res is DBNull))
                version = (long)res;

            return version;
        }
    }
}
