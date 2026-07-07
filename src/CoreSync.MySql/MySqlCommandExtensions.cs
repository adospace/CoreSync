using MySqlConnector;
using System.Threading;
using System.Threading.Tasks;

namespace CoreSync.MySql
{
    internal static class MySqlCommandExtensions
    {
        public static async Task<long> ExecuteLongScalarAsync(this MySqlCommand command, CancellationToken cancellationToken = default)
        {
            var result = await command.ExecuteScalarAsync(cancellationToken);

            if (result == null || result == System.DBNull.Value)
            {
                return 0;
            }

            return System.Convert.ToInt64(result);
        }
    }
}
