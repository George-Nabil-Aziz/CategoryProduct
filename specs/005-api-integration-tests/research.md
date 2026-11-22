# Research: API Integration Tests

## Decision 1: WebApplicationFactory integration tests instead of controller unit tests

**Decision**: Test through HTTP using `WebApplicationFactory<Program>` (`Microsoft.AspNetCore.Mvc.Testing`), exposing `Program` with `public partial class Program { }` (feature 004).

**Rationale**:
- The behaviors worth protecting (401 vs 403, 400 validation shape, paging clamp) depend on the real middleware pipeline: JWT bearer validation, `[Authorize]`, model validation and routing. Calling controller methods directly would bypass all of them.
- No mocks are required, which keeps tests small (6 tests in one 70-line class).

**Alternatives considered**:
- **Controller unit tests with a mocked `DbContext`**: would not exercise authentication or model binding. Rejected.
- **Tests against a real SQL Server**: slower and needs infrastructure on every machine and CI run. Rejected.

## Decision 2: EF Core InMemory provider replaces SQL Server

**Decision**: `ApiFactory.ConfigureWebHost` removes the `DbContextOptions<ApplicationDbContext>`, `DbContextOptions` and `IDbContextOptionsConfiguration<ApplicationDbContext>` registrations and re-adds `ApplicationDbContext` with `UseInMemoryDatabase(<new GUID name>)`. `ConnectionStrings:DefaultConnection` is set to a placeholder so configuration lookups succeed.

**Rationale**: Fast, hermetic, parallel-safe tests (each factory instance gets its own database name). Constitution Principle II is not affected because no schema is touched.

**Alternatives considered**:
- **SQLite in-memory**: enforces more relational behavior but adds a package and still differs from SQL Server. Rejected for now (YAGNI).
- **Testcontainers/SQL Server**: closest to production but requires Docker. Rejected.

**Known limit**: InMemory does not enforce the unique index on `UserName` or foreign keys, so those are not covered.

## Decision 3: Test-only secrets supplied through host settings

**Decision**: `Jwt:Key`, `Jwt:Issuer`, `Jwt:Audience` are provided with `builder.UseSetting(...)` inside the factory; the key is a test-only constant that satisfies the 32-character rule from 004. The seeded user's password is hashed with `PasswordHasher<User>` at seed time, so no plaintext is stored in the in-memory database.

**Rationale**: Constitution Principle IV: no real secret is committed, and production configuration is never read by tests. The key check from 004 is exercised rather than bypassed.

**Alternatives considered**: User-secrets in the test project (requires per-developer setup, breaks `dotnet test` on a clean clone). Rejected.

## Decision 4: One shared fixture per test class

**Decision**: `ApiTests : IClassFixture<ApiFactory>` so the host and seed run once per class; tests only read seeded data or write data that other tests do not depend on.

**Rationale**: Avoids rebuilding the host for every test. `GetCategories_PageSizeAbove50_IsClamped` asserts `totalCount == SeedCategoryCount`, which is safe because no other test in the class creates categories successfully (the write tests expect 401/403).

**Alternatives considered**: A fresh factory per test (slower, no extra isolation needed today). Rejected.
