using System;
using System.Collections.Generic;
using System.Text;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;

namespace CoreSync.Tests.Data
{
    public class SqlServerBlogDbContext : BlogDbContext
    {
        public SqlServerBlogDbContext([NotNull] string connectionString) : base(connectionString)
        {
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlServer(ConnectionString);

            base.OnConfiguring(optionsBuilder);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Post>()
                .HasOne(p => p.Author)
                .WithMany(b => b.Posts)
                .OnDelete(DeleteBehavior.Cascade);

            base.OnModelCreating(modelBuilder);
        }

        public override BlogDbContext Refresh()
        {
            Dispose();
            return new SqlServerBlogDbContext(ConnectionString);
        }
    }
}
