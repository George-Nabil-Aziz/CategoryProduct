using CategoryProduct.API.Data;
using CategoryProduct.API.Services;
using CategoryProduct.API.Dtos;
using System.Security.Cryptography;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.WebUtilities;

namespace CategoryProduct.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AccountController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly TokenService _tokens;
        private readonly IConfiguration _configuration;
        private readonly NotificationService _notifications;
        private readonly TimeProvider _time;
        private readonly ILogger<AccountController> _logger;

        public AccountController(ApplicationDbContext context, TokenService tokens, IConfiguration configuration,
            NotificationService notifications, TimeProvider time, ILogger<AccountController> logger)
        {
            _context = context;
            _tokens = tokens;
            _configuration = configuration;
            _notifications = notifications;
            _time = time;
            _logger = logger;
        }

        [HttpPost("register")]
        [EnableRateLimiting("register")]
        public async Task<IActionResult> Register(RegisterRequestDto request)
        {
            if (!EmailAddressRules.TryNormalize(request.Email, out var email, out var emailError))
                return EmailInvalid(emailError!);

            var userName = request.UserName.Trim();
            var lower = userName.ToLower();
            if (await _context.Users.AnyAsync(u => u.UserName.ToLower() == lower))
                return Conflict(new ProblemDetails { Status = 409, Title = "Conflict", Detail = "Username already taken." });
            if (email != null && await _context.Users.AnyAsync(u => u.Email == email))
                return EmailInUse();

            var user = new Models.User { UserName = userName, PasswordHash = "", Role = "User", Email = email };
            user.PasswordHash = new PasswordHasher<Models.User>().HashPassword(user, request.Password);
            _context.Users.Add(user);
            try { await _context.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                _context.ChangeTracker.Clear();
                if (email != null && await _context.Users.AnyAsync(u => u.Email == email))
                    return EmailInUse();
                return Conflict(new ProblemDetails { Status = 409, Title = "Conflict", Detail = "Username already taken." });
            }

            return StatusCode(StatusCodes.Status201Created, new UserResponseDto(user.Id, user.UserName, user.Role, user.Email));
        }

        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> Me()
        {
            if (CurrentUserId() is not int id) return Unauthorized();
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return Unauthorized();
            return Ok(new ProfileResponseDto(user.Id, user.UserName, user.Role, user.Email));
        }

        [HttpPut("email")]
        [Authorize]
        [EnableRateLimiting("profile")]
        public async Task<IActionResult> UpdateEmail(UpdateEmailRequestDto request)
        {
            if (CurrentUserId() is not int id) return Unauthorized();
            if (!EmailAddressRules.TryNormalize(request.Email, out var email, out var emailError))
                return EmailInvalid(emailError!);

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return Unauthorized();

            if (email != null && await _context.Users.AnyAsync(u => u.Email == email && u.Id != id))
                return EmailInUse();

            user.Email = email;
            try { await _context.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                _context.ChangeTracker.Clear();
                if (email != null && await _context.Users.AnyAsync(u => u.Email == email && u.Id != id))
                    return EmailInUse();
                throw;
            }
            return Ok(new ProfileResponseDto(user.Id, user.UserName, user.Role, user.Email));
        }

        private int? CurrentUserId() =>
            int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

        private ActionResult EmailInvalid(string message)
        {
            ModelState.AddModelError("Email", message);
            return ValidationProblem(ModelState);
        }

        private ActionResult EmailInUse() =>
            Conflict(new ProblemDetails { Status = 409, Title = "Conflict", Detail = "Email already in use." });

        [HttpPost("login")]
        [EnableRateLimiting("login")]
        public async Task<IActionResult> Login(LoginRequestDto request)
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.UserName.ToLower() == request.UserName.ToLower());

            if (user == null)
                return Unauthorized();

            var hasher = new PasswordHasher<Models.User>();
            var result = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);

            if (result == PasswordVerificationResult.Failed)
                return Unauthorized();

            return Ok(await IssueAsync(user, Guid.NewGuid()));
        }

        [HttpPost("refresh")]
        [EnableRateLimiting("refresh")]
        public async Task<IActionResult> Refresh(RefreshRequestDto request)
        {
            var hash = TokenService.Hash(request.RefreshToken ?? string.Empty);
            var stored = await _context.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == hash);
            if (stored == null)
                return Unauthorized();

            var now = DateTime.UtcNow;
            if (stored.RevokedAtUtc != null)
            {
                var grace = _configuration.GetValue<int?>("Jwt:RefreshReuseGraceSeconds") ?? 10;
                if (grace > 0 && stored.ReplacedByTokenHash != null
                    && now - stored.RevokedAtUtc.Value < TimeSpan.FromSeconds(grace))
                    return Unauthorized();

                var active = await _context.RefreshTokens
                    .Where(r => r.UserId == stored.UserId && r.FamilyId == stored.FamilyId && r.RevokedAtUtc == null)
                    .ToListAsync();
                foreach (var t in active) t.Revoke(now);
                try { await _context.SaveChangesAsync(); }
                catch (DbUpdateConcurrencyException) { }
                return Unauthorized();
            }
            if (stored.ExpiresAtUtc <= now)
                return Unauthorized();

            var user = await _context.Users.FindAsync(stored.UserId);
            if (user == null)
                return Unauthorized();

            try
            {
                var response = await IssueAsync(user, stored.FamilyId, stored);
                return Ok(response);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Unauthorized();
            }
        }

        [HttpPost("logout")]
        [EnableRateLimiting("logout")]
        public async Task<IActionResult> Logout(RefreshRequestDto request)
        {
            var hash = TokenService.Hash(request.RefreshToken ?? string.Empty);
            var stored = await _context.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == hash);
            if (stored != null && stored.RevokedAtUtc == null)
            {
                stored.Revoke(DateTime.UtcNow);
                try { await _context.SaveChangesAsync(); }
                catch (DbUpdateConcurrencyException) { }
            }
            return NoContent();
        }

        private const string InvalidResetTokenMessage = "The reset link is invalid or has expired.";
        private const string DefaultMvcBaseUrl = "http://localhost:5256";

        [HttpPost("change-password")]
        [Authorize]
        [EnableRateLimiting("password")]
        public async Task<IActionResult> ChangePassword(ChangePasswordRequestDto request)
        {
            if (CurrentUserId() is not int id) return Unauthorized();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return Unauthorized();

            var hasher = new PasswordHasher<Models.User>();
            if (hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
                return FieldError("CurrentPassword", "Current password is incorrect.");
            if (request.NewPassword == request.CurrentPassword)
                return FieldError("NewPassword", "New password must differ from the current password.");

            var now = _time.GetUtcNow().UtcDateTime;
            user.PasswordHash = hasher.HashPassword(user, request.NewPassword);
            LoginResponseDto response;
            try
            {
                await RevokeAllRefreshTokensAsync(user.Id, now);
                response = await IssueAsync(user, Guid.NewGuid());
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict(new ProblemDetails { Status = 409, Title = "Conflict", Detail = "Please try again." });
            }

            await _notifications.PasswordChangedAsync(user);
            return Ok(response);
        }

        [HttpPost("forgot-password")]
        [EnableRateLimiting("forgot-password")]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordRequestDto request)
        {
            var input = (request.UserNameOrEmail ?? string.Empty).Trim();
            if (input.Length == 0)
                return FieldError("UserNameOrEmail", "The UserNameOrEmail field is required.");

            try
            {
                var lower = input.ToLower();
                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName.ToLower() == lower);
                if (user == null && EmailAddressRules.TryNormalize(input, out var email, out _) && email != null)
                    user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

                if (user == null || string.IsNullOrEmpty(user.Email)
                    || !_configuration.GetValue("Email:Enabled", false))
                    return Accepted();

                var now = _time.GetUtcNow().UtcDateTime;
                var interval = ConfigInt("PasswordReset:MinIntervalSeconds", 60, allowZero: true);
                if (interval > 0)
                {
                    var since = now.AddSeconds(-interval);
                    if (await _context.PasswordResetTokens.AnyAsync(t =>
                            t.UserId == user.Id && t.UsedAtUtc == null && t.CreatedAtUtc > since))
                        return Accepted();
                }

                var minutes = ConfigInt("PasswordReset:TokenMinutes", 30, allowZero: false);
                var older = await _context.PasswordResetTokens
                    .Where(t => t.UserId == user.Id && t.UsedAtUtc == null && t.ExpiresAtUtc > now)
                    .ToListAsync();
                foreach (var t in older) t.MarkUsed(now);

                var raw = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
                _context.PasswordResetTokens.Add(new Models.PasswordResetToken
                {
                    UserId = user.Id,
                    TokenHash = TokenService.Hash(raw),
                    CreatedAtUtc = now,
                    ExpiresAtUtc = now.AddMinutes(minutes)
                });
                await _context.SaveChangesAsync();

                var link = ResetLinkBase() + "/Account/ResetPassword?token=" + Uri.EscapeDataString(raw);
                await _notifications.PasswordResetRequestedAsync(user, link, minutes);
            }
            catch (Exception)
            {
                // Never reveal or log details: the response must be identical for every valid-shaped request.
                _logger.LogWarning("Forgot password processing failed.");
            }
            return Accepted();
        }

        [HttpPost("reset-password")]
        [EnableRateLimiting("password")]
        public async Task<IActionResult> ResetPassword(ResetPasswordRequestDto request)
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var hash = TokenService.Hash(request.Token ?? string.Empty);
            var token = await _context.PasswordResetTokens.Include(t => t.User)
                .FirstOrDefaultAsync(t => t.TokenHash == hash);
            if (token?.User == null || token.UsedAtUtc != null || token.ExpiresAtUtc <= now)
                return InvalidResetToken();

            var user = token.User;
            user.PasswordHash = new PasswordHasher<Models.User>().HashPassword(user, request.NewPassword);
            token.MarkUsed(now);
            var siblings = await _context.PasswordResetTokens
                .Where(t => t.UserId == user.Id && t.Id != token.Id && t.UsedAtUtc == null)
                .ToListAsync();
            foreach (var t in siblings) t.MarkUsed(now);

            try
            {
                await RevokeAllRefreshTokensAsync(user.Id, now);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return InvalidResetToken();
            }

            await _notifications.PasswordChangedAsync(user);
            return NoContent();
        }

        private ActionResult FieldError(string key, string message)
        {
            ModelState.AddModelError(key, message);
            return ValidationProblem(ModelState);
        }

        private ActionResult InvalidResetToken() => FieldError("Token", InvalidResetTokenMessage);

        private async Task RevokeAllRefreshTokensAsync(int userId, DateTime now)
        {
            var active = await _context.RefreshTokens
                .Where(r => r.UserId == userId && r.RevokedAtUtc == null)
                .ToListAsync();
            foreach (var t in active) t.Revoke(now);
        }

        private int ConfigInt(string key, int fallback, bool allowZero) =>
            int.TryParse(_configuration[key], out var v) && (v > 0 || (allowZero && v == 0)) ? v : fallback;

        private string ResetLinkBase()
        {
            var configured = _configuration["App:PublicMvcBaseUrl"];
            if (!string.IsNullOrWhiteSpace(configured)
                && Uri.TryCreate(configured.Trim(), UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                return configured.Trim().TrimEnd('/');
            _logger.LogWarning("App:PublicMvcBaseUrl is missing or invalid; using the default.");
            return DefaultMvcBaseUrl;
        }

        private async Task<LoginResponseDto> IssueAsync(Models.User user, Guid familyId, Models.RefreshToken? replacing = null)
        {
            var (access, accessExpires) = _tokens.CreateAccessToken(user);
            var (raw, entity) = _tokens.CreateRefreshToken(user.Id, familyId);
            _context.RefreshTokens.Add(entity);
            if (replacing != null)
            {
                replacing.Revoke(DateTime.UtcNow);
                replacing.ReplacedByTokenHash = entity.TokenHash;
            }
            await _context.SaveChangesAsync();
            return new LoginResponseDto(access, accessExpires, user.Role, raw, entity.ExpiresAtUtc);
        }
    }
}
