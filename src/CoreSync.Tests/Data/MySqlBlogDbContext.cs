using Microsoft.EntityFrameworkCore;
using System;

namespace CoreSync.Tests.Data
{
    public class MySqlBlogDbContext : BlogDbContext
    {
        private static readonly MySqlServerVersion ServerVersion = new(new Version(8, 4, 0));

        public MySqlBlogDbContext(string connectionString) : base(connectionString)
        {
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseMySql(ConnectionString, ServerVersion);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("Users");
                entity.Property(e => e.Email).HasMaxLength(255);
                entity.Property(e => e.Created).HasColumnType("datetime(6)");
            });

            modelBuilder.Entity<Post>(entity =>
            {
                entity.ToTable("Posts");
                entity.Property(e => e.Id).HasColumnType("char(36)");
                entity.Property(e => e.Updated).HasColumnType("datetime(6)");
                entity.Property<string?>("AuthorEmail").HasMaxLength(255);
                entity.HasOne(e => e.Author)
                    .WithMany(u => u.Posts)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Comment>(entity =>
            {
                entity.ToTable("Comments");
                entity.Property(e => e.Id).HasColumnType("char(36)");
                entity.Property(e => e.Created).HasColumnType("datetime(6)");
                entity.Property<Guid?>("PostId").HasColumnType("char(36)");
                entity.Property<Guid?>("ReplyToId").HasColumnType("char(36)");
                entity.Property<string?>("AuthorEmail").HasMaxLength(255);
            });
        }

        public override BlogDbContext Refresh()
        {
            Dispose();
            return new MySqlBlogDbContext(ConnectionString);
        }
    }
}
