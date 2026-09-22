using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using HudhudNestApi.Application.Reviews.Commands.RateUser;
using HudhudNestApi.Application.Reviews.Queries.GetRatingEligibility;
using HudhudNestApi.Application.Reviews.Queries.GetUserRatings;
using HudhudNestApi.Application.Users.Commands.CancelAccountDeletion;
using HudhudNestApi.Application.Users.Commands.ChangePassword;
using HudhudNestApi.Application.Users.Commands.RecordConsent;
using HudhudNestApi.Application.Users.Commands.RequestDeleteUser;
using HudhudNestApi.Application.Users.Commands.SelectPlan;
using HudhudNestApi.Application.Users.Commands.UpdateUser;
using HudhudNestApi.Application.Users.Commands.UploadUserAvatar;
using HudhudNestApi.Application.Users.Commands.WithdrawConsent;
using HudhudNestApi.Application.Users.DTOs;
using HudhudNestApi.Application.Users.Queries.ExportMyData;
using HudhudNestApi.Application.Users.Queries.GetCurrentUser;
using HudhudNestApi.Application.Users.Queries.GetMyConsents;
using HudhudNestApi.Application.Users.Queries.GetUserById;
using HudhudNestApi.Application.Users.Queries.GetUserProfile;
using HudhudNestApi.Domain.Enums;

namespace HudhudNestApi.Controllers;

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
    [EnableRateLimiting("public-read")]
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
    [EnableRateLimiting("public-read")]
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

    // POST /api/Users/me/plan — records the caller's explicit plan choice (including
    // "free"). Gates listing creation/publishing — see CreatePropertyCommandHandler.
    [HttpPost("me/plan")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SelectPlan(
        [FromBody] SelectPlanRequest dto,
        CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var result = await _sender.Send(new SelectPlanCommand(userId, dto.Tier), ct);

        return result ? NoContent() : Unauthorized();
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

    // DELETE /api/Users/me — SCHEDULES deletion of the CALLER's own account (Finding F7,
    // docs/ACCOUNT-DELETION-PRODUCTION-READINESS.md): re-authenticates immediately, then starts
    // a cancellable delay window rather than anonymizing synchronously — the account remains
    // fully usable until AccountDeletionSweepHostedService executes the request once its
    // ScheduledFor date arrives. UserId always comes from the authenticated principal's claims,
    // never from the request body or URL, so this can never be pointed at another user's
    // account (see RequestDeleteUserCommand's doc comment).
    //
    // BREAKING CHANGE from the previous immediate-deletion behavior: this used to return 204 on
    // an already-completed deletion; it now returns 202 with the scheduled date, since the
    // account is not yet anonymized when this call returns. Any client built against the old
    // contract needs updating.
    [HttpDelete("me")]
    [EnableRateLimiting("account-delete")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteAccount(
        [FromBody] DeleteAccountRequest? dto,
        CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var result = await _sender.Send(new RequestDeleteUserCommand(
            UserId: userId,
            CurrentPassword: dto?.CurrentPassword), ct);

        if (result.NotFound)
            return Unauthorized();

        if (!result.Success)
            return BadRequest(new { errors = result.Errors });

        return Accepted(new { scheduledFor = result.ScheduledFor });
    }

    // POST /api/Users/me/deletion/cancel — cancels a pending deletion request made via
    // DELETE /api/Users/me, any time before its delay window elapses (Finding F7). No
    // re-authentication: an authenticated session is already at least as strong a bar as the
    // one that started the request, and requiring the password again here would make backing
    // out of a deletion harder than starting one.
    [HttpPost("me/deletion/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelAccountDeletion(CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var result = await _sender.Send(new CancelAccountDeletionCommand(userId), ct);

        if (result.NotFound)
            return Unauthorized();

        if (result.NoPendingRequest)
            return NotFound();

        return NoContent();
    }

    // GET /api/Users/me/export — self-service data export (Finding F7,
    // docs/ACCOUNT-DELETION-PRODUCTION-READINESS.md). UserId always comes from the
    // authenticated principal, never from a route/query parameter, so this can never return
    // another user's data (see AccountDataExportRepository's own doc comment). Returned
    // directly as the JSON response body -- see AccountDataExportDto for what is included and
    // deliberately excluded.
    [HttpGet("me/export")]
    [EnableRateLimiting("data-export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ExportMyData(CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var export = await _sender.Send(new ExportMyDataQuery(userId), ct);

        if (export is null)
            return Unauthorized();

        Response.Headers.ContentDisposition =
            $"attachment; filename=\"propertyapi-account-export-{DateTime.UtcNow:yyyyMMdd}.json\"";

        return Ok(export);
    }

    // POST /api/Users/me/consents — records that the caller just explicitly agreed to one
    // version of one policy document, from one client surface. The client must only ever
    // call this in direct response to the user actually checking an unchecked consent
    // checkbox and submitting — see docs/privacy/privacy-gaps.md (P1).
    [HttpPost("me/consents")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RecordConsent(
        [FromBody] RecordConsentRequest dto,
        CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var result = await _sender.Send(
            new RecordConsentCommand(userId, dto.PolicyType, dto.PolicyVersion, dto.Source),
            ct);

        return Ok(result);
    }

    // GET /api/Users/me/consents — every consent record the caller has, including
    // withdrawn ones, so they (or support, on their behalf) can see the full history.
    [HttpGet("me/consents")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyConsents(CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var result = await _sender.Send(new GetMyConsentsQuery(userId), ct);

        return Ok(result);
    }

    // DELETE /api/Users/me/consents/{policyType} — withdraws every currently-active
    // consent the caller has for that policy type. Does not remove the historical rows;
    // WithdrawnAtUtc is set instead (see ConsentRecord's doc comment).
    [HttpDelete("me/consents/{policyType}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> WithdrawConsent(
        ConsentPolicyType policyType,
        CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var withdrawnCount = await _sender.Send(
            new WithdrawConsentCommand(userId, policyType),
            ct);

        return Ok(new { withdrawnCount });
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

// CurrentPassword is optional at the model-binding level because a social-login-only
// account has no password to submit at all — DeleteUserCommandHandler is what actually
// requires it (returns CURRENT_PASSWORD_REQUIRED) when the identity has one.
public sealed record DeleteAccountRequest(
    string? CurrentPassword = null);

public sealed record SelectPlanRequest(string Tier);

public sealed record RecordConsentRequest(
    ConsentPolicyType PolicyType,
    string PolicyVersion,
    ConsentSource Source);

public sealed record RateUserRequest(
    int Credibility,
    int Safety,
    int ResponseSpeed,
    int Transparency,
    string? Comment);
