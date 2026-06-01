using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using WohnungenApi.Dtos;
using WohnungenApi.Services;
using WohnungenApi.Models;
using System.Security.Claims;

namespace WohnungenApi.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ProfileController : ControllerBase
    {
        private readonly UserManager<Benutzer> _userManager;
        private readonly IPhotoService _photoService;

        public ProfileController(
            UserManager<Benutzer> userManager,
            IPhotoService photoService)
        {
            _userManager = userManager;
            _photoService = photoService;
        }

        private async Task<Benutzer?> GetCurrentUserAsync()
        {
            return await _userManager.GetUserAsync(User);
        }

        // ============================
        // جلب المستخدم الحالي
        // ============================
        [HttpGet("me")]
        public async Task<IActionResult> GetCurrentUser()
        {
            var user = await GetCurrentUserAsync();
            var roles = await _userManager.GetRolesAsync(user);
            if (user == null) return Unauthorized();

            return Ok(new
            {
                user.Id,
                user.Email,
                user.DisplayName,
                user.Vorname,
                user.Name,
                Phone = user.PhoneNumber,
                user.IsAgent,
                user.TaxNumber,
                user.ImageUrl,
                Roles = roles,
                Role = roles.FirstOrDefault()
            });
        }

        // ============================
        // تحديث البيانات
        // ============================
        [HttpPut]
        public async Task<IActionResult> UpdateProfile([FromBody] ProfileDto dto)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized();

            if (dto.IsAgent == true &&
                (string.IsNullOrWhiteSpace(dto.Phone) ||
                 string.IsNullOrWhiteSpace(dto.TaxNumber)))
            {
                return BadRequest("PhoneNumber and TaxNumber are required for agents.");
            }

            user.Vorname = dto.Vorname;
            user.Name = dto.Name;
            user.DisplayName = dto.DisplayName;
            user.PhoneNumber = dto.Phone;
            user.IsAgent = dto.IsAgent;
            user.TaxNumber = dto.TaxNumber;

            var result = await _userManager.UpdateAsync(user);

            if (!result.Succeeded)
                return BadRequest(result.Errors);

            return Ok(user);
        }

        // ============================
        // تغيير كلمة المرور (الطريقة الصحيحة)
        // ============================
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword(ChangePasswordDto dto)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized();

            var result = await _userManager.ChangePasswordAsync(
                user,
                dto.CurrentPassword,
                dto.NewPassword);

            if (!result.Succeeded)
                return BadRequest(result.Errors);

            return Ok(new { message = "Password changed successfully" });
        }

        // ============================
        // رفع الصورة
        // ============================
        [HttpPost("avatar")]
        public async Task<IActionResult> UploadAvatar([FromForm] AvatarUploadDto dto)
        {
            if (dto.File == null || dto.File.Length == 0)
                return BadRequest("File is empty");

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var extension = Path.GetExtension(dto.File.FileName).ToLowerInvariant();

            if (!allowedExtensions.Contains(extension))
                return BadRequest("Invalid file type");

            if (dto.File.Length > 5 * 1024 * 1024)
                return BadRequest("Max file size is 5MB");

            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized();

            var result = await _photoService.AddPhotoAsync(dto.File);

            if (result.Error != null)
                return BadRequest(result.Error.Message);

            user.ImageUrl = result.SecureUrl.AbsoluteUri;

            var updateResult = await _userManager.UpdateAsync(user);

            if (!updateResult.Succeeded)
                return BadRequest(updateResult.Errors);

            return Ok(new { imageUrl = user.ImageUrl });
        }

        // ============================
        // حذف الحساب
        // لكن الأفضل لاحقًا أن يكون Soft Delete وليس حذفًا فعليًا. 
        // ============================
        [HttpDelete("delete")]
        public async Task<IActionResult> DeleteAccount()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return Unauthorized();

            var result = await _userManager.DeleteAsync(user);

            if (!result.Succeeded)
                return BadRequest(result.Errors);

            return Ok(new { message = "Account deleted successfully" });
        }
    }
}