using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Common.DTOs;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Lookups.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Lookups;

public sealed class LocationSuggestionService : ILocationSuggestionService
{
    private readonly AppDbContext _db;
    private readonly ILogger<LocationSuggestionService> _logger;

    public LocationSuggestionService(AppDbContext db, ILogger<LocationSuggestionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task SubmitDistrictSuggestionAsync(
        int governorateId,
        string name,
        Guid submittedByUserId,
        Guid? propertyId,
        CancellationToken ct = default)
        => SubmitAsync(LocationSuggestionType.District, governorateId, name, submittedByUserId, propertyId, ct);

    public Task SubmitNeighborhoodSuggestionAsync(
        int districtId,
        string name,
        Guid submittedByUserId,
        Guid? propertyId,
        CancellationToken ct = default)
        => SubmitAsync(LocationSuggestionType.Neighborhood, districtId, name, submittedByUserId, propertyId, ct);

    private async Task SubmitAsync(
        LocationSuggestionType type,
        int parentId,
        string name,
        Guid submittedByUserId,
        Guid? propertyId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name) || parentId <= 0)
            return;

        var normalized = name.Trim();

        // Dedup: don't spam the review queue every time the same manual name
        // gets typed on a new listing. Case-insensitive on purpose — Arabic
        // has no case, but this also covers accidental Latin-script entries.
        var alreadyPending = await _db.Set<LocationSuggestion>()
            .AnyAsync(s =>
                s.Type == type &&
                s.ParentId == parentId &&
                s.Status == LocationSuggestionStatus.Pending &&
                s.Name.ToLower() == normalized.ToLower(),
                ct);

        if (alreadyPending)
        {
            _logger.LogInformation(
                "Skipped duplicate {Type} suggestion {Name} under parent {ParentId} — already pending.",
                type, normalized, parentId);
            return;
        }

        var suggestion = LocationSuggestion.Create(type, parentId, normalized, submittedByUserId, propertyId);
        _db.Set<LocationSuggestion>().Add(suggestion);

        // Deliberately its own SaveChanges — this must never fail the actual
        // property create/update it's attached to. Callers should treat this
        // as best-effort (see the try/catch in CreatePropertyCommandHandler).
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<LocationSuggestionDto>> GetPendingAsync(CancellationToken ct = default)
    {
        var pending = await _db.Set<LocationSuggestion>()
            .AsNoTracking()
            .Where(s => s.Status == LocationSuggestionStatus.Pending)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(ct);

        if (pending.Count == 0)
            return Array.Empty<LocationSuggestionDto>();

        var governorateIds = pending.Where(s => s.Type == LocationSuggestionType.District).Select(s => s.ParentId).Distinct().ToList();
        var districtIds = pending.Where(s => s.Type == LocationSuggestionType.Neighborhood).Select(s => s.ParentId).Distinct().ToList();
        var userIds = pending.Select(s => s.SubmittedByUserId).Distinct().ToList();
        var propertyIds = pending.Where(s => s.PropertyId.HasValue).Select(s => s.PropertyId!.Value).Distinct().ToList();

        var governorateNames = await _db.Governorates
            .Where(g => governorateIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.NameAr, ct);

        var districtNames = await _db.Districts
            .Where(d => districtIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.NameAr, ct);

        var userNames = await _db.UserAccounts
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);

        var propertyTitles = await _db.Properties
            .Where(p => propertyIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Title, ct);

        return pending.Select(s => new LocationSuggestionDto(
            s.Id,
            s.Type.ToString(),
            s.ParentId,
            s.Type == LocationSuggestionType.District
                ? governorateNames.GetValueOrDefault(s.ParentId, "—")
                : districtNames.GetValueOrDefault(s.ParentId, "—"),
            s.Name,
            s.SubmittedByUserId,
            userNames.GetValueOrDefault(s.SubmittedByUserId),
            s.PropertyId,
            s.PropertyId.HasValue ? propertyTitles.GetValueOrDefault(s.PropertyId.Value) : null,
            s.CreatedAt
        )).ToList();
    }

    public async Task<int> ApproveAsync(Guid suggestionId, Guid reviewedByUserId, string? notes, CancellationToken ct = default)
    {
        var suggestion = await _db.Set<LocationSuggestion>().FirstOrDefaultAsync(s => s.Id == suggestionId, ct)
            ?? throw new DomainException("Suggestion not found.");

        int resultingId;

        if (suggestion.Type == LocationSuggestionType.District)
        {
            var nextSortOrder = await _db.Districts
                .Where(d => d.GovernorateId == suggestion.ParentId)
                .Select(d => (int?)d.SortOrder)
                .MaxAsync(ct) ?? 0;

            var district = District.Create(
                governorateId: suggestion.ParentId,
                nameAr: suggestion.Name,
                nameEn: suggestion.Name,
                sortOrder: nextSortOrder + 1);

            _db.Districts.Add(district);
            await _db.SaveChangesAsync(ct);
            resultingId = district.Id;

            // Backfill: any property in this governorate that typed this exact
            // district name manually now points at the real row instead.
            var matchingProperties = await _db.Properties
                .Where(p => p.GovernorateId == suggestion.ParentId
                    && p.DistrictId == null
                    && p.DistrictText != null
                    && p.DistrictText.ToLower() == suggestion.Name.ToLower())
                .ToListAsync(ct);

            foreach (var property in matchingProperties)
            {
                property.DistrictId = resultingId;
                property.DistrictText = null;
            }
        }
        else
        {
            var nextSortOrder = await _db.Neighborhoods
                .Where(n => n.DistrictId == suggestion.ParentId)
                .Select(n => (int?)n.SortOrder)
                .MaxAsync(ct) ?? 0;

            var neighborhood = Neighborhood.Create(
                districtId: suggestion.ParentId,
                nameAr: suggestion.Name,
                nameEn: suggestion.Name,
                sortOrder: nextSortOrder + 1);

            _db.Neighborhoods.Add(neighborhood);
            await _db.SaveChangesAsync(ct);
            resultingId = neighborhood.Id;

            var matchingProperties = await _db.Properties
                .Where(p => p.DistrictId == suggestion.ParentId
                    && p.NeighborhoodId == null
                    && p.NeighborhoodText != null
                    && p.NeighborhoodText.ToLower() == suggestion.Name.ToLower())
                .ToListAsync(ct);

            foreach (var property in matchingProperties)
            {
                property.NeighborhoodId = resultingId;
                property.NeighborhoodText = null;
            }
        }

        suggestion.Approve(reviewedByUserId, resultingId, notes);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Approved {Type} suggestion {SuggestionId} ({Name}) → new entity {ResultingId}.",
            suggestion.Type, suggestionId, suggestion.Name, resultingId);

        return resultingId;
    }

    public async Task RejectAsync(Guid suggestionId, Guid reviewedByUserId, string? notes, CancellationToken ct = default)
    {
        var suggestion = await _db.Set<LocationSuggestion>().FirstOrDefaultAsync(s => s.Id == suggestionId, ct)
            ?? throw new DomainException("Suggestion not found.");

        suggestion.Reject(reviewedByUserId, notes);
        await _db.SaveChangesAsync(ct);
    }
}
