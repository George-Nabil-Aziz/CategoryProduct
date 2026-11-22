using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace CategoryProduct.MVC.Tests;

/// <summary>IAuthenticationService that records sign-in/sign-out calls instead of touching cookies.</summary>
public sealed class RecordingAuthenticationService : IAuthenticationService
{
    public sealed record SignInCall(string? Scheme, ClaimsPrincipal Principal, AuthenticationProperties? Properties);

    public List<SignInCall> SignIns { get; } = new();
    public List<string?> SignOuts { get; } = new();

    public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
        Task.FromResult(AuthenticateResult.NoResult());

    public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;

    public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;

    public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
    {
        SignIns.Add(new SignInCall(scheme, principal, properties));
        return Task.CompletedTask;
    }

    public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
    {
        SignOuts.Add(scheme);
        return Task.CompletedTask;
    }
}
