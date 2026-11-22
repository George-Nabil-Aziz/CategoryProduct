# Contract: Error page and HomeController

## Routes (MVC, default route `{controller=Categories}/{action=Index}/{id?}`)

| Request | Response |
|---|---|
| `GET /Home`, `GET /Home/Index` | 302 redirect to `/Categories` (`RedirectToAction("Index", "Categories")`) |
| any verb `/Home/Error` | status 500, HTML: layout + error view, anonymous allowed |
| any unhandled exception, environment not Development | middleware re-executes `/Home/Error` with the original method; client sees status 500 and the error view |

## `HomeController`

```csharp
public class HomeController : Controller
{
    public IActionResult Index() => RedirectToAction("Index", "Categories");

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        return View("Error", new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });
    }
}
```

(Shape is normative; usings and namespace `CategoryProduct.MVC.Controllers` follow the other controllers. No `[HttpGet]`/`[HttpPost]` on `Error`.)

## `Views/Shared/Error.cshtml`

Must contain: heading text "Something went wrong", the sentence "An unexpected error occurred while processing your request. Please try again.", a Request ID line (`<strong>Request ID:</strong> <code>...</code>`) only when `Model.ShowRequestId`, and a link to `/Categories` with text "Back to Categories".

Must NOT contain: "Development Mode", "ASPNETCORE_ENVIRONMENT", an exception message, an exception type name, a stack trace, an inner-exception text. (The view model carries no exception data; keep it that way.)

## Response headers on `/Home/Error`

`Cache-Control` contains `no-store` (from `[ResponseCache(NoStore = true)]`), `Pragma: no-cache`.

## Tests (`ErrorPageTests`, environment Production through `ProductionMvcFactory`)

| Case | Setup | Expect |
|---|---|---|
| Unhandled exception on GET | `Fake.Throw(Get, "api/category", new InvalidOperationException("secret-marker-9f3a"))`; `GET /Categories` | 500; HTML has "Something went wrong" and a link to `/Categories`; does not contain `secret-marker-9f3a`, `InvalidOperationException`, `System.` frames or `   at ` |
| Request ID shown | same | HTML contains `Request ID:` followed by non-empty code text |
| No dev text | same | HTML does not contain "Development Mode" |
| Cache headers | same | `Cache-Control` contains `no-store` |
| Unhandled exception on POST | sign in Admin; `GET api/category` scripted ok for the token page; `Fake.Throw(Post, "api/category", new InvalidOperationException("secret-marker-9f3a"))`; post Create form | 500 and the same page |
| Anonymous direct | `GET /Home/Error` without a cookie | 500 (not a login redirect), friendly page |
| Signed-in layout | sign in as User, then trigger the exception | page shows `Hello, <name>` (layout intact) |
| Home redirect | `GET /Home` and `GET /Home/Index` | 302 with Location path `/Categories` |
| Development unchanged | covered implicitly: all other test classes run as Development | no new test |
