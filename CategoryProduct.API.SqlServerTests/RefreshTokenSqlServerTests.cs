using System.Net.Http.Json;

namespace CategoryProduct.API.SqlServerTests
{
    public class RefreshTokenSqlServerTests : IClassFixture<SqlServerApiFactory>
    {
        private readonly SqlServerApiFactory _f;
        public RefreshTokenSqlServerTests(SqlServerApiFactory f) => _f = f;

        [Fact]
        public async Task ParallelRefreshWithSameToken_AtMostOne200()
        {
            var name = _f.UniqueName("refresh");
            await _f.CreateUserAsync(name, "User");
            var (_, refresh) = await _f.LoginAsync(name);

            var responses = await Parallel8.RunAsync(8, _ => _f.CreateClient().PostAsJsonAsync(
                "api/account/refresh", new { refreshToken = refresh }));
            var statuses = responses.Select(r => (int)r.StatusCode).ToList();

            Assert.True(statuses.Count(s => s == 200) <= 1, "Statuses: " + string.Join(",", statuses));
            Assert.All(statuses.Where(s => s != 200), s => Assert.Equal(401, s));
        }
    }
}
