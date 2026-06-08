using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropertyApi.Domain.Messaging.Entities;
using PropertyApi.Infrastructure.Persistence;
using PropertyApi.Domain.Users.Constants;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.RateLimiting;

namespace PropertyApi.Controllers;

/// <summary>
/// نقطة نهاية نموذج "اتصل بنا".
///   1. يستخدم AppDbContext بدل WohnungenContext
///   2. حقل Body بدل Message (متوافق مع Domain entity)
///   3. IpAddress يُسجَّل تلقائياً لمكافحة الـ Spam
///   4. Guid Id بدل int
/// ملاحظة Frontend: المسار api/contact لم يتغير ✅
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class ContactController : ControllerBase
{
    private readonly AppDbContext _db;

    public ContactController(AppDbContext db)
        => _db = db;

    // ----------------------------------------------------------
    // POST /api/contact  (عام - بدون تسجيل دخول)
    // ----------------------------------------------------------
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [EnableRateLimiting("contact")]
    public async Task<IActionResult> Submit(
        [FromBody] ContactSubmitRequest dto,
        CancellationToken ct)
    {
        if (dto is null)
            return BadRequest(new { message = "Request body is required." });

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        if (string.IsNullOrWhiteSpace(dto.Name) ||
            string.IsNullOrWhiteSpace(dto.Email) ||
            string.IsNullOrWhiteSpace(dto.Message))
        {
            return BadRequest(new { message = "Name, Email and Message are required." });
        }

        var msg = new ContactMessage
        {
            Name = dto.Name.Trim(),
            Email = dto.Email.Trim().ToLowerInvariant(),
            Subject = dto.Subject?.Trim(),
            Body = dto.Message.Trim(),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
        };

        _db.ContactMessages.Add(msg);
        await _db.SaveChangesAsync(ct);

        return Ok(new { success = true, id = msg.Id });
    }

    // ----------------------------------------------------------
    // GET /api/contact  (Admin فقط)
    // ----------------------------------------------------------
    [HttpGet]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 20,
    CancellationToken ct = default)
    {

        var query = _db.ContactMessages
    .AsNoTracking()
    .OrderByDescending(message => message.CreatedAt);

        var total = await query.CountAsync(ct);

        var messages = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(message => new ContactMessageDto(
                message.Id,
                message.Name,
                message.Email,
                message.Subject,
                message.Body,
                message.IsRead,
                message.CreatedAt))
            .ToListAsync(ct);

        return Ok(new
        {
            total,
            page,
            pageSize,
            data = messages
        });
    }

    // ----------------------------------------------------------
    // GET /api/contact/{id}  (Admin فقط)
    // ----------------------------------------------------------
    [HttpGet("{id:guid}")]
    [Authorize(Roles = RoleNames.Admin)]

    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var msg = await _db.ContactMessages.FindAsync(new object[] { id }, ct);
        if (msg is null) return NotFound();

        return Ok(new ContactMessageDto(
            msg.Id, msg.Name, msg.Email, msg.Subject,
            msg.Body, msg.IsRead, msg.CreatedAt));
    }

    // ----------------------------------------------------------
    // PUT /api/contact/{id}/read  (Admin فقط)
    // ----------------------------------------------------------
    [HttpPut("{id:guid}/read")]
    [Authorize(Roles = RoleNames.Admin)]

    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkAsRead(Guid id, CancellationToken ct)
    {
        var msg = await _db.ContactMessages.FindAsync(new object[] { id }, ct);
        if (msg is null) return NotFound();

        msg.IsRead = true;
        msg.ReadAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    // ----------------------------------------------------------
    // DELETE /api/contact/{id}  (Admin فقط - Soft Delete)
    // ----------------------------------------------------------
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = RoleNames.Admin)]

    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var msg = await _db.ContactMessages.FindAsync(new object[] { id }, ct);
        if (msg is null) return NotFound();

        // AppDbContext يُحوِّل الحذف إلى Soft Delete تلقائياً
        _db.ContactMessages.Remove(msg);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }
}

// ------------------------------------------------------------------
// Request & Response DTOs  (داخل نفس الملف - صغيرة ولا تُبرَّر ملفاً منفصلاً)
// ------------------------------------------------------------------

/// <summary>
/// بيانات نموذج التواصل القادمة من الـ Frontend
/// متوافقة 100% مع الـ Legacy ContactDto
/// </summary>
/// 


public sealed record ContactSubmitRequest(
    [Required]
    [StringLength(150, MinimumLength = 2)]
    string Name,

    [Required]
    [EmailAddress]
    [StringLength(320)]
    string Email,

    [StringLength(300)]
    string? Subject,

    [Required]
    [StringLength(5000, MinimumLength = 10)]
    string Message);

/// <summary>
/// البيانات المُعادة عند قراءة الرسائل (Admin)
/// </summary>
public sealed record ContactMessageDto(
    Guid Id,
    string Name,
    string Email,
    string? Subject,
    string Body,
    bool IsRead,
    DateTime CreatedAt
);