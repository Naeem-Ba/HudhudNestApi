using FluentValidation;

namespace PropertyApi.Application.SocialDistribution.Commands.CreateSocialPublication;

public sealed class CreateSocialPublicationCommandValidator : AbstractValidator<CreateSocialPublicationCommand>
{
    public CreateSocialPublicationCommandValidator()
    {
        RuleFor(x => x.PropertyId).NotEmpty();
        RuleFor(x => x.SocialAccountId).NotEmpty();
        // Deliberately NOT NotEmpty(): Guid.Empty is this codebase's established "system, not a
        // person" marker for a run with no acting admin (see DistributionEngine.RunAsync's own
        // remarks) — a system-triggered/reconciliation-triggered distribution run legitimately
        // passes it here. Phase 1 audit finding: this rule used to reject exactly that, so
        // DistributionEngine.RunAsync threw ValidationException and created zero publications
        // for EVERY reconciliation-triggered run, and for any admin-published property whose
        // PropertyPublishedEvent carried no acting user.
        RuleFor(x => x.Title).MaximumLength(300).When(x => x.Title is not null);
        RuleFor(x => x.Body).MaximumLength(10_000).When(x => x.Body is not null);
        RuleFor(x => x.ImageUrl).MaximumLength(2000).When(x => x.ImageUrl is not null);
        RuleFor(x => x.Language).NotEmpty().MaximumLength(5);
    }
}
