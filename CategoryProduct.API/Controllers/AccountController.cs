using CategoryProduct.API.Data;
using CategoryProduct.API.Services;
using CategoryProduct.API.Dtos;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace CategoryProduct.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AccountController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly TokenService _tokens;
        private readonly IConfiguration _configuration;

        public AccountController(ApplicationDbContext context, TokenService tokens, IConfiguration configuration)
        {
            _context = context;
            _tokens = tokens;
            _configuration = configuration;
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
