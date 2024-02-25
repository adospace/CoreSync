﻿// <auto-generated />
using System;
using CoreSync.Tests.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CoreSync.Tests.Migrations.SqlServerMigrations
{
    [DbContext(typeof(SqlServerBlogDbContext))]
    [Migration("20210714121146_Version1")]
    partial class Version1
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "3.1.11")
                .HasAnnotation("Relational:MaxIdentifierLength", 128)
                .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

            modelBuilder.Entity("CoreSync.Tests.Data.Comment", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("AuthorEmail")
                        .HasColumnType("nvarchar(450)");

                    b.Property<string>("Content")
                        .HasColumnType("nvarchar(max)");

                    b.Property<DateTime>("Created")
                        .HasColumnType("datetime2");

                    b.Property<Guid?>("PostId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid?>("ReplyToId")
                        .HasColumnType("uniqueidentifier");

                    b.HasKey("Id");

                    b.HasIndex("AuthorEmail");

                    b.HasIndex("PostId");

                    b.HasIndex("ReplyToId");

                    b.ToTable("Comments");
                });

            modelBuilder.Entity("CoreSync.Tests.Data.Post", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("AuthorEmail")
                        .HasColumnType("nvarchar(450)");

                    b.Property<int>("Claps")
                        .HasColumnType("int");

                    b.Property<string>("Content")
                        .HasColumnType("nvarchar(max)");

                    b.Property<float>("Stars")
                        .HasColumnType("real");

                    b.Property<string>("Title")
                        .HasColumnType("nvarchar(max)");

                    b.Property<DateTime>("Updated")
                        .HasColumnType("datetime2");

                    b.HasKey("Id");

                    b.HasIndex("AuthorEmail");

                    b.ToTable("Posts");
                });

            modelBuilder.Entity("CoreSync.Tests.Data.User", b =>
                {
                    b.Property<string>("Email")
                        .HasColumnType("nvarchar(450)");

                    b.Property<DateTime>("Created")
                        .HasColumnName("Date Created(date/$time)")
                        .HasColumnType("datetime2");

                    b.Property<string>("Name")
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("Email");

                    b.ToTable("Users");
                });

            modelBuilder.Entity("CoreSync.Tests.Data.Comment", b =>
                {
                    b.HasOne("CoreSync.Tests.Data.User", "Author")
                        .WithMany("Comments")
                        .HasForeignKey("AuthorEmail");

                    b.HasOne("CoreSync.Tests.Data.Post", "Post")
                        .WithMany("Comments")
                        .HasForeignKey("PostId");

                    b.HasOne("CoreSync.Tests.Data.Comment", "ReplyTo")
                        .WithMany()
                        .HasForeignKey("ReplyToId");
                });

            modelBuilder.Entity("CoreSync.Tests.Data.Post", b =>
                {
                    b.HasOne("CoreSync.Tests.Data.User", "Author")
                        .WithMany("Posts")
                        .HasForeignKey("AuthorEmail")
                        .OnDelete(DeleteBehavior.Cascade);
                });
#pragma warning restore 612, 618
        }
    }
}