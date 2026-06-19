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

                // Npgsql 9 defaults this optional FK to no cascade, unlike the
                // SqlServer/Sqlite providers. Keep cascade delete so deleting a
                // User removes its Posts, matching the other providers and the
                // existing migration/snapshot.
                entity.HasOne(e => e.Author)
                    .WithMany(u => u.Posts)
                    .OnDelete(DeleteBehavior.Cascade);
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