# Contract: API behaviors asserted by the integration tests

This feature adds no endpoint. It pins down the HTTP behavior below; each row is one `[Fact]` in `CategoryProduct.API.Tests/ApiTests.cs`. Routes are lowercase in the tests (`api/account/login`, `api/category`, `api/product`); ASP.NET Core routing is case-insensitive.

| Test | Request | Expected |
|------|---------|----------|
| `Login_CorrectCredentials_ReturnsToken` | `POST api/account/login` `{ userName, password }` of the seeded `tester` | `200 OK`; JSON `token` is a non-blank string |
| `Login_WrongPassword_Returns401` | `POST api/account/login` with `tester` and password `wrong` | `401 Unauthorized` |
| `PostCategory_WithoutToken_Returns401` | `POST api/category` `{ name: "New" }`, no `Authorization` header | `401 Unauthorized` |
| `PostCategory_NonAdminToken_Returns403` (from `f868aa7`) | Login as `plainuser`, then `POST api/category` `{ name: "New" }` with `Authorization: Bearer <token>` | `403 Forbidden` |
| `GetCategories_PageSizeAbove50_IsClamped` | `GET api/category?page=1&pageSize=100` | `200 OK`; JSON `pageSize == 50` and `totalCount == 3` (`SeedCategoryCount`) |
| `PostProduct_NonExistentCategory_Returns400` | Login as `tester`, then `POST api/product` `{ name: "Widget", price: 10, quantity: 1, categoryId: 9999 }` with bearer token | `400 Bad Request` |

## Notes

- The login helper reads `token` from the JSON response; `LoginAsync(userName)` takes the user name since `f868aa7`.
- In `b17b5ee` the `tester` user could write because any valid token was enough (003); from `f868aa7` `tester` is an Admin so the same tests keep passing.
- The underlying endpoint contracts are documented in `specs/003-jwt-authentication/contracts/`, `specs/004-api-hardening/contracts/` and `specs/008-role-authorization/contracts/`.
