using System.Text.Json.Serialization;
using FluentValidation;
using MediatR;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Search.Commands.CreateSavedSearch;
using HudhudNestApi.Application.Search.Interfaces;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Search.Enums;

namespace HudhudNestApi.Application.Search.Commands.UpdateSavedSearch;

/// <summary>Replaces the criteria, name and frequency of one of the caller's saved searches (growth engine P3).</summary>
public sealed record UpdateSavedSearchCommand(
    string? Name,
    string? CountryCode,
    string? City,
    string? Region,
    int? GovernorateId,
    int? DistrictId,
    int? NeighborhoodId,
    int? PropertyTypeId,
    ListingType? ListingType,
    decimal? MinPrice,
    decimal? MaxPrice,
    string? CurrencyCode,
    int? MinRooms,
    int? MaxRooms,
    decimal? MinArea,
    decimal? MaxArea,
    bool? HasBalcony = null,
    bool? HasElevator = null,
    bool? HasParkingSpace = null,
    SavedSearchFrequency? Frequency = null) : IRequest<bool>, ISavedSearchCriteria
{
    [JsonIgnore] public Guid Id { get; init; }
    [JsonIgnore] public Guid UserId { get; init; }
}

public sealed class UpdateSavedSearchCommandValidator : SavedSearchCriteriaValidator<UpdateSavedSearchCommand>
{
    public UpdateSavedSearchCommandValidator(ILocationHierarchyChecker locations, ICommonLookupService lookups)
        : base(locations, lookups)
    {
    }
}

public sealed class UpdateSavedSearchCommandHandler : IRequestHandler<UpdateSavedSearchCommand, bool>
{
    private readonly ISavedSearchRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _time;

    public UpdateSavedSearchCommandHandler(ISavedSearchRepository repo, IUnitOfWork uow, TimeProvider time)
    {
        _repo = repo;
        _uow = uow;
        _time = time;
    }

    public async Task<bool> Handle(UpdateSavedSearchCommand request, CancellationToken cancellationToken)
    {
        var search = await _repo.GetAsync(request.Id, cancellationToken);
        // Another user's search answers exactly like a missing one, so ids cannot be probed.
        if (search is null || search.UserId != request.UserId)
            return false;

        search.Update(
            request.Name, request.CountryCode, request.City, request.Region,
            request.GovernorateId, request.DistrictId, request.NeighborhoodId, request.PropertyTypeId,
            request.ListingType, request.MinPrice, request.MaxPrice, request.CurrencyCode,
            request.MinRooms, request.MaxRooms, request.MinArea, request.MaxArea,
            request.HasBalcony, request.HasElevator, request.HasParkingSpace,
            request.Frequency ?? search.Frequency,
            _time.GetUtcNow().UtcDateTime);

        await _uow.SaveChangesAsync(cancellationToken);
        return true;
    }
}

/// <summary>Pause or resume one of the caller's saved searches.</summary>
public sealed record SetSavedSearchPausedCommand(Guid Id, Guid UserId, bool Paused) : IRequest<bool>;

public sealed class SetSavedSearchPausedCommandValidator : AbstractValidator<SetSavedSearchPausedCommand>
{
    public SetSavedSearchPausedCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
    }
}

public sealed class SetSavedSearchPausedCommandHandler : IRequestHandler<SetSavedSearchPausedCommand, bool>
{
    private readonly ISavedSearchRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _time;

    public SetSavedSearchPausedCommandHandler(ISavedSearchRepository repo, IUnitOfWork uow, TimeProvider time)
    {
        _repo = repo;
        _uow = uow;
        _time = time;
    }

    public async Task<bool> Handle(SetSavedSearchPausedCommand request, CancellationToken cancellationToken)
    {
        var search = await _repo.GetAsync(request.Id, cancellationToken);
        if (search is null || search.UserId != request.UserId)
            return false;

        var now = _time.GetUtcNow().UtcDateTime;
        if (request.Paused)
            search.Pause(now);
        else
            search.Resume(now);

        await _uow.SaveChangesAsync(cancellationToken);
        return true;
    }
}
