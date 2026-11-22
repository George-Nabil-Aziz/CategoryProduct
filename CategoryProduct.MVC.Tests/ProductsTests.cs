using System.Net;

namespace CategoryProduct.MVC.Tests;

public class ProductsTests : MvcTestBase
{
    public ProductsTests(MvcFactory factory) : base(factory) { }

    private void ScriptCategories() =>
        Fake.On(HttpMethod.Get, "api/category", FakeResponse.Json(200,
            ApiJson.Paged(new[] { ApiJson.Category(1, "Books"), ApiJson.Category(2, "Toys") })));

    private void ScriptProductList(params object[] items) =>
        Fake.On(HttpMethod.Get, "api/product", FakeResponse.Json(200,
            ApiJson.Paged(items.Length > 0 ? items : new[] { ApiJson.Product(1, "Novel", 12m, 4, 1), ApiJson.Product(2, "Robot", 30m, 2, 2) })));

    private void ScriptProduct(int id = 5, string name = "Pen", int quantity = 7, int categoryId = 2, string? imageUrl = null)
    {
        Fake.On(HttpMethod.Get, $"api/product/{id}", FakeResponse.Json(200, ApiJson.Product(id, name, 12m, quantity, categoryId, imageUrl)));
        ScriptCategories();
        Fake.On(HttpMethod.Get, $"api/category/{categoryId}", FakeResponse.Json(200, ApiJson.Category(categoryId, categoryId == 2 ? "Toys" : "Books")));
    }

    private static Dictionary<string, string> Form(string name = "Pen", string price = "12", string quantity = "7", string categoryId = "2", int? id = null)
    {
        var d = new Dictionary<string, string> { ["Name"] = name, ["Price"] = price, ["Quantity"] = quantity, ["CategoryId"] = categoryId };
        if (id != null) d["Id"] = id.Value.ToString();
        return d;
    }

    // ---------- Index ----------

    [Fact]
    public async Task Index_anonymous_with_filters_sends_query_resolves_categories_and_hides_actions()
    {
        ScriptCategories();
        ScriptProductList();
        var html = await GetHtmlAsync("/Products?search=x&categoryId=2&minPrice=1.5&maxPrice=9.99&sortBy=price&desc=true");

        var q = Fake.Single(HttpMethod.Get, "api/product").Query;
        Assert.Equal("?page=1&pageSize=5&search=x&categoryId=2&minPrice=1.5&maxPrice=9.99&sortBy=price&desc=true", q);
        Assert.NotEmpty(Fake.Calls(HttpMethod.Get, "api/category"));

        Assert.True(HtmlHelpers.HasText(html, "Novel"));
        Assert.True(HtmlHelpers.HasText(html, "Books"));   // category name resolved for product 1
        Assert.True(HtmlHelpers.HasText(html, "Toys"));
        Assert.False(HtmlHelpers.FormAction(html, "/Cart/Add"));
        Assert.False(HtmlHelpers.HasText(html, "Add to cart"));
        Assert.False(HtmlHelpers.HasText(html, "Create New"));
        Assert.DoesNotContain("/Products/Edit/", html);
        Assert.DoesNotContain("/Products/Delete/", html);
    }

    [Fact]
    public async Task Index_user_sees_add_to_cart_form_with_return_url()
    {
        await SignInUserAsync();
        ScriptCategories();
        ScriptProductList();
        var html = await GetHtmlAsync("/Products");
        Assert.True(HtmlHelpers.FormAction(html, "/Cart/Add"));
        Assert.True(HtmlHelpers.HasText(html, "Add to cart"));
        Assert.Contains("name=\"returnUrl\" value=\"/Products\"", HtmlHelpers.Decode(html));
    }

    [Fact]
    public async Task Index_admin_sees_create_edit_delete_links()
    {
        await SignInAdminAsync();
        ScriptCategories();
        ScriptProductList();
        var html = await GetHtmlAsync("/Products");
        Assert.True(HtmlHelpers.HasText(html, "Create New"));
        Assert.Contains("/Products/Edit/1", html);
        Assert.Contains("/Products/Delete/1", html);
    }

    [Fact]
    public async Task Index_item_with_image_uses_mvc_proxy_url_never_api_host()
    {
        ScriptCategories();
        ScriptProductList(ApiJson.Product(1, "Novel", imageUrl: "http://fake-api/images/1.png"));
        var html = await GetHtmlAsync("/Products");
        Assert.Contains("<img src=\"/Products/Image/1\"", html);
        Assert.DoesNotContain("fake-api", html);
    }

    // ---------- Details ----------

    [Fact]
    public async Task Details_anonymous_shows_fields_and_no_upload_form()
    {
        ScriptProduct(5, "Pen", 7, 2);
        var html = await GetHtmlAsync("/Products/Details/5");
        Assert.True(HtmlHelpers.HasText(html, "Pen"));
        Assert.True(HtmlHelpers.HasText(html, "Toys"));
        Assert.False(HtmlHelpers.FormAction(html, "/Products/UploadImage/5"));
        Assert.False(HtmlHelpers.FormAction(html, "/Cart/Add"));
    }

    [Fact]
    public async Task Details_user_sees_add_to_cart_but_no_upload_form()
    {
        await SignInUserAsync();
        ScriptProduct();
        var html = await GetHtmlAsync("/Products/Details/5");
        Assert.True(HtmlHelpers.FormAction(html, "/Cart/Add"));
        Assert.False(HtmlHelpers.FormAction(html, "/Products/UploadImage/5"));
    }

    [Fact]
    public async Task Details_admin_sees_upload_form()
    {
        await SignInAdminAsync();
        ScriptProduct();
        var html = await GetHtmlAsync("/Products/Details/5");
        Assert.True(HtmlHelpers.FormAction(html, "/Products/UploadImage/5"));
        Assert.True(HtmlHelpers.HasText(html, "Upload image"));
    }

    [Fact]
    public async Task Details_404()
    {
        Fake.On(HttpMethod.Get, "api/product/9", FakeResponse.Status(404));
        using var r = await GetAsync("/Products/Details/9");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    // ---------- Create ----------

    [Fact]
    public async Task Create_get_populates_category_select()
    {
        await SignInAdminAsync();
        ScriptCategories();
        var html = await GetHtmlAsync("/Products/Create");
        Assert.Contains("<option value=\"1\">Books</option>", html);
        Assert.Contains("<option value=\"2\">Toys</option>", html);
    }

    [Fact]
    public async Task Create_post_ok_redirects_and_sends_body_with_bearer()
    {
        await SignInAdminAsync(token: "tok-a");
        ScriptCategories();
        Fake.On(HttpMethod.Post, "api/product", FakeResponse.Status(201));
        var resp = await PostFormAsync("/Products/Create", Form("Pen", "12", "7", "2"));
        AssertRedirect(resp, "/Products");
        var call = Fake.Single(HttpMethod.Post, "api/product");
        Assert.Equal("Bearer tok-a", call.Authorization);
        var json = call.Json();
        Assert.Equal("Pen", json.GetProperty("name").GetString());
        Assert.Equal(12m, json.GetProperty("price").GetDecimal());
        Assert.Equal(7, json.GetProperty("quantity").GetInt32());
        Assert.Equal(2, json.GetProperty("categoryId").GetInt32());
    }

    [Fact]
    public async Task Create_post_api_400_shows_field_errors_and_repopulates_select()
    {
        await SignInAdminAsync();
        ScriptCategories();
        Fake.On(HttpMethod.Post, "api/product", FakeResponse.Json(400, ApiJson.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["Name"] = new[] { "Name taken." },
                ["Price"] = new[] { "Price too high." }
            })));
        var resp = await PostFormAsync("/Products/Create", Form());
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Equal("Name taken.", HtmlHelpers.FieldError(html, "Name"));
        Assert.Equal("Price too high.", HtmlHelpers.FieldError(html, "Price"));
        Assert.Contains("<option value=\"1\">Books</option>", html);
    }

    [Fact]
    public async Task Create_post_invalid_category_id_shows_message_without_calling_api()
    {
        await SignInAdminAsync();
        ScriptCategories();
        var resp = await PostFormAsync("/Products/Create", Form(categoryId: "0"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("Please select a category.", HtmlHelpers.FieldError(await resp.Content.ReadAsStringAsync(), "CategoryId"));
        Assert.Empty(Fake.Calls(HttpMethod.Post, "api/product"));
    }

    [Fact]
    public async Task Create_post_api_409_shows_detail()
    {
        await SignInAdminAsync();
        ScriptCategories();
        Fake.On(HttpMethod.Post, "api/product", FakeResponse.Json(409, ApiJson.ConflictProblem("Product name already exists.")));
        var resp = await PostFormAsync("/Products/Create", Form());
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("Product name already exists.", HtmlHelpers.FieldError(await resp.Content.ReadAsStringAsync(), ""));
    }

    // ---------- Edit ----------

    [Fact]
    public async Task Edit_get_quantity_is_readonly_with_inventory_link()
    {
        await SignInAdminAsync();
        ScriptProduct(5);
        var html = await GetHtmlAsync("/Products/Edit/5");
        Assert.Matches(@"<input\b(?=[^>]*name=""Quantity"")[^>]*\breadonly\b", html);
        Assert.Contains("/Inventory/Product/5", html);
    }

    [Fact]
    public async Task Edit_post_ok_puts_posted_quantity_with_bearer()
    {
        await SignInAdminAsync(token: "tok-a");
        ScriptProduct(5);
        Fake.On(HttpMethod.Put, "api/product/5", FakeResponse.Status(204));
        var resp = await PostFormAsync("/Products/Edit/5", Form("Pen2", "15", "9", "1", id: 5));
        AssertRedirect(resp, "/Products");
        var call = Fake.Single(HttpMethod.Put, "api/product/5");
        Assert.Equal("Bearer tok-a", call.Authorization);
        Assert.Equal(9, call.Json().GetProperty("quantity").GetInt32());
        Assert.Equal("Pen2", call.Json().GetProperty("name").GetString());
        Assert.Equal(1, call.Json().GetProperty("categoryId").GetInt32());
    }

    [Fact]
    public async Task Edit_post_api_400_quantity_message_renders_in_quantity_span()
    {
        await SignInAdminAsync();
        ScriptProduct(5);
        Fake.On(HttpMethod.Put, "api/product/5", FakeResponse.Json(400,
            ApiJson.ValidationProblem("Quantity", "Use stock movements to change quantity.")));
        var resp = await PostFormAsync("/Products/Edit/5", Form(id: 5));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("Use stock movements to change quantity.", HtmlHelpers.FieldError(await resp.Content.ReadAsStringAsync(), "Quantity"));
    }

    [Fact]
    public async Task Edit_post_api_404_is_404()
    {
        await SignInAdminAsync();
        ScriptProduct(5);
        Fake.On(HttpMethod.Put, "api/product/5", FakeResponse.Status(404));
        var resp = await PostFormAsync("/Products/Edit/5", Form(id: 5));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Edit_post_api_409_shows_detail()
    {
        await SignInAdminAsync();
        ScriptProduct(5);
        Fake.On(HttpMethod.Put, "api/product/5", FakeResponse.Json(409, ApiJson.ConflictProblem("Conflict while updating.")));
        var resp = await PostFormAsync("/Products/Edit/5", Form(id: 5));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("Conflict while updating.", HtmlHelpers.FieldError(await resp.Content.ReadAsStringAsync(), ""));
    }

    // ---------- Delete ----------

    [Fact]
    public async Task Delete_get_shows_confirmation()
    {
        await SignInAdminAsync();
        ScriptProduct(5, "Pen");
        var html = await GetHtmlAsync("/Products/Delete/5");
        Assert.True(HtmlHelpers.HasText(html, "Pen"));
        Assert.True(HtmlHelpers.FormAction(html, "/Products/Delete/5"));
    }

    [Fact]
    public async Task Delete_post_ok_redirects_with_bearer()
    {
        await SignInAdminAsync(token: "tok-a");
        ScriptProduct(5);
        Fake.On(HttpMethod.Delete, "api/product/5", FakeResponse.Status(204));
        var resp = await PostFormAsync("/Products/Delete/5", new Dictionary<string, string>());
        AssertRedirect(resp, "/Products");
        Assert.Equal("Bearer tok-a", Fake.Single(HttpMethod.Delete, "api/product/5").Authorization);
    }

    [Fact]
    public async Task Delete_post_api_400_rerenders_delete_page()
    {
        await SignInAdminAsync();
        ScriptProduct(5);
        Fake.On(HttpMethod.Delete, "api/product/5", FakeResponse.Json(400, ApiJson.ValidationProblem("Id", "Cannot delete.")));
        var resp = await PostFormAsync("/Products/Delete/5", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(HtmlHelpers.HasText(await resp.Content.ReadAsStringAsync(), "Are you sure you want to delete this?"));
    }

    [Fact]
    public async Task Delete_post_api_404_is_404()
    {
        await SignInAdminAsync();
        ScriptProduct(5);
        Fake.On(HttpMethod.Delete, "api/product/5", FakeResponse.Status(404));
        var resp = await PostFormAsync("/Products/Delete/5", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_post_api_409_shows_detail()
    {
        await SignInAdminAsync();
        ScriptProduct(5);
        Fake.On(HttpMethod.Delete, "api/product/5", FakeResponse.Json(409, ApiJson.ConflictProblem("Product is referenced by orders.")));
        var resp = await PostFormAsync("/Products/Delete/5", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();
        Assert.True(HtmlHelpers.HasText(html, "Are you sure you want to delete this?"));
        Assert.Equal("Product is referenced by orders.", HtmlHelpers.FieldError(html, ""));
    }

    [Theory]
    [InlineData("not json", "text/plain")]
    [InlineData("", "application/json")]
    public async Task Create_post_api_409_without_detail_shows_generic_conflict(string body, string contentType)
    {
        await SignInAdminAsync();
        ScriptCategories();
        Fake.On(HttpMethod.Post, "api/product", FakeResponse.Raw(409, body, contentType));
        var resp = await PostFormAsync("/Products/Create", Form());
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("The request conflicts with existing data.", HtmlHelpers.FieldError(await resp.Content.ReadAsStringAsync(), ""));
    }
}
