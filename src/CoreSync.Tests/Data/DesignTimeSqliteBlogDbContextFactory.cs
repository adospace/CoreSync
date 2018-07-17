using Microsoft.EntityFrameworkCore.Design;
using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync.Tests.Data
{
    internal class DesignTimeSqliteBlogDbContextFactory : IDesignTimeDbContextFactory<SqliteBlogDbContext>
    {
        public SqliteBlogDbContext CreateDbContext(string[] args)
        {
            return new SqliteBlogDbContext(IntegrationTests.ConnectionString);
        }
    }
}
