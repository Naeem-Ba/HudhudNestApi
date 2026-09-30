using MediatR;
using HudhudNestApi.Application.Listings.DTOs;
using HudhudNestApi.Domain.Enums;

namespace HudhudNestApi.Application.Listings.Queries.CheckPotentialDuplicateProperty;

/// <summary>
/// Phase-0, Task 4 — advisory (never blocking) potential-duplicate-listing check.
///
/// Why a Query and not a CreatePropertyCommandValidator rule? ValidationBehavior
/// (the MediatR pipeline behavior all commands go through) collects every
/// FluentValidation failure and, if there is at least one, unconditionally throws —
/// there is no "warning" severity tier. A duplicate-listing signal is inherently
/// probabilistic (same owner reposting vs. two different owners describing the same
/// unit vs. a genuine coincidence), so it must never hard-fail property creation.
/// The frontend calls this endpoint before final submit and shows a dismissible,
/// non-blocking confirmation if candidates come back.
///
/// NeighborhoodId is required for a meaningful check: without a structured location
/// there is no reliable way to compare listings (see PropertyFilterDto notes on why
/// free-text City/Region are not accurate enough for this).
/// </summary>
public sealed record CheckPotentialDuplicatePropertyQuery(
    Guid OwnerId,
    int? NeighborhoodId,
    decimal? Area,
    decimal? Price,
    ListingType ListingType
) : IRequest<DuplicateCheckResultDto>;
