# Contract: Logout sends the bearer without refreshing

## Request the MVC sends

```
POST {Api:BaseUrl}api/account/logout
Authorization: Bearer <access_token claim of the signed-in user>   (omitted if the claim is empty)
Content-Type: application/json
{"refreshToken":"<refresh_token claim>"}
```

No call is made when the refresh token claim is empty (unchanged). The request carries `HttpRequestMessage.Options[TokenRefreshHandler.SkipRefreshOption] = true`.

## `TokenRefreshHandler` rule (new first rule)

If `request.Options.TryGetValue(TokenRefreshHandler.SkipRefreshOption, out var skip)` and `skip` is true: return `await base.SendAsync(request, ct)` immediately. No buffering, no `ExpiresSoon` check, no `api/account/refresh` call, no 401 handling, no retry, no cookie re-issue. All other rules (no bearer or anonymous user passes through; pre-emptive refresh; single refresh and single retry on 401; per-context `refresh_tried` guard) are unchanged for requests without the option.

```csharp
public static readonly HttpRequestOptionsKey<bool> SkipRefreshOption = new("cp.skip-refresh");
```

## `LogoutAsync` shape (normative behavior)

```csharp
public async Task LogoutAsync(string? refreshToken)
{
    if (string.IsNullOrEmpty(refreshToken)) return;
    try
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/account/logout")
        { Content = JsonContent.Create(new { refreshToken }) };
        ApplyBearerToken(request);
        request.Options.Set(TokenRefreshHandler.SkipRefreshOption, true);
        using var _ = await _http.SendAsync(request);
    }
    catch (Exception) { }
}
```

`AccountController.Logout` is unchanged: it reads `User.FindFirst("refresh_token")`, calls `LogoutAsync`, signs out, redirects to Categories.

## Why not just add the bearer

See `research.md` R3. Without the option, an expiring token or a 401 makes the handler call `api/account/refresh` first; the API rotates the refresh token, the MVC then posts the old one to logout, the API finds it already revoked and does nothing, and the new refresh token stays valid after the user "logged out".

## Tests

| Where | Case | Expect |
|---|---|---|
| `AccountTests` (updated) `Logout_clears_cookie_and_revokes_refresh_token` | `SignInUserAsync("alice", token: "tok-1", refresh: "refresh-xyz")`, logout 204 | `call.Authorization == "Bearer tok-1"`; body `refreshToken == "refresh-xyz"`; redirect `/`; signed out. (Replaces `Assert.Null(call.Authorization)`.) |
| `AccountTests` (new) | token expiring in 30 s (`expiresInSeconds: 30`), `api/account/refresh` scripted 200 with new tokens | zero calls to `api/account/refresh`; one logout call with the OLD bearer and the OLD refresh token; signed out |
| `AccountTests` (new) | logout answers 401, refresh scripted 200 | exactly one logout call, zero refresh calls; signed out |
| `AccountTests` (new) | login result with an empty refresh token | no `api/account/logout` call (unchanged behavior); signed out |
| existing | logout API unreachable / 500 / `ApiUnavailableTests.Logout_with_api_down_still_signs_out` | unchanged and green |
| `TokenRefreshHandlerTests` (new) | bearer + option, token expiring in 30 s | inner called once with the original bearer; refresh handler zero calls; `Auth` records no sign-in |
| `TokenRefreshHandlerTests` (new) | bearer + option, inner answers 401 | the 401 is returned; inner called once; zero refresh calls |
| `TokenRefreshHandlerTests` (existing) | all cases without the option | unchanged and green |
