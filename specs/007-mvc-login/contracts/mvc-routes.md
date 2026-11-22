# Contract: MVC routes (sign-in and protected writes)

## Account (`AccountController`, new)

### GET /Account/Login?returnUrl={url}

Shows `Views/Account/Login.cshtml` with an empty `LoginViewModel`; `returnUrl` is kept in `ViewData` and posted back through the form's route.

### POST /Account/Login?returnUrl={url}

Form fields: `UserName`, `Password`, anti-forgery token (`[ValidateAntiForgeryToken]`).

| Condition | Result |
|-----------|--------|
| Model invalid (blank field) | Re-render the form with field errors; no API call |
| API login returns 401 | Re-render the form with model error "Invalid username or password." |
| API login succeeds | Cookie sign-in (see `data-model.md`), then `LocalRedirect(returnUrl)` if `returnUrl` is a local URL, otherwise redirect to `Categories/Index` |
| API login fails with another status or is unreachable | Exception (no handling at this commit) |

### POST /Account/Logout

`[ValidateAntiForgeryToken]`. Clears the auth cookie and redirects to `Categories/Index`. There is no GET logout.

## Protected write routes

`[Authorize]` (any signed-in user at this commit) is applied to these actions in `CategoriesController` and `ProductsController`:

| Controller | Actions |
|------------|---------|
| `/Categories` | `Create` (GET, POST), `Edit` (GET, POST), `Delete` (GET), `Delete` POST (`DeleteConfirmed`) |
| `/Products` | `Create` (GET, POST), `Edit` (GET, POST), `Delete` (GET), `Delete` POST (`DeleteConfirmed`) |

Behavior:

- Anonymous request: cookie challenge, redirect to `/Account/Login?ReturnUrl=<original path>`.
- On POST, an API 401 result: sign out the cookie scheme, redirect to `Account/Login` with `returnUrl = Request.Path + Request.QueryString`.
- All other write outcomes (success redirect to Index, 404, 400 model errors) are as in `specs/006-mvc-api-client/contracts/mvc-routes.md`.

## Public routes (unchanged)

`Index` and `Details` of both controllers stay anonymous. The layout (`Views/Shared/_Layout.cshtml`) shows a Login link to anonymous users and "Hello, {name}" plus a Logout button to signed-in users; the Index pages show "Create New" only when authenticated.

## API calls

See `api-calls.md`.
