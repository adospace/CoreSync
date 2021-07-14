using Microsoft.EntityFrameworkCore.Migrations;

namespace CoreSync.Tests.Migrations.SqliteMigrations
{
    public partial class Version1 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Created",
                table: "Users",
                newName: "Date Created(date/$time)");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Date Created(date/$time)",
                table: "Users",
                newName: "Created");
        }
    }
}
