using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.SocialDistribution.Commands.RetrySocialPublication;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Application.SocialDistribution.Mapping;

namespace PropertyApi.Application.SocialDistribution.Commands.ResolveDeadLetter;

public sealed class ResolveDeadLetterCommandHandler : IRequestHandler<ResolveDeadLetterCommand, SocialPublicationDeadLetterDto>
{
    private readonly ISocialPublicationDeadLetterRepository _deadLetters;
    private readonly IUnitOfWork _uow;
    private readonly ISender _sender;

    public ResolveDeadLetterCommandHandler(ISocialPublicationDeadLetterRepository deadLetters, IUnitOfWork uow, ISender sender)
    {
        _deadLetters = deadLetters;
        _uow = uow;
        _sender = sender;
    }

    public async Task<SocialPublicationDeadLetterDto> Handle(ResolveDeadLetterCommand request, CancellationToken ct)
    {
        var deadLetter = await _deadLetters.GetByIdAsync(request.DeadLetterId, ct)
            ?? throw new NotFoundException("سجل الفشل النهائي (Dead Letter) غير موجود.");

        deadLetter.Resolve(request.ResolvedByUserId, request.Note, DateTime.UtcNow);
        _deadLetters.Update(deadLetter);
        await _uow.SaveChangesAsync(ct);

        if (request.Requeue)
        {
            // Reuses the existing Failed -> Queued path from Phase 3 (RetryManually) rather than
            // any bespoke dead-letter-specific transition — one state machine, one place that
            // enforces MaxRetryCount, regardless of which admin action triggered the retry.
            await _sender.Send(new RetrySocialPublicationCommand(deadLetter.PublicationId, request.ResolvedByUserId), ct);
        }

        return SocialDistributionMapper.ToDto(deadLetter);
    }
}
