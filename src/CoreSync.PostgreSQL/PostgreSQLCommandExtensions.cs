using Npgsql;
using System.Threading;
using System.Threading.Tasks;

namespace CoreSync.PostgreSQL
{
    internal static class PostgreSQLCommandExtensions
    {
        public static async Task<long> ExecuteLongScalarAsync(this NpgsqlCommand command, CancellationToken cancellationToken = default)
        {
            var result = await command.ExecuteScalarAsync(cancellationToken);
            
            if (result == null || result == System.DBNull.Value)
                return 0;
                
            return System.Convert.ToInt64(result);
        }
    }
} 