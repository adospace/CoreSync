using Microsoft.Data.SqlClient;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CoreSync.SqlServerCT
{
    internal static class SqlCommandExtensions
    {
        public static async Task<long> ExecuteLongScalarAsync(this SqlCommand cmd, CancellationToken cancellationToken)
        {
            long version = 0;
            var res = await cmd.ExecuteScalarAsync(cancellationToken);
            if (res != null && !(res is DBNull))
                version = Convert.ToInt64(res);

            return version;
        }
    }
}
