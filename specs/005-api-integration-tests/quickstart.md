# Quickstart: Running the API Integration Tests

## Prerequisites

- .NET 10 SDK.
- No SQL Server, no user-secrets and no environment variables are needed: the test factory supplies an InMemory database and its own test-only JWT settings.

## Run

From the repository root:

```
dotnet test CategoryProduct.API.Tests
```

or run every test project in the solution:

```
dotnet test CategoryProduct.slnx
```

Expected: all tests in `ApiTests` pass (5 at `b17b5ee`; 6 once `PostCategory_NonAdminToken_Returns403` from `f868aa7` is included).

## Verify the pieces

1. **Solution membership (FR-001)**: `CategoryProduct.slnx` lists `CategoryProduct.API.Tests/CategoryProduct.API.Tests.csproj`.
2. **Login (US1.1, US1.2)**: `Login_CorrectCredentials_ReturnsToken` and `Login_WrongPassword_Returns401` pass.
3. **Write protection (US1.3, US1.4)**: `PostCategory_WithoutToken_Returns401` and `PostCategory_NonAdminToken_Returns403` pass.
4. **Paging (US1.5)**: `GetCategories_PageSizeAbove50_IsClamped` passes.
5. **Validation (US1.6)**: `PostProduct_NonExistentCategory_Returns400` passes.
6. **No secrets (SC-001)**: search the test project for committed credentials; only test-only constants in `ApiFactory.cs` exist.

Run a single test with `dotnet test CategoryProduct.API.Tests --filter "FullyQualifiedName~Login_WrongPassword_Returns401"`.

## Database migration

This feature adds no migration. The tests do not use SQL Server; to update a real database for manual testing of the API run:

```
dotnet ef database update --project CategoryProduct.API
```

## Success criteria mapping

- SC-001 -> the run above with no database or secrets configured.
