using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Auth.Models;
using HudhudNestApi.Application.Auth.Phone;
using HudhudNestApi.Application.Auth.Services;
using HudhudNestApi.Application.Common.Observability;
using HudhudNestApi.Application.Common.Security;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Domain.Auth.Entities;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Users.Constants;
using HudhudNestApi.Domain.Users.Entities;
using HudhudNestApi.Infrastructure.Identity.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Domain.Audit.Constants;

namespace HudhudNestApi.Infrastructure.Auth.Services;

public sealed class PhoneAuthenticationWorkflow : IPhoneAuthenticationWorkflow
{
    private const string GenericSendMessage = "If the phone number is eligible, a verification code will be sent.";
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly IPhoneNumberNormalizer _normalizer;
    private readonly IOtpService _otp;
    private readonly IOtpChannelService _channels;
    private readonly ILogger<PhoneAuthenticationWorkflow> _logger;
    private readonly ITokenService _tokens;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IPhoneVerificationPolicy _policy;
    private readonly TimeProvider _clock;
    private readonly IAuditLogService _audit;
    private readonly string[] _allowedCountryCodes;

    public PhoneAuthenticationWorkflow(AppDbContext db, UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn, IPhoneNumberNormalizer normalizer, IOtpService otp,
        IOtpChannelService channels, ITokenService tokens, IRefreshTokenRepository refreshTokens,
        IPhoneVerificationPolicy policy, TimeProvider clock, IAuditLogService audit,
        IOptions<SmsProviderOptions> smsOptions, ILogger<PhoneAuthenticationWorkflow> logger)
    {
        _allowedCountryCodes = smsOptions.Value.AllowedCountryCodes
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray();
        _db = db; _users = users; _signIn = signIn; _normalizer = normalizer; _otp = otp;
        _channels = channels; _logger = logger; _tokens = tokens; _refreshTokens = refreshTokens; _policy = policy; _clock = clock; _audit = audit;
    }

    public async Task<PhoneWorkflowResult> SendOtpAsync(string phoneNumber, OtpPurpose purpose,
        Guid? userId, string? ipAddress, CancellationToken ct, OtpChannel channel = OtpChannel.Sms)
    {
        var normalized = _normalizer.Normalize(phoneNumber);
        if (!normalized.Succeeded) return Fail("PHONE_NUMBER_INVALID");
        var phone = normalized.Value!;

        // SMS pumping guard: a NEW number (registration / number change) may only target the configured
        // countries. The answer depends on the public prefix alone, so it reveals nothing about accounts,
        // and existing accounts (reset, re-verification) are never blocked by it.
        if (purpose is OtpPurpose.PhoneRegistration or OtpPurpose.PhoneNumberChange &&
            _allowedCountryCodes.Length > 0 &&
            !_allowedCountryCodes.Any(prefix => phone.StartsWith(prefix, StringComparison.Ordinal)))
            return Fail("PHONE_COUNTRY_NOT_SUPPORTED");

        // A channel that is off, or does not work for this country, is refused before anything else. The answer
        // comes from configuration and the public calling code alone, so it says nothing about any account.
        if (!_channels.IsAvailable(channel, phone)) return Fail("OTP_CHANNEL_UNAVAILABLE");

        var user = await _users.Users.SingleOrDefaultAsync(x => x.NormalizedPhoneNumber == phone, ct);
        var eligible = purpose switch
        {
            OtpPurpose.PhoneRegistration => user is null,
            OtpPurpose.PhonePasswordReset => user is not null,
            OtpPurpose.PhoneReverification => user is not null && user.Id == userId,
            OtpPurpose.PhoneNumberChange => user is null && userId.HasValue,
            _ => false
        };

        // Anti-enumeration (audit F-2): the response must not depend on whether the number is
        // eligible. An ineligible number gets a stored *decoy* challenge that behaves exactly like a
        // real one at verify time (wrong code, attempt limit, expiry) but can never be satisfied, and
        // no SMS is sent. Past three challenges per hour every number is answered with its most
        // recent challenge, so the limit does not depend on eligibility either.
        // The count-then-insert below is serialised per number and purpose with a transaction-scoped advisory
        // lock, so a parallel burst cannot each read "two so far" and all create a challenge (and send an SMS).
        // The lock is released when the transaction commits, before the message is sent.
        await using var limitTx = _db.Database.IsNpgsql() ? await _db.Database.BeginTransactionAsync(ct) : null;
        if (limitTx is not null)
        {
            var lockKey = $"otp-send:{(int)purpose}:{phone}";
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);
        }

        var cutoff = _clock.GetUtcNow().AddHours(-1);
        var recent = await _db.PhoneOtpChallenges
            .Where(x => x.NormalizedPhoneNumber == phone && x.Purpose == purpose && x.CreatedAtUtc >= cutoff)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => x.Id)
            .Take(3)
            .ToListAsync(ct);
        if (recent.Count >= 3)
        {
            ApplicationTelemetry.RecordOtpSend(channel.ToString(), "rate_limited");
            _logger.LogWarning("OTP send rate-limited on {Channel} for {Phone}.", channel, PiiMasking.MaskPhone(phone));
            return new(true, Message: GenericSendMessage, ChallengeId: recent[0], Channel: channel);
        }

        if (!eligible)
        {
            var decoy = PhoneOtpChallenge.Create(phone, UnsatisfiableHash(), purpose, _clock.GetUtcNow(), userId, channel);
            _db.PhoneOtpChallenges.Add(decoy);
            await _db.SaveChangesAsync(ct);
            if (limitTx is not null) await limitTx.CommitAsync(ct);
            return new(true, Message: GenericSendMessage, ChallengeId: decoy.Id, Channel: channel);
        }

        var (code, hash) = _otp.Generate();
        var challenge = PhoneOtpChallenge.Create(phone, hash, purpose, _clock.GetUtcNow(), userId, channel);
        _db.PhoneOtpChallenges.Add(challenge);
        await _db.SaveChangesAsync(ct);
        if (limitTx is not null) await limitTx.CommitAsync(ct);
        _logger.LogInformation("OTP challenge created for {Phone} on {Channel} ({Purpose}).",
            PiiMasking.MaskPhone(phone), channel, purpose);
        var sent = await _channels.SendAsync(channel, phone, code, ct);
        switch (sent.Outcome)
        {
            case OtpSendOutcome.Sent:
                if (sent.ProviderRequestId is not null)
                {
                    challenge.SetProviderRequestId(sent.ProviderRequestId);
                    await _db.SaveChangesAsync(ct);
                }
                break;

            case OtpSendOutcome.RecipientUnreachable:
                // A fact about the number (for example: not on Telegram). Saying so would tell anyone probing
                // this endpoint that the number is eligible, so the answer stays the generic one and the person
                // is offered another channel by the app. The stored code was never delivered and simply expires.
                return new(true, Message: GenericSendMessage, ChallengeId: challenge.Id, Channel: channel);

            default:
                // The provider itself could not be used. Nothing was delivered, so the challenge is discarded and
                // the failure is reported (SMS keeps its historical code).
                _db.PhoneOtpChallenges.Remove(challenge);
                await _db.SaveChangesAsync(ct);
                return Fail(sent.Outcome == OtpSendOutcome.ChannelUnavailable ? "OTP_CHANNEL_UNAVAILABLE"
                    : channel == OtpChannel.Sms ? "SMS_FAILED" : "OTP_PROVIDER_UNAVAILABLE");
        }
        await _audit.LogAsync(userId, purpose switch
        {
            OtpPurpose.PhoneRegistration => AuditActions.PhoneRegistrationOtpRequested,
            OtpPurpose.PhonePasswordReset => AuditActions.PhonePasswordResetOtpRequested,
            OtpPurpose.PhoneReverification => AuditActions.PhoneReverificationRequested,
            _ => AuditActions.PhoneNumberChangeRequested
        }, ipAddress, newValue: $"{{\"outcome\":\"accepted\",\"channel\":\"{channel}\"}}", ct: ct);
        return new(true, Message: GenericSendMessage, ChallengeId: challenge.Id, Channel: channel);
    }

    public async Task<PhoneWorkflowResult> RegisterAsync(Guid challengeId, string code, string password,
        string firstName, string lastName, string? ipAddress, CancellationToken ct, OtpChannel? channel = null)
    {
        var outcome = await ValidateAndReserveAsync(challengeId, code, OtpPurpose.PhoneRegistration, null, channel, ct);
        if (outcome.Challenge is not { } challenge) return Fail(OtpErrorCode(outcome.FailureReason, "OTP_INVALID"));
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            if (await _users.Users.AnyAsync(x => x.NormalizedPhoneNumber == challenge.NormalizedPhoneNumber, ct))
            {
                await transaction.RollbackAsync(ct);
                await ReleaseAsync(challenge.Id, ct);
                return Fail("PHONE_ALREADY_REGISTERED");
            }
            var now = _clock.GetUtcNow();
            var id = Guid.NewGuid();
            var user = NewVerifiedUser(id, challenge.NormalizedPhoneNumber, now);
            var created = await _users.CreateAsync(user, password);
            if (!created.Succeeded)
            {
                await transaction.RollbackAsync(ct);
                await ReleaseAsync(challenge.Id, ct);
                return IdentityFail(created, "USER_CREATE_FAILED");
            }
            var role = await _users.AddToRoleAsync(user, RoleNames.User);
            if (!role.Succeeded)
            {
                await transaction.RollbackAsync(ct);
                await ReleaseAsync(challenge.Id, ct);
                return IdentityFail(role, "ROLE_ASSIGNMENT_FAILED");
            }
            _db.UserAccounts.Add(UserAccount.Create(id, firstName.Trim(), lastName.Trim(), now.UtcDateTime));
            await ConsumeAsync(challenge.Id, ct);
            await _db.SaveChangesAsync(ct);
            var session = await IssueSessionAsync(user, ipAddress, ct);
            await transaction.CommitAsync(ct);
            await _audit.LogAsync(user.Id, AuditActions.PhoneRegistrationCompleted, ipAddress,
                newValue: "{\"outcome\":\"succeeded\"}", ct: ct);
            return session;
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            await ReleaseAsync(challenge.Id, ct);
            return Fail("PHONE_ALREADY_REGISTERED");
        }
    }

    public async Task<PhoneWorkflowResult> LoginAsync(string phoneNumber, string password,
        string? ipAddress, CancellationToken ct)
    {
        var normalized = _normalizer.Normalize(phoneNumber);
        var user = normalized.Succeeded
            ? await _users.Users.SingleOrDefaultAsync(x => x.NormalizedPhoneNumber == normalized.Value, ct)
            : null;
        if (user is null)
        {
            BurnOnePasswordVerification(password);
            await _audit.LogAsync(null, AuditActions.PhoneLoginFailed, ipAddress,
                newValue: "{\"outcome\":\"failed\"}", ct: ct);
            return Fail("PHONE_AUTH_FAILED");
        }
        // Deliberately not distinguishing IsLockedOut here: AuthController.cs's email login
        // used to do exactly that (a distinct locked-account response) and reverted it as a
        // documented security fix — a lockout-specific code is an enumeration oracle (wrong
        // password on a random number always fails generically; a real, rate-limited number
        // eventually answers differently, confirming it has an account). Same reasoning
        // applies to phone numbers, so every login failure stays PHONE_AUTH_FAILED.
        var signIn = await _signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        // A locked-out account returns before any hashing happens, which made it the fastest of the
        // three failure shapes (unknown / wrong password / locked). Spend the same single
        // verification the other two spend, so response time does not tell them apart.
        if (signIn.IsLockedOut) BurnOnePasswordVerification(password);
        if (!signIn.Succeeded)
        {
            await _audit.LogAsync(user.Id, AuditActions.PhoneLoginFailed, ipAddress,
                newValue: "{\"outcome\":\"failed\"}", ct: ct);
            return Fail("PHONE_AUTH_FAILED");
        }
        // Same rule the email path enforces in AuthenticationSessionIssuer: a banned or deleted
        // account must not obtain a session even with the right password. Checked only after the
        // password verified, so it cannot be used to probe which numbers belong to banned accounts.
        if (user.IsBanned || user.IsDeleted)
        {
            await _audit.LogAsync(user.Id, AuditActions.PhoneLoginFailed, ipAddress,
                newValue: "{\"outcome\":\"blocked\"}", ct: ct);
            return Fail("ACCOUNT_UNAVAILABLE");
        }
        user.LastLoginAt = _clock.GetUtcNow().UtcDateTime;
        await _users.UpdateAsync(user);
        await _audit.LogAsync(user.Id, AuditActions.PhoneLoginSucceeded, ipAddress,
            newValue: "{\"outcome\":\"succeeded\"}", ct: ct);
        return await IssueSessionAsync(user, ipAddress, ct);
    }

    public async Task<PhoneWorkflowResult> VerifyPasswordResetAsync(Guid challengeId, string code,
        CancellationToken ct, OtpChannel? channel = null)
    {
        var outcome = await ValidateAndReserveAsync(challengeId, code, OtpPurpose.PhonePasswordReset, null, channel, ct);
        if (outcome.Challenge is not { } challenge) return Fail(PasswordResetErrorCode(outcome.FailureReason));
        var user = await _users.Users.SingleOrDefaultAsync(x => x.NormalizedPhoneNumber == challenge.NormalizedPhoneNumber, ct);
        if (user is null) return Fail("PASSWORD_RESET_INVALID");
        await ConsumeAsync(challenge.Id, ct);
        return new(true, ConfirmationToken: await _users.GeneratePasswordResetTokenAsync(user));
    }

    public async Task<PhoneWorkflowResult> ConfirmPasswordResetAsync(string phoneNumber,
        string confirmationToken, string newPassword, string? ipAddress, CancellationToken ct)
    {
        var normalized = _normalizer.Normalize(phoneNumber);
        if (!normalized.Succeeded) return Fail("PASSWORD_RESET_INVALID");
        var user = await _users.Users.SingleOrDefaultAsync(x => x.NormalizedPhoneNumber == normalized.Value, ct);
        if (user is null) return Fail("PASSWORD_RESET_INVALID");
        var reset = await _users.ResetPasswordAsync(user, confirmationToken, newPassword);
        if (!reset.Succeeded) return IdentityFail(reset, "PASSWORD_RESET_INVALID");
        // Proving control of the number by OTP is stronger than the failed guesses that locked the
        // account, so the owner must be able to sign in with the new password straight away (audit F-7).
        await _users.ResetAccessFailedCountAsync(user);
        await _users.SetLockoutEndDateAsync(user, null);
        await _users.UpdateSecurityStampAsync(user);
        await _refreshTokens.RevokeActiveTokensForUserAsync(user.Id, _clock.GetUtcNow().UtcDateTime, ipAddress, ct);
        await _audit.LogAsync(user.Id, AuditActions.PhonePasswordResetCompleted, ipAddress,
            newValue: "{\"outcome\":\"succeeded\"}", ct: ct);
        return new(true, Message: "Password reset completed.");
    }

    public async Task<PhoneWorkflowResult> GetReverificationStatusAsync(Guid userId, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user?.PhoneLastVerifiedAtUtc is null) return Fail("PHONE_REVERIFICATION_NOT_CONFIGURED");
        var state = _policy.Evaluate(user.PhoneLastVerifiedAtUtc.Value, _clock.GetUtcNow());
        return new(true, VerificationState: state, DueAtUtc: user.PhoneVerificationDueAtUtc,
            GraceEndsAtUtc: user.PhoneVerificationGraceEndsAtUtc);
    }

    public async Task<PhoneWorkflowResult> VerifyReverificationAsync(Guid userId, Guid challengeId,
        string code, CancellationToken ct, OtpChannel? channel = null)
    {
        var outcome = await ValidateAndReserveAsync(challengeId, code, OtpPurpose.PhoneReverification, userId, channel, ct);
        if (outcome.Challenge is not { } challenge) return Fail(OtpErrorCode(outcome.FailureReason, "OTP_INVALID"));
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null || user.NormalizedPhoneNumber != challenge.NormalizedPhoneNumber) return Fail("OTP_INVALID");
        SetVerified(user, _clock.GetUtcNow());
        await ConsumeAsync(challenge.Id, ct);
        await _users.UpdateAsync(user);
        await _audit.LogAsync(user.Id, AuditActions.PhoneOwnershipReverified, null,
            newValue: "{\"outcome\":\"succeeded\"}", ct: ct);
        return new(true, VerificationState: PhoneVerificationState.Verified,
            DueAtUtc: user.PhoneVerificationDueAtUtc, GraceEndsAtUtc: user.PhoneVerificationGraceEndsAtUtc);
    }

    public async Task<PhoneWorkflowResult> VerifyPhoneChangeAsync(Guid userId, Guid challengeId,
        string code, string currentPassword, string? ipAddress, CancellationToken ct, OtpChannel? channel = null)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        // Counted towards Identity lockout (audit F-8): a stolen access token must not allow unlimited
        // guessing of the current password, only the rate limiter's few tries before the account locks.
        if (user is null ||
            !(await _signIn.CheckPasswordSignInAsync(user, currentPassword, lockoutOnFailure: true)).Succeeded)
            return Fail("RECENT_AUTHENTICATION_REQUIRED");
        var outcome = await ValidateAndReserveAsync(challengeId, code, OtpPurpose.PhoneNumberChange, userId, channel, ct);
        if (outcome.Challenge is not { } challenge) return Fail(OtpErrorCode(outcome.FailureReason, "OTP_INVALID"));
        if (await _users.Users.AnyAsync(x => x.NormalizedPhoneNumber == challenge.NormalizedPhoneNumber, ct))
            return Fail("PHONE_NUMBER_ALREADY_IN_USE");
        // The AnyAsync above is only a friendly early answer: another request can take the number between it
        // and the write, and the unique index is what actually decides. Registration handles that with a
        // transaction; so does this, otherwise the loser got a 500 and a burnt code.
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            user.PhoneNumber = challenge.NormalizedPhoneNumber;
            user.NormalizedPhoneNumber = challenge.NormalizedPhoneNumber;
            user.PhoneNumberConfirmed = true;
            SetVerified(user, _clock.GetUtcNow());
            await ConsumeAsync(challenge.Id, ct);
            await _users.UpdateSecurityStampAsync(user);
            await _users.UpdateAsync(user);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            await ReleaseAsync(challenge.Id, ct);
            return Fail("PHONE_NUMBER_ALREADY_IN_USE");
        }
        await _refreshTokens.RevokeActiveTokensForUserAsync(user.Id, _clock.GetUtcNow().UtcDateTime, ipAddress, ct);
        await _audit.LogAsync(user.Id, AuditActions.PhoneNumberChanged, ipAddress,
            newValue: "{\"outcome\":\"succeeded\"}", ct: ct);
        return new(true, Message: "Phone number changed.");
    }

    private async Task<OtpValidationOutcome> ValidateAndReserveAsync(Guid id, string code,
        OtpPurpose purpose, Guid? userId, OtpChannel? channel, CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        var challenge = await _db.PhoneOtpChallenges.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (challenge is null || challenge.Purpose != purpose || challenge.UserId != userId)
            return OtpValidationOutcome.Failed(OtpFailureReason.NotFound);
        // A code belongs to the channel it was created for. A caller that names another channel is refused
        // with the same generic outcome as an unknown challenge, and the attempt is not counted against the code.
        if (channel is { } requested && requested != challenge.Channel)
        {
            ApplicationTelemetry.RecordOtpVerification(challenge.Channel.ToString(), "failed");
            return OtpValidationOutcome.Failed(OtpFailureReason.NotFound);
        }
        if (challenge.ConsumedAtUtc is not null)
            return OtpValidationOutcome.Failed(OtpFailureReason.AlreadyUsed);
        if (challenge.ExpiresAtUtc <= now)
            return OtpValidationOutcome.Failed(OtpFailureReason.Expired);
        if (challenge.AttemptCount >= 3)
            return OtpValidationOutcome.Failed(OtpFailureReason.TooManyAttempts);
        if (challenge.ReservedUntilUtc > now)
            return OtpValidationOutcome.Failed(OtpFailureReason.NotFound);
        if (!_otp.Verify(code, challenge.CodeHash))
        {
            ApplicationTelemetry.RecordOtpVerification(challenge.Channel.ToString(), "failed");
            if (!_db.Database.IsRelational())
            {
                challenge.IncrementAttempts();
                await _db.SaveChangesAsync(ct);
                return OtpValidationOutcome.Failed(OtpFailureReason.WrongCode);
            }
            await _db.PhoneOtpChallenges.Where(x => x.Id == id && x.AttemptCount < 3)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1), ct);
            return OtpValidationOutcome.Failed(OtpFailureReason.WrongCode);
        }
        ApplicationTelemetry.RecordOtpVerification(challenge.Channel.ToString(), "succeeded");
        var reservation = Guid.NewGuid();
        if (!_db.Database.IsRelational())
        {
            challenge.Reserve(reservation, now.AddMinutes(2));
            await _db.SaveChangesAsync(ct);
            return OtpValidationOutcome.Success(challenge);
        }
        var affected = await _db.PhoneOtpChallenges.Where(x => x.Id == id && x.ConsumedAtUtc == null &&
                (x.ReservedUntilUtc == null || x.ReservedUntilUtc <= now))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReservationId, reservation)
                .SetProperty(x => x.ReservedUntilUtc, now.AddMinutes(2)), ct);
        return affected == 1
            ? OtpValidationOutcome.Success(await _db.PhoneOtpChallenges.SingleAsync(x => x.Id == id, ct))
            : OtpValidationOutcome.Failed(OtpFailureReason.NotFound);
    }

    /// <summary>
    /// Distinguishes wrong/expired/already-used/rate-limited so callers can return a specific
    /// error code instead of collapsing every OTP failure into one generic response. NotFound
    /// stays generic (covers purpose/user mismatch and the rare concurrent-reservation race) —
    /// those are attacker-probing/internal-race shapes, not something a legitimate user needs
    /// distinguished from "wrong code".
    /// </summary>
    private enum OtpFailureReason { NotFound, Expired, AlreadyUsed, TooManyAttempts, WrongCode }

    private readonly record struct OtpValidationOutcome(PhoneOtpChallenge? Challenge, OtpFailureReason? FailureReason)
    {
        public static OtpValidationOutcome Success(PhoneOtpChallenge challenge) => new(challenge, null);
        public static OtpValidationOutcome Failed(OtpFailureReason reason) => new(null, reason);
    }

    private static string OtpErrorCode(OtpFailureReason? reason, string genericCode) => reason switch
    {
        OtpFailureReason.Expired => "OTP_EXPIRED",
        OtpFailureReason.AlreadyUsed => "OTP_ALREADY_USED",
        OtpFailureReason.TooManyAttempts => "OTP_RATE_LIMITED",
        OtpFailureReason.WrongCode => "OTP_WRONG",
        _ => genericCode
    };

    private static string PasswordResetErrorCode(OtpFailureReason? reason) => reason switch
    {
        OtpFailureReason.Expired => "PASSWORD_RESET_EXPIRED",
        OtpFailureReason.AlreadyUsed => "PASSWORD_RESET_ALREADY_USED",
        OtpFailureReason.TooManyAttempts => "PASSWORD_RESET_RATE_LIMITED",
        _ => "PASSWORD_RESET_INVALID"
    };

    private async Task ConsumeAsync(Guid id, CancellationToken ct)
    {
        if (!_db.Database.IsRelational())
        {
            var challenge = await _db.PhoneOtpChallenges.SingleAsync(x => x.Id == id, ct);
            challenge.Consume(_clock.GetUtcNow());
            await _db.SaveChangesAsync(ct);
            return;
        }
        await _db.PhoneOtpChallenges.Where(x => x.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ConsumedAtUtc, _clock.GetUtcNow())
                .SetProperty(x => x.ReservationId, (Guid?)null)
                .SetProperty(x => x.ReservedUntilUtc, (DateTimeOffset?)null), ct);
    }

    private async Task ReleaseAsync(Guid id, CancellationToken ct)
    {
        if (!_db.Database.IsRelational())
        {
            var challenge = await _db.PhoneOtpChallenges.SingleAsync(x => x.Id == id, ct);
            challenge.Release();
            await _db.SaveChangesAsync(ct);
            return;
        }
        await _db.PhoneOtpChallenges.Where(x => x.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReservationId, (Guid?)null)
                .SetProperty(x => x.ReservedUntilUtc, (DateTimeOffset?)null), ct);
    }

    private ApplicationUser NewVerifiedUser(Guid id, string phone, DateTimeOffset now)
    {
        var user = new ApplicationUser
        {
            Id = id,
            UserName = phone,
            PhoneNumber = phone,
            NormalizedPhoneNumber = phone,
            PhoneNumberConfirmed = true,
            CreatedAt = now.UtcDateTime,
            UpdatedAt = now.UtcDateTime
        };
        SetVerified(user, now);
        return user;
    }

    private static void SetVerified(ApplicationUser user, DateTimeOffset now)
    {
        user.PhoneLastVerifiedAtUtc = now;
        user.PhoneVerificationDueAtUtc = now + PhoneVerificationPolicy.VerificationInterval;
        user.PhoneVerificationGraceEndsAtUtc = user.PhoneVerificationDueAtUtc + PhoneVerificationPolicy.GracePeriod;
        user.PhoneVerificationState = PhoneVerificationState.Verified;
    }

    private async Task<PhoneWorkflowResult> IssueSessionAsync(ApplicationUser user, string? ip, CancellationToken ct)
    {
        var roles = await _users.GetRolesAsync(user);
        var access = _tokens.GenerateAccessToken(new AccessTokenSubject(user.Id, user.Email, user.UserName, user.SecurityStamp), roles.ToArray());
        var refresh = _tokens.GenerateRefreshToken();
        await _refreshTokens.AddAsync(user.Id, refresh, ip, ct);
        return new(true, AccessToken: access, RefreshToken: refresh,
            AccessTokenExpiresAtUtc: _tokens.GetAccessTokenExpiresAtUtc(), VerificationState: user.PhoneVerificationState,
            DueAtUtc: user.PhoneVerificationDueAtUtc, GraceEndsAtUtc: user.PhoneVerificationGraceEndsAtUtc);
    }

    // One password-hasher verification against a fixed dummy hash, i.e. exactly the cost of checking a
    // wrong password for a real account. The hash is computed once per process (a fresh HashPassword on
    // every miss cost a second, extra hash and made unknown numbers slower than wrong passwords).
    private static string UnsatisfiableHash() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    private static string? _dummyPasswordHash;

    private void BurnOnePasswordVerification(string password)
    {
        var dummy = new ApplicationUser();
        var hash = _dummyPasswordHash ??= _users.PasswordHasher.HashPassword(dummy, "Dummy-Password-9D6A4E01");
        _users.PasswordHasher.VerifyHashedPassword(dummy, hash, password);
    }

    private static PhoneWorkflowResult Fail(string code) => new(false, code, "The request could not be completed.");
    private static PhoneWorkflowResult IdentityFail(IdentityResult result, string fallback) =>
        new(false, result.Errors.Any(e => e.Code.StartsWith("Password", StringComparison.Ordinal)) ? "PASSWORD_POLICY_FAILED" : fallback,
            "The request could not be completed.");
}
