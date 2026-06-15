using FluentValidation;

namespace PropertyApi.Application.Bookings.Commands.RequestVisit;

public sealed class RequestVisitCommandValidator : AbstractValidator<RequestVisitCommand>
{
    public RequestVisitCommandValidator()
    {
        RuleFor(x => x.PropertyId).NotEmpty();
        RuleFor(x => x.RequesterId).NotEmpty();

        RuleFor(x => x.ProposedAt)
            .GreaterThan(DateTime.UtcNow.AddHours(2))
            .WithMessage("يجب أن يكون الحجز قبل موعد الزيارة بساعتين على الأقل.")
            .LessThan(DateTime.UtcNow.AddDays(90))
            .WithMessage("لا يمكن الحجز لأكثر من 90 يومًا مقدمًا.");

        RuleFor(x => x.VisitorName)
            .NotEmpty().WithMessage("الاسم مطلوب.")
            .MaximumLength(150);

        RuleFor(x => x.VisitorPhone)
            .NotEmpty().WithMessage("رقم الهاتف مطلوب.")
            .Matches(@"^\\+?[0-9\\s\\-]{7,20}$")
            .WithMessage("صيغة رقم الهاتف غير صحيحة.");

        RuleFor(x => x.VisitorNote)
            .MaximumLength(500)
            .When(x => x.VisitorNote is not null);
    }
}
