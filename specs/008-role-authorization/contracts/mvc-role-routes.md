# Contract: MVC role behavior (changed)

Builds on `specs/007-mvc-login/contracts/mvc-routes.md`.

## Sign-in (`POST /Account/Login`)

On success the cookie principal now has three claims: `ClaimTypes.Name`, `ClaimTypes.Role` (from the API login result's `Role`) and `access_token`. Other behavior is unchanged.

## GET /Account/AccessDenied (new)

Renders `Views/Account/AccessDenied.cshtml`: heading "Access denied", text "You don't have permission to do this." and a link back to Categories Index. This is the cookie scheme's `AccessDeniedPath`.

## Protected actions (changed from `[Authorize]` to `[Authorize(Roles = "Admin")]`)

`Create` (GET, POST), `Edit` (GET, POST), `Delete` (GET), `Delete` POST in both `/Categories` and `/Products`.

| Caller | Result |
|--------|--------|
| Anonymous | Redirect to `/Account/Login` (as in 007) |
| Signed in, role not `Admin` (including cookies issued before this change) | Redirect to `/Account/AccessDenied?ReturnUrl=...` |
| Admin | Action runs |

## API replies to an Admin's write

| API status | MVC result |
|------------|------------|
| 2xx | Redirect to Index (unchanged) |
| 401 | Sign out, redirect to `Account/Login` with `returnUrl` (unchanged from 007) |
| 403 | Form re-rendered with model error "You don't have permission to make changes."; user stays signed in |
| 400, 404 | Unchanged from 006 |

## Link visibility (views)

| View | Visible to Admin only |
|------|-----------------------|
| `Categories/Index`, `Products/Index` | "Create New", per-row "Edit" and "Delete" ("Details" is always visible) |
| `Categories/Details`, `Products/Details` | "Edit" ("Back to List" is always visible) |

Reads (`Index`, `Details`) remain accessible to anonymous users.
