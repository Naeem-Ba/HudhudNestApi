using MediatR;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Reviews.DTOs;
using HudhudNestApi.Application.Reviews.Interfaces;
using HudhudNestApi.Application.Reviews.Mapping;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Reviews.Entities;

namespace HudhudNestApi.Application.Reviews.Commands.RateAgency;

/// <summary>
/// Rates an agency as a business, independently of the rating of the member who published the
/// listing (<see cref="UserRating"/>). Rules, all enforced here and not in the UI:
/// the agency must exist and be active; neither its owner nor any of its members can rate it
/// (that would be rating yourself or your employer); the rater must have really dealt with it
/// (completed visit or a message thread about one of its listings); one rating per rater and
/// agency, edited in place on a repeat call.
/// </summary>
public sealed class RateAgencyCommandHandler : IRequestHandler<RateAgencyCommand, AgencyRatingDto>
{
    private readonly IAgencyRatingRepository _ratings;
    private readonly IAgencyRepository _agencies;
    private readonly IUnitOfWork _uow;

    public RateAgencyCommandHandler(
        IAgencyRatingRepository ratings,
        IAgencyRepository agencies,
        IUnitOfWork uow)
    {
        _ratings = ratings;
        _agencies = agencies;
        _uow = uow;
    }

    public async Task<AgencyRatingDto> Handle(RateAgencyCommand request, CancellationToken ct)
    {
        var agency = await _agencies.GetByIdAsync(request.AgencyId, ct);
        if (agency is not { IsDeleted: false, IsActive: true })
            throw new NotFoundException("المكتب غير موجود.");

        var rater = await _agencies.GetUserAccountAsync(request.RaterId, ct)
            ?? throw new NotFoundException("المستخدم غير موجود.");

        if (agency.OwnerUserId == request.RaterId || rater.AgencyId == agency.Id)
            throw new DomainException("لا يمكنك تقييم المكتب الذي تنتمي إليه.");

        if (!await _ratings.HasInteractionWithAgencyAsync(request.RaterId, agency.Id, ct))
            throw new DomainException(
                "يجب إتمام زيارة لأحد عقارات هذا المكتب أو التواصل معه عبر الرسائل قبل أن تتمكّن من تقييمه.");

        var existing = await _ratings.GetByRaterAndAgencyAsync(agency.Id, request.RaterId, ct);
        AgencyRating rating;
        if (existing is null)
        {
            rating = AgencyRating.Create(
                agency.Id, request.RaterId,
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

        await _uow.SaveChangesAsync(ct);
        return AgencyRatingMapper.ToDto(rating);
    }
}
