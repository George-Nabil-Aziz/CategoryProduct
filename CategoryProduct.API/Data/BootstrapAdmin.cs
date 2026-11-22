using System.Text.RegularExpressions;
using CategoryProduct.API.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CategoryProduct.API.Data
{
    public enum BootstrapAdminOutcome
    {
        NotConfigured,
        Created,
        SkippedAdminExists,
        SkippedUserNameTaken,
        Rejected,
        Failed
    }

    /// <summary>
    /// Creates the first Admin from <c>Bootstrap:AdminUserName</c> / <c>Bootstrap:AdminPassword</c> when no Admin exists.
    /// Never throws and never logs the password.
    /// </summary>
    public static class BootstrapAdmin
    {
        private static readonly Regex UserNamePattern = new(@"^[A-Za-z0-9._-]+$", RegexOptions.Compiled);

        public static async Task<BootstrapAdminOutcome> EnsureIfConfiguredAsync(
            IServiceProvider services, IConfiguration configuration, ILogger logger)
        {
            try
            {
                var rawName = configuration["Bootstrap:AdminUserName"];
                var password = configuration["Bootstrap:AdminPassword"];
                var hasName = !string.IsNullOrWhiteSpace(rawName);
                var hasPassword = !string.IsNullOrWhiteSpace(password);

                if (!hasName && !hasPassword)
                {
                    await WarnIfNoAdminAsync(services, logger);
                    return BootstrapAdminOutcome.NotConfigured;
                }

                if (hasName != hasPassword)
                {
                    logger.LogWarning(
                        "Both Bootstrap:AdminUserName and Bootstrap:AdminPassword are required; no bootstrap Admin created.");
                    return BootstrapAdminOutcome.NotConfigured;
                }

                var name = rawName!.Trim();
                var rule = FirstBrokenRule(name, password!);
                if (rule != null)
                {
                    logger.LogError("Bootstrap admin rejected: {Rule}", rule);
                    return BootstrapAdminOutcome.Rejected;
                }

                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                if (await db.Users.AnyAsync(u => u.Role == "Admin"))
                {
                    logger.LogInformation("Bootstrap admin skipped: an Admin user already exists.");
                    return BootstrapAdminOutcome.SkippedAdminExists;
                }

                var lower = name.ToLower();
                if (await db.Users.AnyAsync(u => u.UserName.ToLower() == lower))
                {
                    logger.LogWarning(
                        "Bootstrap admin skipped: user name '{UserName}' is already taken; the existing user was not promoted.",
                        name);
                    return BootstrapAdminOutcome.SkippedUserNameTaken;
                }

                var user = new User { UserName = name, Role = "Admin", PasswordHash = "" };
                user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password!);
                db.Users.Add(user);

                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    logger.LogInformation(
                        "Bootstrap admin '{UserName}' was created by another instance or the name is taken; continuing.",
                        name);
                    return BootstrapAdminOutcome.SkippedUserNameTaken;
                }

                logger.LogInformation("Bootstrap admin '{UserName}' created.", name);
                return BootstrapAdminOutcome.Created;
            }
            catch (Exception ex)
            {
                // The password is never part of an exception message raised here.
                logger.LogError(ex, "Bootstrap admin failed; the API continues without it.");
                return BootstrapAdminOutcome.Failed;
            }
        }

        private static string? FirstBrokenRule(string name, string password)
        {
            if (name.Length < 3 || name.Length > 50)
                return "UserName must be 3-50 characters.";
            if (!UserNamePattern.IsMatch(name))
                return "UserName may contain only letters, digits, '.', '_' and '-'.";
            if (password.Length < 8)
                return "Password must be at least 8 characters.";
            return null;
        }

        private static async Task WarnIfNoAdminAsync(IServiceProvider services, ILogger logger)
        {
            try
            {
                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                if (!await db.Users.AnyAsync(u => u.Role == "Admin"))
                    logger.LogWarning(
                        "No Admin user exists. Set Bootstrap:AdminUserName and Bootstrap:AdminPassword to create one.");
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not check for an Admin user (migrations may not be applied).");
            }
        }
    }
}
