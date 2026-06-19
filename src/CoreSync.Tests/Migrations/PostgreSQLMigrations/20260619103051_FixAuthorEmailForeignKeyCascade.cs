using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreSync.Tests.Migrations.PostgreSQLMigrations
{
    /// <inheritdoc />
    public partial class FixAuthorEmailForeignKeyCascade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Posts_Users_AuthorEmail",
                table: "Posts");

            migrationBuilder.AddForeignKey(
                name: "FK_Posts_Users_AuthorEmail",
                table: "Posts",
                column: "AuthorEmail",
                principalTable: "Users",
                principalColumn: "Email");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Posts_Users_AuthorEmail",
                table: "Posts");

            migrationBuilder.AddForeignKey(
                name: "FK_Posts_Users_AuthorEmail",
                table: "Posts",
                column: "AuthorEmail",
                principalTable: "Users",
                principalColumn: "Email",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
