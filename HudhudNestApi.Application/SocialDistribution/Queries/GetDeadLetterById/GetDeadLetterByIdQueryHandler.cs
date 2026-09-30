using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Mapping;

namespace HudhudNestApi.Application.SocialDistribution.Queries.GetDeadLetterById;

public sealed class GetDeadLetterByIdQueryHandler : IRequestHandler<GetDeadLetterByIdQuery, SocialPublicationDeadLetterDto>
{
    private readonly ISocialPublicationDeadLetterRepository _deadLetters;

    public GetDeadLetterByIdQueryHandler(ISocialPublicationDeadLetterRepository deadLetters) => _deadLetters = deadLetters;

    public async Task<SocialPublicationDeadLetterDto> Handle(GetDeadLetterByIdQuery request, CancellationToken ct)
    {
        var deadLetter = await _deadLetters.GetByIdAsync(request.DeadLetterId, ct)
            ?? throw new NotFoundException("سجل الفشل النهائي (Dead Letter) غير موجود.");

        return SocialDistributionMapper.ToDto(deadLetter);
    }
}
