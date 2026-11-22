# Research: API Hardening

## Decision 1: Where the CategoryId existence check lives

**Decision**: Check `_context.Categories.AnyAsync(c => c.Id == request.CategoryId)` at the top of `ProductController.Post` and `Put`; on failure add a model error for `CategoryId` ("Category does not exist.") and return `ValidationProblem(ModelState)`.

**Rationale**:
- Reuses the 400 `ValidationProblemDetails` format already produced by `[ApiController]` for the 002 DTO rules, so clients handle one error shape.
- Matches Principle III: the controller already talks to `ApplicationDbContext` directly; no validator or service layer is added.
- Without it, an unknown `CategoryId` only fails at `SaveChanges` with a foreign-key exception.

**Alternatives considered**:
- **Catch `DbUpdateException` and translate it**: couples the response to provider errors and is only discovered after the write attempt. Rejected.
- **Custom `ValidationAttribute` on `ProductRequestDto`**: needs `DbContext` access through the validation context; heavier than one controller check. Rejected.

## Decision 2: Fail-fast JWT key check at startup

**Decision**: In `Program.cs`, before registering services, read `Jwt:Key` and throw `InvalidOperationException` when it is null/whitespace or shorter than 32 characters. The message tells the developer to use `dotnet user-secrets set "Jwt:Key" "<key>"` (development) or the `Jwt__Key` environment variable. The `Key` entry is removed from `appsettings.json`; `Issuer`, `Audience` and `ExpiryMinutes` stay.

**Rationale**:
- Constitution Principle IV forbids committed secrets; 003 had left an empty `Key` placeholder, which let the API start with an empty signing key.
- 32 characters is the minimum chosen for HMAC-SHA256 signing keys.
- The API project already has a `UserSecretsId`, so user-secrets works without csproj changes.

**Alternatives considered**:
- **Keep an empty placeholder and fail at first login**: fails late and only for one endpoint. Rejected.
- **Stronger entropy checks (base64 length, character classes)**: not required by the spec; YAGNI. Rejected.

## Decision 3: Paging envelope and limits

**Decision**: Add `public record PagedResponseDto<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages)`. Controllers clamp with `Math.Max(page, 1)` and `Math.Clamp(pageSize, 1, 50)`, order by `Id`, and compute `TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)`.

**Rationale**:
- Principle I requires `page`/`pageSize` pagination; a pager needs total counts.
- Without `OrderBy`, `Skip`/`Take` over SQL Server has no guaranteed order, so pages could overlap.
- A hard upper bound of 50 prevents unbounded result sets.
- One generic record serves both Category and Product lists.

**Alternatives considered**:
- **Total count in a response header**: leaves the body shape unchanged but is harder for typed clients. Rejected.
- **Return 400 for out-of-range values**: clamping is more forgiving and keeps clients simple. Rejected.

## Decision 4: Async conversion without a service layer

**Decision**: Convert every action in `CategoryController`, `ProductController` and `AccountController.Login` to `async Task<IActionResult>` using `FindAsync`, `FirstOrDefaultAsync`, `AnyAsync`, `CountAsync`, `ToListAsync` and `SaveChangesAsync`, in a separate commit (`3149197`) from the behavior changes (`b17b5ee`).

**Rationale**: Avoids blocking request threads on database I/O; a separate commit keeps the behavior diff reviewable. Principle III: no service layer introduced.

**Alternatives considered**: Leave the actions synchronous until load requires otherwise. Rejected: the cost is low and every later feature builds on these actions.
