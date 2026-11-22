using CategoryProduct.API.Models;
using Microsoft.AspNetCore.Identity;

namespace CategoryProduct.API.Tests
{
    public class SeededTestUserMarkerTests
    {
        // Hash inserted by migration SeedTestUserForAuth (also pinned in NeutralizeSeededTestUser).
        private const string SeededHash =
            "AQAAAAIAAYagAAAAENKkunRHnzjDucj641tHfwyMEdkA6lL7wKrwYWVP4LKRA2L9/9W536fomTkCKhKHSw==";

        private const string Marker = "/w==";

        [Theory]
        [InlineData("anything")]
        [InlineData("")]
        [InlineData("P@ssw0rd!")]
        public void Marker_NeverVerifies_AndDoesNotThrow(string password)
        {
            var user = new User { UserName = "testuser", PasswordHash = Marker };
            var result = new PasswordHasher<User>().VerifyHashedPassword(user, Marker, password);
            Assert.Equal(PasswordVerificationResult.Failed, result);
        }

        [Fact]
        public void SeededHash_WithWrongPassword_Fails()
        {
            var user = new User { UserName = "testuser", PasswordHash = SeededHash };
            var result = new PasswordHasher<User>().VerifyHashedPassword(user, SeededHash, "definitely-not-the-password");
            Assert.Equal(PasswordVerificationResult.Failed, result);
        }
    }
}
