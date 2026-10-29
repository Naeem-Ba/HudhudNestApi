using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Notifications.Localization;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.ShortStay.Interfaces;
using HudhudNestApi.Domain.Audit.Constants;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Domain.ShortStay.Entities;

namespace HudhudNestApi.Application.ShortStay.Admin;

// P9-D11: short-stay listings had no administrative take-down. Mirrors the sale/rent one (P8-D10): the
// listing is unpublished with a reason the owner can read, the owner cannot publish it again, existing
// bookings are untouched, and the action is reversible and audited.

public sealed record AdminShortStayListingDto(
    Guid Id,
    string Title,
    string? City,
    Guid OwnerId,
    string AccommodationTypeNameAr,
    bool IsPublished,
    bool IsTakenDown,
    DateTime? TakenDownAt,
    string? TakenDownReason,
    DateTime CreatedAt);

public sealed record AdminShortStayListingFilter(string? Search, bool? TakenDown, int Page, int PageSize);

public sealed record SearchAdminShortStayListingsQuery(AdminShortStayListingFilter Filter)
    : IRequest<PagedResult<AdminShortStayListingDto>>;

public sealed class SearchAdminShortStayListingsQueryHandler
    : IRequestHandler<SearchAdminShortStayListingsQuery, PagedResult<AdminShortStayListingDto>>
{
    private readonly IShortStayListingRepository _listings;

    public SearchAdminShortStayListingsQueryHandler(IShortStayListingRepository listings) => _listings = listings;

    public async Task<PagedResult<AdminShortStayListingDto>> Handle(
        SearchAdminShortStayListingsQuery request, CancellationToken ct)
    {
        var f = request.Filter;
        var page = Math.Max(1, f.Page);
        var pageSize = Math.Clamp(f.PageSize, 1, 100);

        var search = f.Search is null ? null : new string(f.Search.Where(c => !char.IsControl(c)).ToArray());
        var result = await _listings.SearchForAdminAsync(search, f.TakenDown, page, pageSize, ct);

        return new PagedResult<AdminShortStayListingDto>
        {
            Items = result.Items.Select(l => new AdminShortStayListingDto(
                l.Id, l.Title, l.City, l.OwnerId, l.AccommodationType.NameAr, l.IsPublished,
                l.IsTakenDown, l.TakenDownAt, l.TakenDownReason, l.CreatedAt)).ToList(),
            TotalCount = result.TotalCount,
            Page = page,
            PageSize = pageSize
        };
    }
}

public sealed record TakeDownShortStayListingCommand(Guid AdminId, Guid ListingId, string? Reason, string? IpAddress)
    : IRequest<AdminShortStayListingDto>;

public sealed class TakeDownShortStayListingCommandValidator : AbstractValidator<TakeDownShortStayListingCommand>
{
    public TakeDownShortStayListingCommandValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A reason is required to take a listing down.")
            .MaximumLength(ShortStayListing.MaxTakeDownReasonLength)
            .Must(r => r is null || !r.Any(char.IsControl)).WithMessage("The reason contains invalid characters.");
    }
}

public sealed record RestoreShortStayListingCommand(Guid AdminId, Guid ListingId, string? Reason, string? IpAddress)
    : IRequest<AdminShortStayListingDto>;

public sealed class RestoreShortStayListingCommandValidator : AbstractValidator<RestoreShortStayListingCommand>
{
    public RestoreShortStayListingCommandValidator()
    {
        RuleFor(x => x.Reason)
            .MaximumLength(ShortStayListing.MaxTakeDownReasonLength)
            .Must(r => r is null || !r.Any(char.IsControl)).WithMessage("The reason contains invalid characters.");
    }
}

public sealed class AdminShortStayListingCommandHandler
    : IRequestHandler<TakeDownShortStayListingCommand, AdminShortStayListingDto>,
      IRequestHandler<RestoreShortStayListingCommand, AdminShortStayListingDto>
{
    private readonly IShortStayListingRepository _listings;
    private readonly IUnitOfWork _uow;
    private readonly IAuditLogService _audit;
    private readonly INotificationService _notifications;
    private readonly ILogger<AdminShortStayListingCommandHandler> _logger;

    public AdminShortStayListingCommandHandler(
        IShortStayListingRepository listings, IUnitOfWork uow, IAuditLogService audit,
        INotificationService notifications, ILogger<AdminShortStayListingCommandHandler> logger)
    {
        _listings = listings;
        _uow = uow;
        _audit = audit;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<AdminShortStayListingDto> Handle(TakeDownShortStayListingCommand request, CancellationToken ct)
    {
        var listing = await _listings.GetByIdWithDetailsAsync(request.ListingId, ct)
            ?? throw new NotFoundException("Listing not found.");

        var before = Snapshot(listing);
        listing.TakeDown(request.AdminId, request.Reason!);
        _listings.Update(listing);
        await _uow.SaveChangesAsync(ct);

        await _audit.LogAsync(request.AdminId, AuditActions.ShortStayListingTakenDownByAdmin, request.IpAddress,
            before, JsonSerializer.Serialize(new { snapshot = Snapshot(listing), reason = listing.TakenDownReason }), ct);

        await TryNotifyAsync(listing, NotificationType.ListingTakenDown,
            NotificationText.FromLiteral(listing.TakenDownReason), ct);

        return ToDto(listing);
    }

    public async Task<AdminShortStayListingDto> Handle(RestoreShortStayListingCommand request, CancellationToken ct)
    {
        var listing = await _listings.GetByIdWithDetailsAsync(request.ListingId, ct)
            ?? throw new NotFoundException("Listing not found.");

        var before = Snapshot(listing);
        listing.RestoreFromTakeDown();
        _listings.Update(listing);
        await _uow.SaveChangesAsync(ct);

        await _audit.LogAsync(request.AdminId, AuditActions.ShortStayListingRestoredByAdmin, request.IpAddress,
            before, JsonSerializer.Serialize(new { snapshot = Snapshot(listing), reason = request.Reason }), ct);

        await TryNotifyAsync(listing, NotificationType.ListingRestored,
            NotificationText.Of(NotificationKeys.ShortStayListingRestoredDetail, ("title", listing.Title)), ct);

        return ToDto(listing);
    }

    // The take-down is committed; telling the owner is best-effort. The listing id travels as the related
    // entity id (a short-stay listing is not a Property).
    private async Task TryNotifyAsync(ShortStayListing listing, NotificationType type, NotificationText detail, CancellationToken ct)
    {
        try
        {
            await _notifications.NotifyShortStayBookingUpdateAsync(listing.OwnerId, listing.Id, listing.Title, type, detail, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to notify the owner about an admin short-stay listing action. ListingId={ListingId}", listing.Id);
        }
    }

    private static string Snapshot(ShortStayListing l)
        => JsonSerializer.Serialize(new { listingId = l.Id, ownerId = l.OwnerId, l.IsPublished, l.IsTakenDown });

    private static AdminShortStayListingDto ToDto(ShortStayListing l) => new(
        l.Id, l.Title, l.City, l.OwnerId, l.AccommodationType?.NameAr ?? string.Empty, l.IsPublished,
        l.IsTakenDown, l.TakenDownAt, l.TakenDownReason, l.CreatedAt);
}
