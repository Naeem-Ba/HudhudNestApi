using Microsoft.AspNetCore.Mvc;
using WohnungenApi.Data;
using WohnungenApi.Models;
using Microsoft.EntityFrameworkCore;
using WohnungenApi.Dtos;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace WohnungenApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BenutzerController : ControllerBase
    {
        private readonly WohnungenContext _context;

        public BenutzerController(WohnungenContext context)
        {
            _context = context;
        }

        // ----------------------------
        // REGISTER
        // ----------------------------
        [HttpPost("register")]
        public async Task<IActionResult> CreateUser([FromBody] BenutzerDto dto)
        {
            // تحقق من وجود المستخدم
            var exists = await _context.Benutzer.AnyAsync(u => u.Email == dto.Email);
            if (exists)
                return BadRequest("Benutzer existiert bereits");

            // إنشاء كائن Benutzer الحقيقية
            var benutzer = new Benutzer
            {
                DisplayName = dto.DisplayName,
                IsAgent = dto.IsAgent,
                Name = dto.Name,
                Vorname = dto.Vorname,
                Email = dto.Email,
                Phone = dto.Phone,
                PasswordHash = dto.PasswordHash,
                CreatedAt = DateTime.Now,
                Role = "user"
            };

            try
            {
                _context.Benutzer.Add(benutzer);
                await _context.SaveChangesAsync();
                return Ok(benutzer);
            }
            catch (DbUpdateException dbEx)
            {
                return StatusCode(500, $"Datenbankfehler: {dbEx.InnerException?.Message}");
            }
        }

        // ----------------------------
        // LOGIN
        // ----------------------------
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] BenutzerDto dto, [FromServices] JwtService jwt)
        {
            if (dto == null || string.IsNullOrEmpty(dto.Email) || string.IsNullOrEmpty(dto.PasswordHash))
                return BadRequest("Ungültige Daten");

            var user = await _context.Benutzer
                .FirstOrDefaultAsync(u => u.Email == dto.Email && u.PasswordHash == dto.PasswordHash);

            if (user == null)
                return Unauthorized("Falsche E-Mail oder Passwort");

            var token = jwt.GenerateToken(user);

            return Ok(new
            {
                token,
                user = new
                {
                    user.Id,
                    user.Email,
                    user.Role,
                    user.DisplayName
                }
            });
        }

        // ----------------------------
        // UPDATE ROLE
        // ----------------------------
        [Authorize]
        [HttpPut("{id}/role")]
        public async Task<IActionResult> UpdateRole(int id, [FromBody] string newRole)
        {
            var user = await _context.Benutzer.FindAsync(id);
            if (user == null)
                return NotFound("User not found.");

            if (string.IsNullOrWhiteSpace(newRole))
                return BadRequest("Invalid role.");

            if (newRole != "Admin" && newRole != "user")
                return BadRequest("Role must be either 'Admin' or 'user'.");

            user.Role = newRole;
            await _context.SaveChangesAsync();

            return Ok(user);
        }

        [Authorize]
        [HttpGet("me")]
        public IActionResult Me()
        {
            return Ok(new
            {
                email = User.FindFirst(ClaimTypes.Email)?.Value,
                role = User.FindFirst(ClaimTypes.Role)?.Value
            });
        }

        // ----------------------------
        // UPDATE ROLE
        // ----------------------------
        //[Authorize]
        //[HttpPost("change-password")]
        //public async Task<IActionResult> ChangePassword([FromBody] BenutzerDto dto)
        //{
        //    var userId = GetCurrentUserId();
        //    var user = await _context.Benutzer.FindAsync(userId);

        //    // التحقق من كلمة المرور القديمة (تأكد من استخدام التشفير إذا كنت تشفرها)
        //    if (user.PasswordHash != dto.CurrentPassword)
        //        return BadRequest("كلمة المرور الحالية غير صحيحة");

        //    user.PasswordHash = dto.NewPassword; // يفضل تشفيرها هنا
        //    await _context.SaveChangesAsync();

        //    return Ok();
        //}

    }
}
