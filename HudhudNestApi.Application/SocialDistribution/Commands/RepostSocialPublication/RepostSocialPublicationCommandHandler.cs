using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.SocialDistribution.Commands.CreateSocialPublication;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.SocialDistribution.Commands.RepostSocialPublication;

public sealed class RepostSocialPublicationCommandHandler : IRequestHandler<RepostSocialPublicationCommand, SocialPublicationDto>
{
    private readonly ISocialPublicationRepository _publications;
    private readonly ISender _sender;

    public RepostSocialPublicationCommandHandler(ISocialPublicationRepository publications, ISender sender)
    {
        _publications = publications;
        _sender = sender;
    }

    public async Task<SocialPublicationDto> Handle(RepostSocialPublicationCommand request, CancellationToken ct)
    {
        var original = await _publications.GetByIdAsync(request.OriginalPublicationId, ct)
            ?? throw new NotFoundException("منشور التوزيع الأصلي غير موجود.");

        if (original.Content is null)
            throw new ConflictException("لا يمكن إعادة نشر منشور بلا محتوى.");

        // Only an already-live post is meaningful to "repost" — reposting a Draft/Failed/
        // Cancelled publication would just be a confusingly-named duplicate of Create (spec
        // §"Repost": the feature re-shares something that already went out, it does not retry a
        // failed attempt — RetrySocialPublicationCommand already covers that case).
        if (original.Status != SocialPublicationStatus.Published)
            throw new ConflictException("لا يمكن إعادة نشر منشور لم يُنشر بنجاح بعد.");

        // Re-validated end-to-end by CreateSocialPublicationCommandHandler itself (property still
        // public, account still active, channel still active) — this handler does not duplicate
        // those checks, only supplies the repost-specific inputs.
        return await _sender.Send(
            new CreateSocialPublicationCommand(
                original.PropertyId,
                original.SocialAccountId,
                request.ActorUserId,
                Title: original.Content.Title,
                Body: original.Content.Body,
                ImageUrl: original.Content.ImageUrl,
                Hashtags: original.Content.HashtagList,
                Language: original.Content.Language,
                DistributionRuleId: null,
                DistributionRunId: null,
                IsPromotionalRepost: true),
            ct);
    }
}
