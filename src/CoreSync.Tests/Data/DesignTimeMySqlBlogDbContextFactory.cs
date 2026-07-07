using Microsoft.EntityFrameworkCore.Design;

namespace CoreSync.Tests.Data
{
    public class DesignTimeMySqlBlogDbContextFactory : IDesignTimeDbContextFactory<MySqlBlogDbContext>
    {
        public MySqlBlogDbContext CreateDbContext(string[] args)
        {
            return new MySqlBlogDbContext(IntegrationTests.MySqlConnectionString);
        }
    }
}
