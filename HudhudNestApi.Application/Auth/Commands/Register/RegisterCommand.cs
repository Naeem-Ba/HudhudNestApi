using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Auth.Models;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Common.Security;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Users.Constants;
using HudhudNestApi.Domain.Users.Entities;
using System.Text.RegularExpressions;

namespace HudhudNestApi.Application.Auth.Commands.Register;

public sealed record RegisterCommand(
    string FirstName,
    string LastName,
    string Email,
    string Password,

    // Both acceptances are required by RegisterCommandValidator (QA Phase 5, P5-D04): the
    // checkbox used to be enforced only in the Angular form, so a direct API call could create
    // an account with no ConsentRecord at all. The parameters keep their defaults so the
    // command can still be constructed without them; the validator is what rejects that.
    bool PrivacyPolicyAccepted = false,
    string? PrivacyPolicyVersion = null,
    string? ConsentSource = null,
    // Terms of Service acceptance, recorded next to the privacy consent so each document is
    // provable on its own.
    bool TermsAccepted = false,
    string? TermsVersion = null,
    // The site language the client was showing ("ar", "en", "de"). Only picks the language
    // of the confirmation mail; an older client omits it and gets the bilingual message.
    string? Language = null)
    : IRequest<RegisterResult>;

public sealed record RegisterResult
{
    public bool Success { get; init; }

    public bool Conflict { get; init; }

    public string Message { get; init; }
        = string.Empty;

    public Guid? UserId { get; init; }

    /// <summary>
    /// False when the account was created but the confirmation mail could not be sent.
    /// Registration still succeeds -- login does not wait on confirmation -- so this is how
    /// the client learns that "we sent you a link" would be untrue and offers a resend.
    /// </summary>
    public bool ConfirmationEmailSent { get; init; }

    public IReadOnlyList<string> Errors { get; init; }
        = Array.Empty<string>();

    public static RegisterResult Ok(
        Guid userId,
        bool confirmationEmailSent)
        => new()
        {
            Success = true,
            UserId = userId,
            ConfirmationEmailSent = confirmationEmailSent,
            Message = "Registration successful."
        };

    public static RegisterResult EmailConflict()
        => new()
        {
            Success = false,
            Conflict = true,
            Message = "Email already registered."
        };

    /// <summary>
    /// Stable code for the conflict above. The message is English prose a localised client
    /// cannot translate; the code is the contract (docs/password-policy.md).
    /// </summary>
    public const string EmailConflictCode = "EMAIL_ALREADY_REGISTERED";

    public static RegisterResult Fail(
        IEnumerable<string> errors)
        => new()
        {
            Success = false,
            Errors = errors.ToArray()
        };
}

public sealed class RegisterCommandHandler
    : IRequestHandler<
        RegisterCommand,
        RegisterResult>
{
    private readonly IRegisterIdentityService _identity;

    private readonly IUserAccountRepository _accounts;

    private readonly IUnitOfWork _unitOfWork;

    private readonly IEmailVerificationService
        _emailVerification;

    private readonly IConsentRecordRepository
        _consents;

    private readonly ILogger<RegisterCommandHandler>
        _logger;

    public RegisterCommandHandler(
        IRegisterIdentityService identity,
        IUserAccountRepository accounts,
        IUnitOfWork unitOfWork,
        IEmailVerificationService emailVerification,
        IConsentRecordRepository consents,
        ILogger<RegisterCommandHandler> logger)
    {
        _identity = identity;
        _accounts = accounts;
        _unitOfWork = unitOfWork;
        _emailVerification = emailVerification;
        _consents = consents;
        _logger = logger;
    }

    public async Task<RegisterResult> Handle(
        RegisterCommand request,
        CancellationToken ct)
    {
        var email =
            NormalizeEmail(
                request.Email);

        /*
         * Fast conflict check.
         *
         * The database unique constraint remains the final authority
         * for concurrent requests.
         */
        var existingIdentity =
            await _identity.FindByEmailAsync(
                email,
                ct);

        if (existingIdentity is not null)
        {
            return RegisterResult.EmailConflict();
        }

        var now =
            DateTime.UtcNow;

        var userId =
            Guid.NewGuid();

        /*
         * Business profile aggregate.
         *
         * During the migration period:
         *
         * IdentityId == UserAccountId
         */
        var account =
            UserAccount.Create(
                userId,
                request.FirstName,
                request.LastName,
                now);

        account.ChooseInitialLanguage(
            request.Language,
            now);

        /*
         * Framework-neutral identity creation request.
         *
         * Legacy profile fields are temporary compatibility data.
         * They can be removed after the remaining Legacy User readers
         * are migrated.
         */
        var identityRequest =
            new CreateIdentityAccount(
                UserAccountId:
                    userId,

                Email:
                    email,

                PhoneNumber:
                    null,

                Password:
                    request.Password,

                LegacyFirstName:
                    request.FirstName.Trim(),

                LegacyLastName:
                    request.LastName.Trim(),

                LegacyCreatedAtUtc:
                    now);

        await _unitOfWork
            .BeginTransactionAsync(ct);

        try
        {
            /*
             * 1. Create Identity account.
             */
            var createResult =
                await _identity.CreateAsync(
                    identityRequest,
                    ct);

            if (!createResult.Succeeded)
            {
                await _unitOfWork
                    .RollbackTransactionAsync(ct);

                // Lost the race to a concurrent registration of the same address: the
                // unique index decided it. Same answer as the fast check above.
                if (createResult.Errors.Contains(
                        IdentityOperationResult.DuplicateEmailCode))
                {
                    return RegisterResult.EmailConflict();
                }

                _logger.LogWarning(
                    "Registration failed for email {Email}. Errors: {Errors}",
                    PiiMasking.MaskEmail(email),
                    string.Join(
                        ", ",
                        createResult.Errors));

                return RegisterResult.Fail(
                    createResult.Errors);
            }

            /*
             * 2. Assign the default role.
             */
            var roleResult =
                await _identity.AddToRoleAsync(
                    userId,
                    RoleNames.User,
                    ct);

            if (!roleResult.Succeeded)
            {
                await _unitOfWork
                    .RollbackTransactionAsync(ct);

                _logger.LogWarning(
                    "Adding default role failed for identity {IdentityId}. Errors: {Errors}",
                    userId,
                    string.Join(
                        ", ",
                        roleResult.Errors));

                return RegisterResult.Fail(
                    roleResult.Errors);
            }

            /*
             * 3. Track the business profile.
             */
            await _accounts.AddAsync(
                account,
                ct);

            /*
             * 3b. Record Privacy Policy consent, if the caller actually just checked
             * the (never pre-checked) consent checkbox and submitted the form -- see
             * the RegisterCommand doc comment. Committed atomically with the account
             * itself: a consent record must never exist for an account that failed to
             * commit, nor an account exist with no chance to have recorded consent
             * alongside it.
             */
            TryRecordConsent(
                userId,
                request,
                now);

            /*
             * UnitOfWork.CommitTransactionAsync does not implicitly
             * call SaveChangesAsync.
             */
            await _unitOfWork
                .SaveChangesAsync(ct);

            /*
             * 4. Commit Identity + role + UserAccount atomically.
             */
            await _unitOfWork
                .CommitTransactionAsync(ct);

            _logger.LogInformation(
                "Identity {IdentityId} and UserAccount registered successfully.",
                userId);

            /*
             * 5. Address confirmation.
             *
             * After the commit, never before it: the account is what the confirmation
             * refers to, and a mail server is not something to hold a database
             * transaction open across.
             */
            var confirmationEmailSent =
                await SendConfirmationEmailAsync(
                    userId,
                    email,
                    request.Language);

            return RegisterResult.Ok(
                userId,
                confirmationEmailSent);
        }
        catch (Exception ex)
        {
            await _unitOfWork
                .RollbackTransactionAsync(
                    CancellationToken.None);

            _logger.LogError(
                ex,
                "Atomic registration failed.");

            throw;
        }
    }

    /// <summary>
    /// Mints a confirmation token and mails the link, and lets no failure out. Returns
    /// whether the provider accepted the message, so the caller can say so truthfully.
    ///
    /// By the time this runs the registration is committed: the account exists, the
    /// user can sign in, and an unconfirmed address is simply unconfirmed -- login
    /// does not gate on it. Rethrowing would report a sign-up that succeeded as one
    /// that failed, and send the user back to register again against an email that
    /// is now taken, which is a worse outcome than a missing message.
    ///
    /// The request's CancellationToken is deliberately not threaded through. It is
    /// cancelled when the client goes away, and a user who closes the tab the instant
    /// after submitting has still registered and still needs the link. The same
    /// reasoning already governs the rollback path above.
    /// </summary>
    private async Task<bool> SendConfirmationEmailAsync(
        Guid identityId,
        string email,
        string? language)
    {
        try
        {
            var token =
                await _identity
                    .GenerateEmailConfirmationTokenAsync(
                        identityId,
                        CancellationToken.None);

            await _emailVerification
                .SendVerificationLinkAsync(
                    email,
                    token,
                    language,
                    CancellationToken.None);

            _logger.LogInformation(
                "Confirmation link sent for identity {IdentityId}.",
                identityId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Identity {IdentityId} registered, but the confirmation email could "
                + "not be sent. The account stays unconfirmed until it is resent.",
                identityId);

            return false;
        }
    }

    private static string NormalizeEmail(
        string email)
        => email
            .Trim()
            .ToLowerInvariant();

    /// <summary>
    /// Adds the ConsentRecords to the same unit of work as the rest of registration, so a
    /// consent record never exists for an account that failed to commit. The validator has
    /// already rejected a missing or false acceptance; the guard in AddConsentIfAccepted stays
    /// as defence in depth for callers that bypass the validation pipeline.
    /// </summary>
    private void TryRecordConsent(
        Guid userId,
        RegisterCommand request,
        DateTime nowUtc)
    {
        var source = Enum.TryParse<ConsentSource>(
            request.ConsentSource,
            ignoreCase: true,
            out var parsedSource)
                ? parsedSource
                : ConsentSource.Web;

        AddConsentIfAccepted(
            userId, ConsentPolicyType.PrivacyPolicy,
            request.PrivacyPolicyAccepted, request.PrivacyPolicyVersion, source, nowUtc);

        AddConsentIfAccepted(
            userId, ConsentPolicyType.TermsOfService,
            request.TermsAccepted, request.TermsVersion, source, nowUtc);
    }

    private void AddConsentIfAccepted(
        Guid userId,
        ConsentPolicyType policyType,
        bool accepted,
        string? version,
        ConsentSource source,
        DateTime nowUtc)
    {
        if (!accepted || string.IsNullOrWhiteSpace(version))
        {
            return;
        }

        _consents.Add(ConsentRecord.Create(userId, policyType, version, source, nowUtc));
    }
}

public sealed class RegisterCommandValidator
    : AbstractValidator<RegisterCommand>
{
    /// <summary>Stable error code for a missing or false consent, for localised clients.</summary>
    public const string ConsentRequiredCode = "CONSENT_REQUIRED";

    private const int MaxPolicyVersionLength = 64;

    private readonly IPasswordSecurityService _passwordSecurityService;

    public RegisterCommandValidator(
        IPasswordSecurityService passwordSecurityService)
    {
        _passwordSecurityService = passwordSecurityService;

        RuleFor(x => x.FirstName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.LastName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(320)
            .EmailAddress()
            .Must(
                email =>
                    email != null &&
                    !email.Any(
                        char.IsWhiteSpace))
            .WithMessage(
                "Email must not contain whitespace.");

        // Consent is enforced on the server, not only in the form (P5-D04): without these
        // rules POST /api/auth/register created accounts with no ConsentRecord whenever the
        // flags were false or absent.
        RuleFor(x => x.PrivacyPolicyAccepted)
            .Equal(true)
            .WithMessage("The privacy policy must be accepted.")
            .WithErrorCode(ConsentRequiredCode);

        RuleFor(x => x.PrivacyPolicyVersion)
            .NotEmpty()
            .WithMessage("The accepted privacy policy version is required.")
            .WithErrorCode(ConsentRequiredCode)
            .MaximumLength(MaxPolicyVersionLength);

        RuleFor(x => x.TermsAccepted)
            .Equal(true)
            .WithMessage("The terms of service must be accepted.")
            .WithErrorCode(ConsentRequiredCode);

        RuleFor(x => x.TermsVersion)
            .NotEmpty()
            .WithMessage("The accepted terms of service version is required.")
            .WithErrorCode(ConsentRequiredCode)
            .MaximumLength(MaxPolicyVersionLength);

        RuleFor(x => x.Password)
            .NotEmpty()
            .WithMessage("Password is required.")
            .MinimumLength(8)
            .WithMessage("Password must be at least 8 characters long.")
            .CustomAsync(ValidatePasswordSecurityAsync);
    }

    /// <summary>
    /// Async validation for password complexity and breach screening.
    ///
    /// BUG FIX: this used to `throw new ValidationException(...)` from inside a
    /// MustAsync rule. That type is FluentValidation's — not the application's
    /// own Common.Exceptions.ValidationException that ExceptionHandlingMiddleware
    /// knows about — so every weak or breached password escaped the validation
    /// pipeline and surfaced to the caller as HTTP 500 instead of 422, and the
    /// per-rule messages produced by IPasswordSecurityService never reached the
    /// client at all. A validator reports failures through the context; it does
    /// not throw.
    /// </summary>
    private async Task ValidatePasswordSecurityAsync(
        string password,
        ValidationContext<RegisterCommand> context,
        CancellationToken cancellationToken)
    {
        // NotEmpty/MinimumLength above already reported their own failures for
        // these; re-running the security check would only add noise.
        if (string.IsNullOrEmpty(password))
        {
            return;
        }

        var result = await _passwordSecurityService.ValidatePasswordAsync(
            password,
            cancellationToken);

        if (result.IsValid)
        {
            return;
        }

        // Surface every individual reason (length, character classes, breach)
        // rather than collapsing them into one opaque message.
        //
        // The stable code travels with the message because the code is what a client
        // can translate. Sending the English sentence on its own is how Arabic and
        // German users came to read "This password has been exposed in known data
        // breaches" verbatim: the Angular app had nothing else to key a lookup off,
        // so it printed the server's prose.
        foreach (var error in result.Errors)
        {
            context.AddFailure(new ValidationFailure(
                nameof(RegisterCommand.Password),
                error.Message)
            {
                ErrorCode = error.Code
            });
        }
    }
}
