using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WohnungenApi.Data;
using WohnungenApi.Dtos;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ProfileController : ControllerBase
{
    private readonly WohnungenContext _context;
    private readonly IWebHostEnvironment _env;

    public ProfileController(WohnungenContext context, IWebHostEnvironment env)
    {
        _context = context;
        _env = env;
    }

    // دالة مساعدة لجلب ID المستخدم بأمان
    private int GetCurrentUserId()
    {
        // 1. استخراج كل الـ Claims المتاحة في التوكن حالياً
        var allClaims = User.Claims.Select(c => $"{c.Type}: {c.Value}").ToList();
        var claimsString = string.Join(" | ", allClaims);

        // 2. محاولة البحث عن المعرف بأكثر من صيغة
        var claim = User.FindFirst(ClaimTypes.NameIdentifier) // الصيغة القياسية (http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier)
                    ?? User.FindFirst("Id")                   // الصيغة التي أعتقدها
                    ?? User.FindFirst("id")                   // الصيغة الصغيرة
                    ?? User.FindFirst("sub");                 // الصيغة العالمية

        if (claim == null)
        {
            // إذا فشل، سنعيد رسالة تحتوي على كل محتويات التوكن لنفهم ماذا أرسل الـ JWT
            throw new Exception($"ID not found. Available claims are: {claimsString}");
        }

        return int.Parse(claim.Value);
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
    {
        try
        {
            var userId = GetCurrentUserId();
            var user = await _context.Benutzer.FindAsync(userId);

            if (user == null) return NotFound();
            return Ok(user);
        }
        catch (Exception ex) { return BadRequest(ex.Message); }
    }

    [HttpPut]
    public async Task<IActionResult> Profile([FromBody] ProfileDto dto)
    {
        try
        {
            var userId = GetCurrentUserId();
            var user = await _context.Benutzer.FindAsync(userId);
            if (user == null) return NotFound();

            user.Vorname = dto.Vorname;
            user.Name = dto.Name;
            user.DisplayName = dto.DisplayName;
            user.Phone = dto.Phone;
            user.IsAgent = dto.IsAgent;

            await _context.SaveChangesAsync();
            return Ok(user);
        }
        catch (Exception) { return Unauthorized(); }
    }

    [HttpPost("avatar")]
    public async Task<IActionResult> UploadAvatar([FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest("File is empty");

        try
        {
            var userId = GetCurrentUserId();
            var user = await _context.Benutzer.FindAsync(userId);
            if (user == null) return NotFound();

            var uploadsPath = Path.Combine(_env.WebRootPath, "profile_fotos");
            if (!Directory.Exists(uploadsPath)) Directory.CreateDirectory(uploadsPath);

            var fileName = $"user_{userId}.jpg";
            var filePath = Path.Combine(uploadsPath, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            user.ImageUrl = $"/profile_fotos/{fileName}";
            await _context.SaveChangesAsync();

            return Ok(new { imageUrl = user.ImageUrl });
        }
        catch (Exception) { return Unauthorized(); }
    }
}