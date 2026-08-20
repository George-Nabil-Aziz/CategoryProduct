using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CategoryProduct.API.Migrations
{
    /// <inheritdoc />
    public partial class SeedTestUserForAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "UserName", "PasswordHash" },
                values: new object[]
                {
                    "testuser",
                    "AQAAAAIAAYagAAAAENKkunRHnzjDucj641tHfwyMEdkA6lL7wKrwYWVP4LKRA2L9/9W536fomTkCKhKHSw=="
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "UserName",
                keyValue: "testuser");
        }
    }
}
