using MediatR;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Agencies.Ratings;
using HudhudNestApi.Application.Agencies.Ratings.DTOs;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Agencies.Entities;

namespace HudhudNestApi.Application.Agencies.Ratings.Commands.RateAgency;

public sealed class RateAgencyCommandHandler : IRequestHandler<RateAgencyCommand, RateAgencyResult>
{
    private readonly IAgencyRepository _agencies;
    private readonly IAgencyRatingRepository _ratings;
    private readonly IUserAccountRepository _accounts;
    private readonly IUnitOfWork _uow;

    public RateAgencyCommandHandler(
        IAgencyRepository agencies,
        IAgencyRatingRepository ratings,
        IUserAccountRepository accounts,
        IUnitOfWork uow)
    {
        _agencies = agencies;
        _ratings = ratings;
        _accounts = accounts;
        _uow = uow;
    }

    public async Task<RateAgencyResult> Handle(RateAgencyCommand request, CancellationToken ct)
    {
        // A deactivated office has no public page, so it has nothing to rate (same 404 as GetAgencyBySlug).
        var agency = await _agencies.GetByIdAsync(request.AgencyId, ct);
        if (agency is null || !agency.IsActive)
            throw new NotFoundException("المكتب العقاري غير موجود.");

        var rater = await _accounts.GetByIdAsync(request.RaterId, ct)
            ?? throw new NotFoundException("الحساب غير موجود.");

        if (AgencyRatingPolicy.IsMemberOrOwner(agency, rater))
            throw new ForbiddenException("لا يمكن لمالك المكتب أو أعضائه تقييم مكتبهم.");

        if (!await _ratings.HasInteractionAsync(agency.Id, rater.Id, ct))
            throw new ForbiddenException(
                "يجب إتمام زيارة لأحد عقارات هذا المكتب أو التواصل معه عبر الرسائل قبل أن تتمكّن من تقييمه.");

        var existing = await _ratings.GetByRaterAndAgencyAsync(agency.Id, rater.Id, ct);
        var created = existing is null;
        AgencyRating rating;

        if (existing is null)
        {
            rating = AgencyRating.Create(
                agency.Id, rater.Id,
                request.Service, request.Professionalism, request.Reliability, request.OverallExperience,
                request.Comment);
            await _ratings.AddAsync(rating, ct);
        }
        else
        {
            existing.Update(
                request.Service, request.Professionalism, request.Reliability, request.OverallExperience,
                request.Comment, DateTime.UtcNow);
            rating = existing;
        }

        // Two first-time submissions racing past the lookup above hit the (agency, rater) unique index;
        // UnitOfWork turns that into a 409 instead of a 500.
        await _uow.SaveChangesAsync(ct);

        return new RateAgencyResult(AgencyRatingMapper.ToDto(rating, rater), created);
    }
}
