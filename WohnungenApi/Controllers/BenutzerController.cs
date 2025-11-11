using Microsoft.AspNetCore.Mvc;
using WohnungenApi.Data;
using WohnungenApi.Models;
using Microsoft.EntityFrameworkCore;

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

        [HttpPost("register")]
        public async Task<IActionResult> CreateUser([FromBody] Benutzer user)
        {
            Console.WriteLine($"📨 Empfangene Daten: Email={user?.Email}, Name={user?.Name}");

            var exist = await _context.Benutzer.AnyAsync(u => u.Email == user.Email);
            if (exist)
                return BadRequest("Benutzer existiert bereits");
            try
            {
                _context.Benutzer.Add(user);
                await _context.SaveChangesAsync();
                return Ok(user);
            }
            catch (DbUpdateException dbEx)
            {
                // ✅ هذا مهم لاكتشاف مشاكل قاعدة البيانات
                Console.WriteLine($"❌ Datenbankfehler: {dbEx.InnerException?.Message}");
                return StatusCode(500, $"Datenbankfehler: {dbEx.InnerException?.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ Fehler beim Speichern: " + ex.Message);
                return StatusCode(500, ex.Message);
            }
        }
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] Benutzer loginData)
        {
            if (loginData == null || string.IsNullOrEmpty(loginData.Email) || string.IsNullOrEmpty(loginData.PasswordHash))
                return BadRequest("Ungültige Daten");

            var user = await _context.Benutzer
                .FirstOrDefaultAsync(u => u.Email == loginData.Email && u.PasswordHash == loginData.PasswordHash);

            if (user == null)
                return Unauthorized("Falsche E-Mail oder Passwort");

            return Ok(user);
        }
    }
}
