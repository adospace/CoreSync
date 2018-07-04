using Microsoft.EntityFrameworkCore.Design;
using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync.Tests.Data
{
    internal class DesignTimeSampleDbContextFactory : IDesignTimeDbContextFactory<SampleDbContext>
    {

        public SampleDbContext CreateDbContext(string[] args)
        {
            return new SampleDbContext(IntegrationTests.ConnectionString);
        }
    }
}
