using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CategoryProduct.API.Data;
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
        private readonly IConfiguration _configuration;

        public AccountController(ApplicationDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
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

            var expiryMinutes = _configuration.GetValue<int>("Jwt:ExpiryMinutes", 60);
            var expiresAtUtc = DateTime.UtcNow.AddMinutes(expiryMinutes);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.UserName),
                new Claim(ClaimTypes.Role, user.Role)
            };

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_configuration["Jwt:Key"] ?? string.Empty));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature);

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: expiresAtUtc,
                signingCredentials: credentials);

            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

            return Ok(new LoginResponseDto(tokenString, expiresAtUtc, user.Role));
        }
    }
}
