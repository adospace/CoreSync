
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CoreSync.SqlServer
{
    internal static class SqlCommandExtensions
    {
        public static async Task<long> ExecuteLongScalarAsync(this SqlCommand cmd, CancellationToken cancellationToken)
        {
            long version = 0;
            var res = await cmd.ExecuteScalarAsync();
            if (!(res is DBNull))
                version = (long)res;

            return version;
        }
    }
}
