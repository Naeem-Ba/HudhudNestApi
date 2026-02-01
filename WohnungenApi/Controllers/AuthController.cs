using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using WohnungenApi.Data;
using WohnungenApi.Dtos;
using WohnungenApi.Models;

namespace WohnungenApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly WohnungenContext _context;
        private readonly JwtService _jwtService;
        public AuthController(WohnungenContext context, JwtService jwtService)
        {
            _context = context;
            _jwtService = jwtService;
        }

        // ----------------------------
        // REGISTER
        // ----------------------------
        [HttpPost("register")]
        public async Task<IActionResult> Register(BenutzerDto dto)
        {
            // 1️⃣ تحقق من الإيميل
            if (await _context.Benutzer.AnyAsync(x => x.Email == dto.Email))
            {
                return BadRequest("Email already exists");
            }

            // 2️⃣ إنشاء المستخدم (بدون كلمة السر)
            var user = new Benutzer
            {
                DisplayName = dto.DisplayName,
                IsAgent = dto.IsAgent,
                Name = dto.Name,
                Vorname = dto.Vorname,
                Email = dto.Email,
                Phone = dto.Phone
            };

            // 3️⃣ HASH كلمة السر (النقطة الأهم)
            var hasher = new PasswordHasher<Benutzer>();
            user.PasswordHash = hasher.HashPassword(user, dto.Password);

            // 4️⃣ حفظ
            _context.Benutzer.Add(user);
            await _context.SaveChangesAsync();

            return Ok("User registered successfully");
        }

        // ----------------------------
        // LOGIN
        // ----------------------------
        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginDto dto)
        {
            var user = await _context.Benutzer
                .FirstOrDefaultAsync(x => x.Email == dto.Email);

            if (user == null)
                return Unauthorized("Invalid credentials");

            var hasher = new PasswordHasher<Benutzer>();

            var result = hasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                dto.Password
            );

            if (result == PasswordVerificationResult.Failed)
                return Unauthorized("Invalid credentials");

            // ✅ توليد التوكن
            var token = _jwtService.GenerateToken(user);

            return Ok(new
            {
                token,
                user = new
                {
                    user.Id,
                    user.Email,
                    user.DisplayName,
                    user.Role
                }
            });
        }
    }
}
