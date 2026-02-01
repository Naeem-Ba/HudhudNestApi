using Microsoft.AspNetCore.Mvc;
using WohnungenApi.Data;
using WohnungenApi.Models;
using Microsoft.EntityFrameworkCore;
using WohnungenApi.Dtos;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

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
        // UPDATE ROLE
        // ----------------------------
        [Authorize(Roles = "Admin")]
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
        // change-password
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
