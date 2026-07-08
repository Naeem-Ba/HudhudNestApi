using FluentValidation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PropertyApi.Application.Auth.Commands.VerifyPhoneOtp
{

    public sealed class VerifyPhoneOtpCommandValidator
        : AbstractValidator<VerifyPhoneOtpCommand>
    {
        public VerifyPhoneOtpCommandValidator()
        {
            RuleFor(x => x.PhoneNumber)
                .NotEmpty()
                .WithMessage(
                    "Phone number is required.")
                .Matches(
                    @"^\+[1-9]\d{7,14}$")
                .WithMessage(
                    "Phone number must use international E.164 format, " +
                    "for example +963911234567.");

            RuleFor(x => x.Code)
                .NotEmpty()
                .WithMessage(
                    "Verification code is required.")
                .Length(6)
                .WithMessage(
                    "Verification code must contain exactly six digits.")
                .Matches(
                    @"^\d{6}$")
                .WithMessage(
                    "Verification code must contain digits only.");

            RuleFor(x => x.FirstName)
                .MaximumLength(100)
                .When(
                    x =>
                        !string.IsNullOrWhiteSpace(
                            x.FirstName))
                .WithMessage(
                    "First name must not exceed 100 characters.");

            RuleFor(x => x.LastName)
                .MaximumLength(100)
                .When(
                    x =>
                        !string.IsNullOrWhiteSpace(
                            x.LastName))
                .WithMessage(
                    "Last name must not exceed 100 characters.");

            RuleFor(x => x.Purpose)
                .IsInEnum()
                .WithMessage(
                    "The OTP purpose is not supported.");
        }
    }
}
