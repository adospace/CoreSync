using Microsoft.EntityFrameworkCore.Design;
using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync.Tests.Data
{
    internal class DesignTimeSqlServerBlogDbContextFactory : IDesignTimeDbContextFactory<SqlServerBlogDbContext>
    {
        public SqlServerBlogDbContext CreateDbContext(string[] args)
        {
            return new SqlServerBlogDbContext(IntegrationTests.ConnectionString);
        }
    }
}
