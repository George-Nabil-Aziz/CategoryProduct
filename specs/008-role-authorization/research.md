# Research: Role-Based Authorization

## Decision 1: A single `Role` string on `User`

**Decision**: Add `public string Role { get; set; } = "User";` to `User`, configured in `ApplicationDbContext` as required, `HasMaxLength(20)` and `HasDefaultValue("User")`. Migration `20261103173834_AddUserRole` adds an `nvarchar(20)` column `Role` (not null, default `User`) and runs `UPDATE Users SET Role = 'Admin' WHERE UserName = 'testuser'`.

**Rationale**:
- Principle III: two levels (Admin vs everyone else) need one column, not Identity's roles/user-roles tables.
- Default `User` makes every existing and future account non-privileged unless explicitly promoted (least privilege).
- Promoting `testuser` in the migration keeps the existing demo/test login working after deployment.

**Alternatives considered**:
- **ASP.NET Core Identity roles tables**: far more schema and services than two roles need. Rejected.
- **Boolean `IsAdmin`**: simpler but blocks adding a third role without another migration. Rejected: a string costs the same.

## Decision 2: Carry the role in the JWT and the MVC cookie

**Decision**: `AccountController.Login` adds `new Claim(ClaimTypes.Role, user.Role)` and returns `LoginResponseDto(token, expiresAtUtc, role)`. MVC reads `Role` from the login result and stores it as `ClaimTypes.Role` in the cookie alongside `access_token`.

**Rationale**: `[Authorize(Roles = "Admin")]` works from the claim with no per-request database lookup. The trade-off is explicit: role changes apply at next login (see spec edge cases).

**Alternatives considered**: Look up the role from the database on every request (extra query per request and a custom authorization handler). Rejected for now.

## Decision 3: `[Authorize(Roles = "Admin")]` on write actions only

**Decision**: Replace `[Authorize]` with `[Authorize(Roles = "Admin")]` on `Post`, `Put`, `Delete` of `CategoryController` and `ProductController`, and on all create/edit/delete actions of the MVC controllers. `GET` actions stay anonymous.

**Rationale**: Uses the framework's built-in role check; no custom policy or middleware (Principle III). Reads stay public as required by 003.

**Alternatives considered**: A named policy (`AddPolicy("AdminOnly")`). Rejected: one role string used in a single attribute form is enough.

## Decision 4: Different MVC handling for 401 and 403

**Decision**: `ApiClient.SendAsync` returns `Forbidden` for 403 and `Unauthorized` for 401 (previously both were `Unauthorized`). Controllers keep signing the user out on `Unauthorized`; `Forbidden` becomes the form error "You don't have permission to make changes." `AccessDeniedPath` becomes `/Account/AccessDenied` (previously `/Account/Login`), which renders a short "Access denied" page with a link back to Categories.

**Rationale**: Signing out on 403 would be wrong (the token is valid, just not privileged) and would loop users back to login; a stale or missing role claim needs a clear explanation instead. Direct URL access by a signed-in non-Admin is stopped earlier by the cookie middleware, which uses `AccessDeniedPath`.

**Alternatives considered**: Redirect to the Access Denied page on API 403 as well. Rejected: the delivered code shows the form error instead (an earlier draft of this spec said otherwise, which was incorrect).

## Decision 5: Hide write links in views

**Decision**: Index and Details views wrap Create/Edit/Delete links in `User.IsInRole("Admin")`.

**Rationale**: Avoids offering actions that can only fail; the server-side `[Authorize]` remains the real enforcement.

**Alternatives considered**: Leave links visible and rely on Access Denied. Rejected: confusing for non-Admins (this was the 007 behavior for Edit/Delete).
