using Microsoft.EntityFrameworkCore;
using System;

namespace CoreSync.Tests.Data
{
    public class PostgreSQLBlogDbContext : BlogDbContext
    {
        public PostgreSQLBlogDbContext(string connectionString) : base(connectionString)
        {
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseNpgsql(ConnectionString);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("Users");
                entity.Property(e => e.Created).HasColumnType("timestamp");
            });

            modelBuilder.Entity<Post>(entity =>
            {
                entity.ToTable("Posts");
                entity.Property(e => e.Updated).HasColumnType("timestamp");
            });

            modelBuilder.Entity<Comment>(entity =>
            {
                entity.ToTable("Comments");
                entity.Property(e => e.Created).HasColumnType("timestamp");
            });
        }

        public override BlogDbContext Refresh()
        {
            Dispose();
            return new PostgreSQLBlogDbContext(ConnectionString);
        }
    }
} 