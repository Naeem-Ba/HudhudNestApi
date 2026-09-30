using HudhudNestApi.Domain.Enums;

namespace HudhudNestApi.Application.Auth.Phone;

public sealed record PhoneWorkflowResult(
    bool Succeeded,
    string? ErrorCode = null,
    string? Message = null,
    Guid? ChallengeId = null,
    string? AccessToken = null,
    string? RefreshToken = null,
    DateTime? AccessTokenExpiresAtUtc = null,
    string? ConfirmationToken = null,
    PhoneVerificationState? VerificationState = null,
    DateTimeOffset? DueAtUtc = null,
    DateTimeOffset? GraceEndsAtUtc = null,
    OtpChannel? Channel = null);

/// <remarks>
/// The trailing <c>channel</c> parameters are optional so existing callers keep working: a send without one uses
/// SMS, and a verify without one accepts the challenge whatever channel it was created for. A verify WITH one is
/// refused unless it names the challenge's own channel.
/// </remarks>
public interface IPhoneAuthenticationWorkflow
{
    Task<PhoneWorkflowResult> SendOtpAsync(string phoneNumber, OtpPurpose purpose,
        Guid? userId, string? ipAddress, CancellationToken ct, OtpChannel channel = OtpChannel.Sms);
    Task<PhoneWorkflowResult> RegisterAsync(Guid challengeId, string code, string password,
        string firstName, string lastName, string? ipAddress, CancellationToken ct, OtpChannel? channel = null);
    Task<PhoneWorkflowResult> LoginAsync(string phoneNumber, string password,
        string? ipAddress, CancellationToken ct);
    Task<PhoneWorkflowResult> VerifyPasswordResetAsync(Guid challengeId, string code,
        CancellationToken ct, OtpChannel? channel = null);
    Task<PhoneWorkflowResult> ConfirmPasswordResetAsync(string phoneNumber, string confirmationToken,
        string newPassword, string? ipAddress, CancellationToken ct);
    Task<PhoneWorkflowResult> GetReverificationStatusAsync(Guid userId, CancellationToken ct);
    Task<PhoneWorkflowResult> VerifyReverificationAsync(Guid userId, Guid challengeId,
        string code, CancellationToken ct, OtpChannel? channel = null);
    Task<PhoneWorkflowResult> VerifyPhoneChangeAsync(Guid userId, Guid challengeId,
        string code, string currentPassword, string? ipAddress, CancellationToken ct, OtpChannel? channel = null);
}
