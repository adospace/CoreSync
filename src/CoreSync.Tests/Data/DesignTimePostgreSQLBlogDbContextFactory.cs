using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CoreSync.Tests.Data
{
    public class DesignTimePostgreSQLBlogDbContextFactory : IDesignTimeDbContextFactory<PostgreSQLBlogDbContext>
    {
        public PostgreSQLBlogDbContext CreateDbContext(string[] args)
        {
            return new PostgreSQLBlogDbContext(IntegrationTests.PostgreSQLConnectionString);
        }
    }
} 