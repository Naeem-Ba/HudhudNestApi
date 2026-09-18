using FluentValidation;
using PropertyApi.Application.AppUpdates.Commands.CreateAppRelease;
using PropertyApi.Domain.AppUpdates.ValueObjects;

namespace PropertyApi.Application.AppUpdates.Commands.UpdateAppRelease;

public sealed class UpdateAppReleaseCommandValidator : AbstractValidator<UpdateAppReleaseCommand>
{
    public UpdateAppReleaseCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();

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
            .WithName(nameof(UpdateAppReleaseCommand.MinimumSupportedVersion));

        RuleFor(x => x.StoreUrl)
            .Must(CreateAppReleaseCommandValidator.BeAValidAbsoluteHttpUrl)
            .WithMessage("رابط المتجر يجب أن يكون رابطًا صحيحًا من نوع http أو https.")
            .When(x => !string.IsNullOrWhiteSpace(x.StoreUrl));

        RuleFor(x => x.ReleaseNotesAr).MaximumLength(4000);
        RuleFor(x => x.ReleaseNotesEn).MaximumLength(4000);
        RuleFor(x => x.ReleaseNotesDe).MaximumLength(4000);

        RuleFor(x => x.ReleaseDate).NotEqual(default(DateTime));
    }

    private static bool HaveMinimumNotAboveVersion(UpdateAppReleaseCommand command) =>
        !AppVersion.TryParse(command.Version, out var version) ||
        !AppVersion.TryParse(command.MinimumSupportedVersion, out var minimum) ||
        minimum <= version;
}
