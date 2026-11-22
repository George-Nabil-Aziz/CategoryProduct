# Contract: MVC pages (CategoryProduct.MVC)

All calls to the API go through `CategoryProductApiClient` over HTTP (Principle V).

| Route | Verb | Auth | Behavior | API call |
|-------|------|------|----------|----------|
| `/Account/Register` | GET | anonymous | Shows form: User name, Password, Confirm password | none |
| `/Account/Register` | POST (anti-forgery) | anonymous | Validates `RegisterViewModel`; on success logs in and signs cookie, redirects to `Categories/Index`; if the follow-up login fails or is rate limited, redirects to `Account/Login` | `POST api/account/register`, then `POST api/account/login` |
| `/Users` (`Users/Index`) | GET `?page&search` | role `Admin` | Table of users with a role dropdown (`User`/`Admin`) and Save per row, search box, Previous/Next paging | `GET api/users?page=&pageSize=10&search=` |
| `/Users/ChangeRole` | POST (anti-forgery) `id, role, page, search` | role `Admin` | Redirects back to Index keeping page/search; sets `TempData["Success"]` = `Role updated.` or `TempData["Error"]` | `PUT api/users/{id}/role` |

## Error mapping (Register POST)

| API outcome | UI |
|-------------|----|
| `429` | Model-level error: `Too many attempts. Try again in a minute.` |
| `409` | Field error on `UserName`: `Username already taken.` |
| `400` with errors | Errors for `UserName`/`Password` shown on that field, others model-level |
| other failure | `Registration failed. Please try again.` |

## Error mapping (ChangeRole POST)

Conflict message from the API, or the joined validation messages, or `User not found.`, or
`Could not change role.`

## Navigation

`_Layout.cshtml`: admins see a `Users` link; anonymous visitors see `Login` and `Register`.
`Login.cshtml` also links to Register.

Note: `GetUsersAsync` calls `EnsureSuccessStatusCode()`, so an API failure on the list page
surfaces as an exception handled by the app's general error handling.
