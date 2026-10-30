using FluentValidation.TestHelper;
using HudhudNestApi.Application.Bookings.Commands.ConfirmVisit;
using HudhudNestApi.Application.Bookings.Commands.DeclineVisit;
using HudhudNestApi.Application.Bookings.Commands.ProposeAlternateVisit;
using HudhudNestApi.Application.Contact.Commands.SubmitContact;
using HudhudNestApi.Application.Marketing.Commands.SubmitLead;
using HudhudNestApi.Application.Marketing.Commands.SubmitSurvey;
using HudhudNestApi.Application.Reviews.Commands.AddReview;
using HudhudNestApi.Domain.Marketing.Enums;

namespace HudhudNestApi.Application.Tests.Interactions;

/// <summary>
/// QA Phase 12: owner notes on a visit had no length limit (varchar(500), so 500 from the database), the contact
/// subject accepted line breaks, the lead phone accepted any text of six characters, and the survey stored enum
/// values that do not exist.
/// </summary>
public sealed class InteractionPhase12ValidatorTests
{
    private static readonly string TooLong = new('ن', 501);

    [Fact]
    public void ConfirmVisit_NoteOver500_IsRejected()
    {
        var validator = new ConfirmVisitCommandValidator();
        validator.TestValidate(new ConfirmVisitCommand(Guid.NewGuid(), Guid.NewGuid(), TooLong)).ShouldHaveValidationErrorFor(x => x.OwnerNote);
        validator.TestValidate(new ConfirmVisitCommand(Guid.NewGuid(), Guid.NewGuid(), new string('ن', 500))).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new ConfirmVisitCommand(Guid.NewGuid(), Guid.NewGuid(), null)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void DeclineVisit_ReasonOver500_OrNul_IsRejected()
    {
        var validator = new DeclineVisitCommandValidator();
        validator.TestValidate(new DeclineVisitCommand(Guid.NewGuid(), Guid.NewGuid(), TooLong)).ShouldHaveValidationErrorFor(x => x.Reason);
        validator.TestValidate(new DeclineVisitCommand(Guid.NewGuid(), Guid.NewGuid(), "a\0b")).ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void ProposeAlternate_NoteOver500_IsRejected()
        => new ProposeAlternateVisitCommandValidator()
            .TestValidate(new ProposeAlternateVisitCommand(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(3), TooLong))
            .ShouldHaveValidationErrorFor(x => x.OwnerNote);

    [Theory]
    [InlineData("طلب بخصوص إعلان", true)]
    [InlineData(null, true)]
    [InlineData("line one\r\nBcc: attacker@example.com", false)]
    [InlineData("a\nb", false)]
    public void ContactSubject_MustBeASingleLine(string? subject, bool valid)
    {
        var result = new SubmitContactCommandValidator()
            .TestValidate(new SubmitContactCommand("اسم", "a@b.co", subject, "رسالة طويلة كفاية", null));
        Assert.Equal(valid, result.IsValid);
    }

    [Theory]
    [InlineData("+963 944 123 456", true)]
    [InlineData("00963944123456", true)]
    [InlineData("(0151) 123-4567", true)]
    [InlineData("0944123456", true)]
    [InlineData("abcdefgh", false)]
    [InlineData("123456", false)]
    [InlineData("1234567890123456", false)]
    [InlineData("12ab34567", false)]
    public void LeadPhone_FollowsTheLandingFormRule(string phone, bool valid)
    {
        var result = new SubmitLeadCommandValidator()
            .TestValidate(new SubmitLeadCommand("اسم كامل", phone, "دمشق", "renter", "landing", null, null, null, null));
        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public void Survey_UnknownEnumValues_AreRejected_AndAnEmptyAnswerSetStaysValid()
    {
        var validator = new SubmitSurveyCommandValidator();

        SubmitSurveyCommand Survey(PaymentWillingness? w = null, PreferredPaymentModel? m = null) => new(
            null, w, m, null, null, null, null, null, null, null, null, null, null, null, "landing");

        Assert.True(validator.TestValidate(Survey()).IsValid);
        validator.TestValidate(Survey(w: (PaymentWillingness)99)).ShouldHaveValidationErrorFor(x => x.WillingnessToPay);
        validator.TestValidate(Survey(m: (PreferredPaymentModel)99)).ShouldHaveValidationErrorFor(x => x.PreferredPaymentModel);
    }

    [Fact]
    public void ReviewRatingMessage_IsReadableArabic_NotMojibake()
    {
        var result = new AddReviewCommandValidator()
            .TestValidate(new AddReviewCommand(Guid.NewGuid(), Guid.NewGuid(), 0, null));
        var message = result.Errors.Single(e => e.PropertyName == "Rating").ErrorMessage;
        Assert.Equal("التقييم يجب أن يكون بين 1 و5 نجوم.", message);
    }
}
