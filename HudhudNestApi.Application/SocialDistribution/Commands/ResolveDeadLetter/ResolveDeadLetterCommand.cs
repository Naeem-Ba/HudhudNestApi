using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;

namespace HudhudNestApi.Application.SocialDistribution.Commands.ResolveDeadLetter;

/// <summary>
/// Admin acknowledges a dead-lettered publication failure (spec §13: "يجب أن يستطيع المسؤول عرض
/// سبب الفشل" + "إعادة الإرسال بعد إصلاح السبب"). When <paramref name="Requeue"/> is true, this
/// ALSO calls the existing <c>SocialPublication.RetryManually</c> path (via
/// <c>RetrySocialPublicationCommand</c>) — resolving and requeuing are otherwise two independent,
/// separately-auditable actions (spec: "منع إعادة النشر المكرر عند Requeue" — RetryManually
/// already refuses once MaxRetryCount is exhausted, so a resolve-without-fixing-the-cause cannot
/// loop forever either).
/// </summary>
public sealed record ResolveDeadLetterCommand(
    Guid DeadLetterId,
    Guid ResolvedByUserId,
    string? Note,
    bool Requeue) : IRequest<SocialPublicationDeadLetterDto>;
