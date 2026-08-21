using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropertyApi.Application.Reviews.Commands.RateUser;
using PropertyApi.Application.Reviews.Queries.GetRatingEligibility;
using PropertyApi.Application.Reviews.Queries.GetUserRatings;
using PropertyApi.Application.Users.Commands.ChangePassword;
using PropertyApi.Application.Users.Commands.DeleteUser;
using PropertyApi.Application.Users.Commands.UpdateUser;
using PropertyApi.Application.Users.Commands.UploadUserAvatar;
using PropertyApi.Application.Users.DTOs;
using PropertyApi.Application.Users.Queries.GetCurrentUser;
using PropertyApi.Application.Users.Queries.GetUserById;
using PropertyApi.Application.Users.Queries.GetUserProfile;

namespace PropertyApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class UsersController : ControllerBase
{
    private readonly ISender _sender;

    public UsersController(ISender sender)
        => _sender = sender;

    [HttpGet("me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetCurrentUser(CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var user = await _sender.Send(new GetCurrentUserQuery(userId), ct);
        return user is null ? Unauthorized() : Ok(user);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var user = await _sender.Send(new GetUserByIdQuery(id), ct);
        return user is null ? NotFound() : Ok(user);
    }

    // ── الصفحة الشخصية العامة (Public Profile) ──────────────────────────
    // GET /api/Users/{id}/profile — صورة، تاريخ الانضمام، عدد المُباع/المؤجَّر،
    // ومتوسطات التقييم. متاحة لأي زائر (حتى غير المسجَّل) تمامًا مثل
    // GET /api/Properties العام — نفس فكرة "من هو هذا البائع؟" قبل التواصل معه.
    [HttpGet("{id:guid}/profile")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProfile(Guid id, CancellationToken ct)
    {
        var profile = await _sender.Send(new GetUserProfileQuery(id), ct);
        return profile is null ? NotFound() : Ok(profile);
    }

    // GET /api/Users/{id}/ratings?page=&pageSize= — قائمة التقييمات + التعليقات
    [HttpGet("{id:guid}/ratings")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRatings(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _sender.Send(
            new GetUserRatingsQuery(id, page, pageSize), ct);
        return Ok(result);
    }

    // GET /api/Users/{id}/ratings/eligibility — هل يحق للمستخدم الحالي تقييم
    // صاحب هذا الملف الآن؟ وهل قيّمه من قبل (لعرض نموذج تعديل مُعبَّأ مسبقًا)؟
    // يتطلب تسجيل دخول (بعكس GetProfile/GetRatings) لأنه يحتاج راترId الفعلي.
    [HttpGet("{id:guid}/ratings/eligibility")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetRatingEligibility(Guid id, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var raterId))
            return Unauthorized();

        var result = await _sender.Send(new GetRatingEligibilityQuery(id, raterId), ct);
        return Ok(result);
    }

    // POST /api/Users/{id}/ratings — تقييم مستخدم (المصداقية/الأمان/سرعة الرد/الشفافية)
    // — يقبل أيضًا تعديل تقييم سابق (Upsert)، راجع RateUserCommandHandler.
    [HttpPost("{id:guid}/ratings")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RateUser(
        Guid id,
        [FromBody] RateUserRequest dto,
        CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var raterId))
            return Unauthorized();

        var result = await _sender.Send(new RateUserCommand(
            RatedUserId: id,
            RaterId: raterId,
            Credibility: dto.Credibility,
            Safety: dto.Safety,
            ResponseSpeed: dto.ResponseSpeed,
            Transparency: dto.Transparency,
            Comment: dto.Comment
        ), ct);

        return StatusCode(201, result);
    }

    // 🗑️ إزالة: كان هنا GET /api/users (Admin، بلا ترقيم صفحات) — أُزيل لأنه
    // كان مكرَّراً وظيفياً مع GET /api/admin/users (AdminController.GetUsers)
    // الذي يوفّر نفس البيانات مع ترقيم صفحات، وهو المُستخدَم فعلياً بالواجهة.
    // فحصتُ الاستخدام قبل الحذف: GetAllUsersQuery لم يكن مُستدعى من أي مكان
    // آخر بالباك اند (Controller وحيد فقط)، والواجهة لا تستهلكه إطلاقاً.
    // القرار وليس حذف عشوائي: تقليل السطح العام غير المُستخدَم لتسهيل الصيانة.

    [HttpPut("me")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdateProfileRequest dto,
        CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var result = await _sender.Send(new UpdateUserCommand(
            UserId: userId,
            FirstName: dto.FirstName,
            LastName: dto.LastName,
            DisplayName: dto.DisplayName,
            PhoneNumber: dto.PhoneNumber,
            ProfileImageUrl: null,
            PreferredLanguage: null,
            PreferredCurrency: null,
            CountryCode: null,
            Bio: dto.Bio,
            ContactInfo: dto.ContactInfo
        ), ct);

        return result is null ? NotFound() : NoContent();
    }

    // POST /api/Users/me/avatar — رفع/استبدال الصورة الشخصية (multipart/form-data).
    // نفس منهجية POST /api/properties/{id}/images (PropertyImagesController):
    // IFormFile → DTO لا يعرف ASP.NET → Command → Handler يتحقق ويرفع عبر
    // IMediaStorageService. الحد 2.5 ميجابايت بالباك اند (RequestSizeLimit) أعلى
    // بقليل من حد الواجهة (2 ميجابايت بالضبط) لاستيعاب overhead الـ multipart
    // نفسه، بينما التحقق الفعلي من حجم الصورة داخل Handler يبقى مطابقًا للواجهة.
    [HttpPost("me/avatar")]
    [RequestSizeLimit(2_500_000)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadAvatar(
        // No [FromForm] here: Swashbuckle throws "[FromForm] attribute used with IFormFile"
        // and fails the whole /swagger/v1/swagger.json document. IFormFile already binds from
        // multipart/form-data without the attribute, so it is redundant as well as harmful.
        IFormFile? file,
        CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        if (file is null || file.Length == 0)
            return BadRequest(new { message = "لم يتم رفع أي صورة." });

        var fileDto = new UploadUserAvatarFileDto(
            file.OpenReadStream(),
            file.FileName,
            file.ContentType,
            file.Length);

        var result = await _sender.Send(
            new UploadUserAvatarCommand(userId, fileDto), ct);

        return result.Status switch
        {
            UploadUserAvatarStatus.Success => Ok(new { ImageUrl = result.ImageUrl }),
            UploadUserAvatarStatus.NotFound => NotFound(),
            UploadUserAvatarStatus.ValidationFailed => BadRequest(new { message = result.Message }),
            UploadUserAvatarStatus.StorageFailed => BadRequest(new { message = result.Message }),
            _ => BadRequest(new { message = result.Message })
        };
    }

    [HttpPost("me/change-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest dto,
        CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var result = await _sender.Send(new ChangePasswordCommand(
            UserId: userId,
            CurrentPassword: dto.CurrentPassword,
            NewPassword: dto.NewPassword,
            IpAddress: GetClientIp()), ct);

        if (result.NotFound)
            return Unauthorized();

        if (!result.Success)
            return BadRequest(new { errors = result.Errors });

        return NoContent();
    }

    [HttpDelete("me")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteAccount(CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var result = await _sender.Send(new DeleteUserCommand(userId), ct);

        if (result.NotFound)
            return Unauthorized();

        if (!result.Success)
            return BadRequest(new { errors = result.Errors });

        return NoContent();
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdText, out userId);
    }

    private string? GetClientIp()
        => HttpContext.Connection.RemoteIpAddress?.ToString();
}

public sealed record UpdateProfileRequest(
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string? PhoneNumber,
    string? Bio = null,
    string? ContactInfo = null);

public sealed record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword);

public sealed record RateUserRequest(
    int Credibility,
    int Safety,
    int ResponseSpeed,
    int Transparency,
    string? Comment);
