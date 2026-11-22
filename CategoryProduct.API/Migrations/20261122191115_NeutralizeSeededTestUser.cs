using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CategoryProduct.API.Migrations
{
    /// <inheritdoc />
    public partial class NeutralizeSeededTestUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data-only change (no model change). Neutralizes the account seeded by SeedTestUserForAuth
            // (and promoted to Admin by AddUserRole) when it still has the seeded password hash.
            // Refresh tokens are revoked first because that statement keys on the still-matching hash.
            // The marker '/w==' is valid base64 and verifies as Failed (login answers 401).
            migrationBuilder.Sql(@"
UPDATE rt SET RevokedAtUtc = SYSUTCDATETIME(), ConcurrencyStamp = NEWID()
FROM RefreshTokens rt INNER JOIN Users u ON u.Id = rt.UserId
WHERE u.UserName = 'testuser'
  AND u.PasswordHash = 'AQAAAAIAAYagAAAAENKkunRHnzjDucj641tHfwyMEdkA6lL7wKrwYWVP4LKRA2L9/9W536fomTkCKhKHSw=='
  AND rt.RevokedAtUtc IS NULL;

UPDATE Users SET PasswordHash = '/w==', Role = 'User'
WHERE UserName = 'testuser'
  AND PasswordHash = 'AQAAAAIAAYagAAAAENKkunRHnzjDucj641tHfwyMEdkA6lL7wKrwYWVP4LKRA2L9/9W536fomTkCKhKHSw==';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: restoring the known password would re-create the vulnerability.
        }
    }
}
