using CategoryProduct.API.Data;
using CategoryProduct.API.Services;
using CategoryProduct.API.Dtos;
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

        public AccountController(ApplicationDbContext context, TokenService tokens)
        {
            _context = context;
            _tokens = tokens;
        }

        [HttpPost("register")]
        [EnableRateLimiting("register")]
        public async Task<IActionResult> Register(RegisterRequestDto request)
        {
            var userName = request.UserName.Trim();
            var lower = userName.ToLower();
            if (await _context.Users.AnyAsync(u => u.UserName.ToLower() == lower))
                return Conflict(new ProblemDetails { Status = 409, Title = "Conflict", Detail = "Username already taken." });

            var user = new Models.User { UserName = userName, PasswordHash = "", Role = "User" };
            user.PasswordHash = new PasswordHasher<Models.User>().HashPassword(user, request.Password);
            _context.Users.Add(user);
            try { await _context.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                return Conflict(new ProblemDetails { Status = 409, Title = "Conflict", Detail = "Username already taken." });
            }

            return StatusCode(StatusCodes.Status201Created, new UserResponseDto(user.Id, user.UserName, user.Role));
        }

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

            return Ok(await IssueAsync(user));
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
                var active = await _context.RefreshTokens
                    .Where(r => r.UserId == stored.UserId && r.RevokedAtUtc == null)
                    .ToListAsync();
                foreach (var t in active) t.RevokedAtUtc = now;
                await _context.SaveChangesAsync();
                return Unauthorized();
            }
            if (stored.ExpiresAtUtc <= now)
                return Unauthorized();

            var user = await _context.Users.FindAsync(stored.UserId);
            if (user == null)
                return Unauthorized();

            var response = await IssueAsync(user, stored);
            return Ok(response);
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout(RefreshRequestDto request)
        {
            var hash = TokenService.Hash(request.RefreshToken ?? string.Empty);
            var stored = await _context.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == hash);
            if (stored != null && stored.RevokedAtUtc == null)
            {
                stored.RevokedAtUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
            return NoContent();
        }

        private async Task<LoginResponseDto> IssueAsync(Models.User user, Models.RefreshToken? replacing = null)
        {
            var (access, accessExpires) = _tokens.CreateAccessToken(user);
            var (raw, entity) = _tokens.CreateRefreshToken(user.Id);
            _context.RefreshTokens.Add(entity);
            if (replacing != null)
            {
                replacing.RevokedAtUtc = DateTime.UtcNow;
                replacing.ReplacedByTokenHash = entity.TokenHash;
            }
            await _context.SaveChangesAsync();
            return new LoginResponseDto(access, accessExpires, user.Role, raw, entity.ExpiresAtUtc);
        }
    }
}
