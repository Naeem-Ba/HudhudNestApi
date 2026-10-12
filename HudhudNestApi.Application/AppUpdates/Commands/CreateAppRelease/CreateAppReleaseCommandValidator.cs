using FluentValidation;
using HudhudNestApi.Application.AppUpdates.Policies;
using HudhudNestApi.Domain.AppUpdates.ValueObjects;

namespace HudhudNestApi.Application.AppUpdates.Commands.CreateAppRelease;

public sealed class CreateAppReleaseCommandValidator : AbstractValidator<CreateAppReleaseCommand>
{
    public CreateAppReleaseCommandValidator()
    {
        RuleFor(x => x.Platform).IsInEnum();

        RuleFor(x => x.Version)
            .NotEmpty()
            .Must(v => AppVersion.TryParse(v, out _))
            .WithMessage("يجب أن يكون رقم الإصدار على الصيغة major.minor.patch، مثل 1.4.2.");

        RuleFor(x => x.MinimumSupportedVersion)
            .NotEmpty()
            .Must(v => AppVersion.TryParse(v, out _))
            .WithMessage("يجب أن يكون الحد الأدنى للإصدار المدعوم على الصيغة major.minor.patch، مثل 1.4.2.");

        RuleFor(x => x)
            .Must(HaveMinimumNotAboveVersion)
            .WithMessage("الحد الأدنى للإصدار المدعوم لا يمكن أن يكون أحدث من الإصدار نفسه.")
            .WithName(nameof(CreateAppReleaseCommand.MinimumSupportedVersion));

        RuleFor(x => x.StoreUrl)
            .Must(BeAValidAbsoluteHttpUrl)
            .WithMessage("رابط المتجر يجب أن يكون رابطًا صحيحًا من نوع https.")
            .When(x => !string.IsNullOrWhiteSpace(x.StoreUrl));

        // Official store host for the platform, and a link for any release created enabled. See AppStoreLinkPolicy.
        RuleFor(x => x.StoreUrl)
            .Must((command, url) => AppStoreLinkPolicy.IsAcceptableLink(command.Platform, url))
            .WithMessage(x => AppStoreLinkPolicy.InvalidLinkMessage(x.Platform))
            .When(x => !string.IsNullOrWhiteSpace(x.StoreUrl) && BeAValidAbsoluteHttpUrl(x.StoreUrl));

        RuleFor(x => x.StoreUrl)
            .NotEmpty()
            .WithMessage(AppStoreLinkPolicy.MissingLinkMessage)
            .When(x => x.IsEnabled && AppStoreLinkPolicy.RequiresLink(x.Platform));

        RuleFor(x => x.ReleaseNotesAr).MaximumLength(4000);
        RuleFor(x => x.ReleaseNotesEn).MaximumLength(4000);
        RuleFor(x => x.ReleaseNotesDe).MaximumLength(4000);

        RuleFor(x => x.ReleaseDate).NotEqual(default(DateTime));
    }

    // Short-circuits to valid when either field already failed its own format rule above, so
    // this cross-field rule never masks that with a confusing message of its own.
    private static bool HaveMinimumNotAboveVersion(CreateAppReleaseCommand command) =>
        !AppVersion.TryParse(command.Version, out var version) ||
        !AppVersion.TryParse(command.MinimumSupportedVersion, out var minimum) ||
        minimum <= version;

    internal static bool BeAValidAbsoluteHttpUrl(string? url) =>
        string.IsNullOrWhiteSpace(url) ||
        (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
         (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));
}
