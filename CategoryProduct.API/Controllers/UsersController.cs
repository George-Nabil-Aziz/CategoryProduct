using CategoryProduct.API.Data;
using CategoryProduct.API.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CategoryProduct.API.Controllers
{
    [Route("api/users")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class UsersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public UsersController(ApplicationDbContext context) => _context = context;

        [HttpGet]
        public async Task<ActionResult<PagedResponseDto<UserResponseDto>>> GetUsers(
            int page = 1, int pageSize = 10, string? search = null)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 50);

            var query = _context.Users.AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(u => u.UserName.ToLower().Contains(s));
            }

            var total = await query.CountAsync();
            var items = await query.OrderBy(u => u.UserName)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(u => new UserResponseDto(u.Id, u.UserName, u.Role))
                .ToListAsync();
            var totalPages = (int)Math.Ceiling(total / (double)pageSize);
            return new PagedResponseDto<UserResponseDto>(items, page, pageSize, total, totalPages);
        }

        [HttpPut("{id}/role")]
        public async Task<IActionResult> UpdateRole(int id, UpdateRoleRequestDto request)
        {
            if (request.Role != "User" && request.Role != "Admin")
                return BadRequest(new ValidationProblemDetails(
                    new Dictionary<string, string[]> { ["Role"] = new[] { "Role must be 'User' or 'Admin'." } }));

            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            if (user.Role == "Admin" && request.Role != "Admin"
                && !await _context.Users.AnyAsync(u => u.Role == "Admin" && u.Id != id))
                return Conflict(new ProblemDetails { Status = 409, Title = "Conflict", Detail = "Cannot remove the last admin." });

            user.Role = request.Role;
            await _context.SaveChangesAsync();
            return Ok(new UserResponseDto(user.Id, user.UserName, user.Role));
        }
    }
}
