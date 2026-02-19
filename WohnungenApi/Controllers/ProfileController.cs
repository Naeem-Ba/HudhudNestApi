using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WohnungenApi.Data;
using WohnungenApi.Dtos;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WohnungenApi.Services;
using Microsoft.AspNetCore.Identity;
using WohnungenApi.Models;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ProfileController : ControllerBase
{
    private readonly WohnungenContext _context;
    private readonly IPhotoService _photoService; // حقن خدمة الصور

    public ProfileController(WohnungenContext context, IPhotoService photoService)
    {
        _context = context;
        _photoService = photoService;
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
    public async Task<IActionResult> UpdateProfile([FromBody] ProfileDto dto)
    {
        var userId = GetCurrentUserId();
        var user = await _context.Benutzer.FindAsync(userId);
        if (user == null) return NotFound();

        //حماية إضافية: التأكد من وجود رقم الهاتف إذا كان الحساب وكيلاً
        if (dto.IsAgent == true && (string.IsNullOrWhiteSpace(dto.Phone) || string.IsNullOrWhiteSpace(dto.TaxNumber)))
        {
            return BadRequest("رقم الهاتف والرقم الضريبي مطلوبان لحسابات الوكلاء.");
        }

        user.Vorname = dto.Vorname;
        user.Name = dto.Name;
        user.DisplayName = dto.DisplayName;
        user.Phone = dto.Phone;
        user.IsAgent = dto.IsAgent;
        user.TaxNumber = dto.TaxNumber; // حفظ الرقم الضريبي

        await _context.SaveChangesAsync();
        return Ok(user);
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        var userId = GetCurrentUserId();
        var user = await _context.Benutzer.FindAsync(userId);
        if (user == null) return NotFound();

        //// ملاحظة أمنية: يفضل استخدام PasswordHasher لفك التشفير والمقارنة
        //// هنا نقارن مباشرة (إذا كنت تخزنها كنص عادي حالياً)
        //if (user.PasswordHash != dto.CurrentPassword)
        //{
        //    return BadRequest("كلمة المرور الحالية غير صحيحة.");
        //}

        //user.PasswordHash = dto.NewPassword;
        //await _context.SaveChangesAsync();

        var hasher = new PasswordHasher<Benutzer>();
        var verificationResult = hasher.VerifyHashedPassword(user, user.PasswordHash, dto.CurrentPassword);

        if (verificationResult == PasswordVerificationResult.Failed)
        {
            return BadRequest("كلمة المرور الحالية غير صحيحة.");
        }

        user.PasswordHash = hasher.HashPassword(user, dto.NewPassword);
        await _context.SaveChangesAsync();

        return Ok(new { message = "تم تغيير كلمة المرور بنجاح" });
    }

    [HttpPost("avatar")]
   
    public async Task<IActionResult> UploadAvatar([FromForm] AvatarUploadDto dto)
    {
        // 1. التأكد من وجود ملف
        if (dto.File == null || dto.File.Length == 0)
            return BadRequest("File is empty");

        //فحص نوع الملف ✅ 
        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        var extension = Path.GetExtension(dto.File.FileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(extension))
            return BadRequest ("(webp ,png ,jpg (نوع الملف غير مسموح. يرجى رفع صورة فقط");


        //(MB5) فحص حجم الملف ✅

             var maxFileSize = 5 * 1024 * 1024;
        if (dto.File.Length > maxFileSize)
            return BadRequest ("MBحجم الملف كبير جدً. الحد اأقصى 5");

        try
        {
            // 2. الحصول على معرف المستخدم الحالي (من التوكن)
            var userId = GetCurrentUserId();
            var user = await _context.Benutzer.FindAsync(userId);
            if (user == null) return NotFound("User not found");

            // 3. رفع الصورة إلى Cloudinary بدلاً من السيرفر المحلي
            var result = await _photoService.AddPhotoAsync(dto.File);

            if (result.Error != null)
                return BadRequest(result.Error.Message);

            // 4. تحديث رابط الصورة في قاعدة البيانات بالرابط الذي أعطاه Cloudinary
            user.ImageUrl = result.SecureUrl.AbsoluteUri;

            await _context.SaveChangesAsync();

            return Ok(new { imageUrl = user.ImageUrl });
        }
        catch (Exception) { return Unauthorized(); }
    }

}