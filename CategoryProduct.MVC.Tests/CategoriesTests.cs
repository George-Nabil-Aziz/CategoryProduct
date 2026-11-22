using System.Net;

namespace CategoryProduct.MVC.Tests;

public class CategoriesTests : MvcTestBase
{
    public CategoriesTests(MvcFactory factory) : base(factory) { }

    private const string Home = "/";

    private void ScriptIndex(int page = 1, int totalPages = 1, params (int id, string name)[] items)
    {
        var rows = items.Length > 0 ? items : new (int id, string name)[] { (1, "Books"), (2, "Toys") };
        Fake.On(HttpMethod.Get, "api/category", FakeResponse.Json(200,
            ApiJson.Paged(rows.Select(r => ApiJson.Category(r.id, r.name)), page, 5, totalPages: totalPages)));
    }

    private void ScriptCategory(int id = 3, string name = "Books") =>
        Fake.On(HttpMethod.Get, $"api/category/{id}", FakeResponse.Json(200, ApiJson.Category(id, name)));

    private static Dictionary<string, string> Form(string name, int? id = null)
    {
        var d = new Dictionary<string, string> { ["Name"] = name };
        if (id != null) d["Id"] = id.Value.ToString();
        return d;
    }

    // ---------- Index ----------

    [Fact]
    public async Task Index_anonymous_renders_rows_without_create_link_and_calls_api_without_bearer()
    {
        ScriptIndex(1, 3);
        var html = await GetHtmlAsync("/Categories");
        Assert.True(HtmlHelpers.HasText(html, "Books"));
        Assert.True(HtmlHelpers.HasText(html, "Toys"));
        Assert.False(HtmlHelpers.HasText(html, "Create New"));
        Assert.Contains("page=2", HtmlHelpers.Decode(html));
        var call = Fake.Single(HttpMethod.Get, "api/category");
        Assert.Equal("?page=1&pageSize=5&search=", call.Query);
        Assert.Null(call.Authorization);
    }

    [Fact]
    public async Task Index_page_and_search_reach_api_and_paging_links_keep_search()
    {
        ScriptIndex(2, 3);
        var html = await GetHtmlAsync("/Categories?page=2&search=foo");
        Assert.Equal("?page=2&pageSize=5&search=foo", Fake.Single(HttpMethod.Get, "api/category").Query);
        var decoded = HtmlHelpers.Decode(html);
        Assert.Contains("value=\"foo\"", decoded);
        Assert.Contains("page=1&search=foo", decoded);
        Assert.Contains("page=3&search=foo", decoded);
        Assert.Contains("Previous", html);
        Assert.Contains("Next", html);
    }

    [Fact]
    public async Task Index_page_zero_is_clamped_to_one()
    {
        ScriptIndex();
        await GetHtmlAsync("/Categories?page=0");
        Assert.StartsWith("?page=1&", Fake.Single(HttpMethod.Get, "api/category").Query);
    }

    [Fact]
    public async Task Index_admin_sees_create_link()
    {
        await SignInAdminAsync();
        ScriptIndex();
        var html = await GetHtmlAsync("/Categories");
        Assert.True(HtmlHelpers.HasText(html, "Create New"));
    }

    // ---------- Details ----------

    [Fact]
    public async Task Details_found_lists_products()
    {
        ScriptCategory(3, "Books");
        Fake.On(HttpMethod.Get, "api/category/3/products", FakeResponse.Json(200,
            new[] { ApiJson.Product(1, "Novel", categoryId: 3), ApiJson.Product(2, "Atlas", categoryId: 3) }));
        var html = await GetHtmlAsync("/Categories/Details/3");
        Assert.True(HtmlHelpers.HasText(html, "Books"));
        Assert.True(HtmlHelpers.HasText(html, "Novel"));
        Assert.True(HtmlHelpers.HasText(html, "Atlas"));
        Assert.Single(Fake.Calls(HttpMethod.Get, "api/category/3"));
        Assert.Single(Fake.Calls(HttpMethod.Get, "api/category/3/products"));
    }

    [Fact]
    public async Task Details_404()
    {
        Fake.On(HttpMethod.Get, "api/category/9", FakeResponse.Status(404));
        using var r = await GetAsync("/Categories/Details/9");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    // ---------- Create ----------

    [Fact]
    public async Task Create_get_renders_form_for_admin()
    {
        await SignInAdminAsync();
        var html = await GetHtmlAsync("/Categories/Create");
        Assert.True(HtmlHelpers.FormAction(html, "/Categories/Create"));
    }

    [Fact]
    public async Task Create_post_ok_redirects_and_sends_bearer_and_name()
    {
        await SignInAdminAsync(token: "tok-a");
        Fake.On(HttpMethod.Post, "api/category", FakeResponse.Status(201));
        var resp = await PostFormAsync("/Categories/Create", Form("Gadgets"));
        AssertRedirect(resp, Home);
        var call = Fake.Single(HttpMethod.Post, "api/category");
        Assert.Equal("Bearer tok-a", call.Authorization);
        Assert.Equal("Gadgets", call.Json().GetProperty("name").GetString());
    }

    [Fact]
    public async Task Create_post_api_400_shows_field_message()
    {
        await SignInAdminAsync();
        Fake.On(HttpMethod.Post, "api/category", FakeResponse.Json(400, ApiJson.ValidationProblem("Name", "Name is bad.")));
        var resp = await PostFormAsync("/Categories/Create", Form("Gadgets"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("Name is bad.", HtmlHelpers.FieldError(await resp.Content.ReadAsStringAsync(), "Name"));
    }

    [Fact]
    public async Task Create_post_api_409_shows_detail()
    {
        await SignInAdminAsync();
        Fake.On(HttpMethod.Post, "api/category", FakeResponse.Json(409, ApiJson.ConflictProblem("Category exists.")));
        var resp = await PostFormAsync("/Categories/Create", Form("Gadgets"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("Category exists.", HtmlHelpers.FieldError(await resp.Content.ReadAsStringAsync(), "") ?? "");
    }

    [Fact]
    public async Task Create_post_api_403_shows_permission_message()
    {
        await SignInAdminAsync();
        Fake.On(HttpMethod.Post, "api/category", FakeResponse.Status(403));
        var resp = await PostFormAsync("/Categories/Create", Form("Gadgets"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("You don't have permission to make changes.",
            HtmlHelpers.FieldError(await resp.Content.ReadAsStringAsync(), "") ?? "");
    }

    [Fact]
    public async Task Create_post_api_401_with_failed_refresh_redirects_to_login_and_clears_cookie()
    {
        await SignInAdminAsync(refresh: "r-1");
        Fake.On(HttpMethod.Post, "api/category", FakeResponse.Status(401));
        Fake.On(HttpMethod.Post, "api/account/refresh", FakeResponse.Status(401));
        var resp = await PostFormAsync("/Categories/Create", Form("Gadgets"));
        AssertLoginRedirect(resp, "/Categories/Create");
        Assert.Contains(resp.Headers.GetValues("Set-Cookie"), c => c.StartsWith(".AspNetCore.Cookies=;", StringComparison.Ordinal));
        Assert.Single(Fake.Calls(HttpMethod.Post, "api/account/refresh"));
        // cookie is gone: the next admin page bounces to login without API calls
        var next = await GetAsync("/Categories/Create");
        AssertLoginRedirect(next, "/Categories/Create");
    }

    [Fact]
    public async Task Create_post_invalid_model_does_not_call_api()
    {
        await SignInAdminAsync();
        var resp = await PostFormAsync("/Categories/Create", Form("a"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.NotNull(HtmlHelpers.FieldError(await resp.Content.ReadAsStringAsync(), "Name"));
        Assert.Empty(Fake.Requests);
    }

    // ---------- Edit ----------

    [Fact]
    public async Task Edit_get_prefills_form()
    {
        await SignInAdminAsync();
        ScriptCategory(3, "Books");
        var html = await GetHtmlAsync("/Categories/Edit/3");
        Assert.Contains("value=\"Books\"", html);
    }

    [Fact]
    public async Task Edit_get_404()
    {
        await SignInAdminAsync();
        Fake.On(HttpMethod.Get, "api/category/3", FakeResponse.Status(404));
        using var r = await GetAsync("/Categories/Edit/3");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Edit_post_ok_puts_name_with_bearer()
    {
        await SignInAdminAsync(token: "tok-a");
        ScriptCategory(3, "Books");
        Fake.On(HttpMethod.Put, "api/category/3", FakeResponse.Status(204));
        var resp = await PostFormAsync("/Categories/Edit/3", Form("Novels", 3));
        AssertRedirect(resp, Home);
        var call = Fake.Single(HttpMethod.Put, "api/category/3");
        Assert.Equal("Bearer tok-a", call.Authorization);
        Assert.Equal("Novels", call.Json().GetProperty("name").GetString());
    }

    [Fact]
    public async Task Edit_post_api_404_is_404()
    {
        await SignInAdminAsync();
        ScriptCategory(3);
        Fake.On(HttpMethod.Put, "api/category/3", FakeResponse.Status(404));
        var resp = await PostFormAsync("/Categories/Edit/3", Form("Novels", 3));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Edit_post_api_400_rerenders_form()
    {
        await SignInAdminAsync();
        ScriptCategory(3);
        Fake.On(HttpMethod.Put, "api/category/3", FakeResponse.Json(400, ApiJson.ValidationProblem("Name", "Name is bad.")));
        var resp = await PostFormAsync("/Categories/Edit/3", Form("Novels", 3));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("Name is bad.", HtmlHelpers.FieldError(await resp.Content.ReadAsStringAsync(), "Name"));
    }

    // ---------- Delete ----------

    [Fact]
    public async Task Delete_get_shows_confirmation()
    {
        await SignInAdminAsync();
        ScriptCategory(3, "Books");
        var html = await GetHtmlAsync("/Categories/Delete/3");
        Assert.True(HtmlHelpers.HasText(html, "Books"));
        Assert.True(HtmlHelpers.FormAction(html, "/Categories/Delete/3"));
    }

    [Fact]
    public async Task Delete_post_ok_redirects_and_sends_bearer()
    {
        await SignInAdminAsync(token: "tok-a");
        ScriptCategory(3);
        Fake.On(HttpMethod.Delete, "api/category/3", FakeResponse.Status(204));
        var resp = await PostFormAsync("/Categories/Delete/3", new Dictionary<string, string>());
        AssertRedirect(resp, Home);
        Assert.Equal("Bearer tok-a", Fake.Single(HttpMethod.Delete, "api/category/3").Authorization);
    }

    [Fact]
    public async Task Delete_post_api_409_rerenders_delete_page_with_message()
    {
        await SignInAdminAsync();
        ScriptCategory(3, "Books");
        Fake.On(HttpMethod.Delete, "api/category/3", FakeResponse.Json(409, ApiJson.ConflictProblem("Category has products.")));
        var resp = await PostFormAsync("/Categories/Delete/3", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Contains("Category has products.", HtmlHelpers.FieldError(html, "") ?? "");
        Assert.True(HtmlHelpers.HasText(html, "Are you sure you want to delete this?"));
    }

    [Fact]
    public async Task Delete_post_api_404_is_404()
    {
        await SignInAdminAsync();
        ScriptCategory(3);
        Fake.On(HttpMethod.Delete, "api/category/3", FakeResponse.Status(404));
        var resp = await PostFormAsync("/Categories/Delete/3", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
