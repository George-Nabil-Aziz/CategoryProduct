# Implementation Plan: MVC Login

**Branch**: `007-mvc-login` (retroactive) | **Date**: 2026-11-03 | **Spec**: [spec.md](./spec.md)

## Summary

Add cookie sign-in to `CategoryProduct.MVC` that delegates credential checks to the API and keeps the returned JWT inside the auth cookie, then forwards it to the API on writes.

## Technical Context

**Language/Version**: C# / .NET 10, ASP.NET Core MVC cookie authentication, `IHttpContextAccessor`.

**Dependencies**: Feature 003 (API login) and 006 (typed client). No new packages.

## Files Touched

```text
CategoryProduct.MVC/
├── Program.cs                            # AddHttpContextAccessor, AddCookie (HttpOnly, Secure, Lax), UseAuthentication
├── Controllers/AccountController.cs      # new: Login GET/POST, Logout
├── Controllers/CategoriesController.cs   # [Authorize] on writes, 401 -> sign out + redirect
├── Controllers/ProductsController.cs     # same
├── Services/CategoryProductApiClient.cs  # LoginAsync, per-request bearer from `access_token` claim
├── Models/LoginViewModel.cs              # new
├── Views/Account/Login.cshtml            # new
├── Views/Shared/_Layout.cshtml           # sign in / sign out UI
└── Views/Categories|Products/Index.cshtml# minor
```

## Key Decisions

- JWT stored as a claim in the cookie (no server session store): simplest approach for a single MVC host.
- Bearer set per request (`HttpRequestMessage`) rather than on `DefaultRequestHeaders`, to avoid leaking one user's token to another through the shared typed client.
- The API stays the only authority on credentials; MVC never sees or stores passwords.
- Handling 401 by signing out prevents users from being stuck with an expired token.
