# CategoryProduct.MVC.Tests

Integration tests for the MVC app. The real MVC host runs in-process (`WebApplicationFactory<Program>`); every
`HttpClient` it creates talks to `FakeApi` instead of the network, so `CategoryProductApiClient`, `ApiErrorHandler`
and `TokenRefreshHandler` are real code under test.

## Writing a test

Derive from `MvcTestBase` (one host per class, fresh `Fake` and `Client` per test). Script the API routes the page
calls, drive the app through `Client`, assert on the response and on `Fake.Requests`.

```csharp
public class ExampleTests : MvcTestBase
{
    public ExampleTests(MvcFactory factory) : base(factory) { }

    [Fact]
    public async Task Admin_sees_users_and_api_gets_bearer()
    {
        await SignInAdminAsync(token: "tok");                       // real login form; clears the request log
        Fake.On(HttpMethod.Get, "api/users",
            FakeResponse.Json(200, ApiJson.Paged(new[] { ApiJson.User(1, "alice") })));

        var html = await GetHtmlAsync("/Users");                    // asserts 200

        Assert.True(HtmlHelpers.HasText(html, "alice"));
        Assert.Equal("Bearer tok", Fake.Single(HttpMethod.Get, "api/users").Authorization);
    }

    [Fact]
    public async Task Post_with_antiforgery_then_redirect()
    {
        Fake.On(HttpMethod.Post, "api/account/register", FakeResponse.Status(201));
        ScriptLogin("User");                                        // register logs the new user in
        var resp = await PostFormAsync("/Account/Register", new Dictionary<string, string>
            { ["UserName"] = "bob", ["Password"] = "P@ssw0rd!", ["ConfirmPassword"] = "P@ssw0rd!" });
        // the token is fetched from GET /Account/Register automatically (override with tokenPageUrl:)
        AssertRedirect(resp, "/"); // or: var next = await FollowAsync(resp);
    }
}
```

## Helper reference

- `MvcTestBase`: `Fake`, `Client`, `Factory`, `GetAsync`, `GetHtmlAsync` (asserts 200), `PostFormAsync(url, fields, tokenPageUrl?)`,
  `PostFormNoTokenAsync`, `PostMultipartAsync(url, fields, fileName, contentType, bytes, tokenPageUrl?, fileField = "file")`,
  `ScriptLogin`, `SignInUserAsync(name)`, `SignInAdminAsync(name)`, `FollowAsync(response)`, `AssertRedirect(response, pathAndQuery)`,
  `AssertLoginRedirect(response, returnPath)`, `NewAnonymousClient()`.
- `FakeApi`: `On` (fixed or computed), `OnQuery`, `OnSequence`, `Throw`, `Hang`, `Reset`, `ClearRequests`, `Requests`, `Calls`, `Single`.
  Unscripted calls throw `FakeApiUnmatchedException` listing the scripted routes. Later registrations win.
  Routes match method + path only (case-insensitive, no query); use `OnQuery` to distinguish query strings.
- `FakeResponse`: `Json`, `Status`, `Bytes`, `Raw`. `RecordedRequest`: `Method`, `Path`, `Query`, `Authorization`, `ContentType`, `BodyText`, `BodyBytes`, `Json()`.
- `ApiJson`: `Login`, `Paged`, `Category`, `Product`, `StockMovement`, `Cart`/`CartLine`, `Order`/`OrderLine`, `OrderSummary`, `User`, `ValidationProblem`, `ConflictProblem`.
- `HtmlHelpers`: `ExtractAntiforgeryToken`, `Decode`, `HasText`, `Alert`/`Alerts`, `FormAction`, `FieldError`.

Run: `dotnet test CategoryProduct.MVC.Tests` (filter with `--filter "FullyQualifiedName~ClassName"`).
