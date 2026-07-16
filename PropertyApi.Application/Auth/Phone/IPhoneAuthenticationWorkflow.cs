using PropertyApi.Domain.Enums;

namespace PropertyApi.Application.Auth.Phone;

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
    DateTimeOffset? GraceEndsAtUtc = null);

public interface IPhoneAuthenticationWorkflow
{
    Task<PhoneWorkflowResult> SendOtpAsync(string phoneNumber, OtpPurpose purpose,
        Guid? userId, string? ipAddress, CancellationToken ct);
    Task<PhoneWorkflowResult> RegisterAsync(Guid challengeId, string code, string password,
        string firstName, string lastName, string? ipAddress, CancellationToken ct);
    Task<PhoneWorkflowResult> LoginAsync(string phoneNumber, string password,
        string? ipAddress, CancellationToken ct);
    Task<PhoneWorkflowResult> VerifyPasswordResetAsync(Guid challengeId, string code,
        CancellationToken ct);
    Task<PhoneWorkflowResult> ConfirmPasswordResetAsync(string phoneNumber, string confirmationToken,
        string newPassword, string? ipAddress, CancellationToken ct);
    Task<PhoneWorkflowResult> GetReverificationStatusAsync(Guid userId, CancellationToken ct);
    Task<PhoneWorkflowResult> VerifyReverificationAsync(Guid userId, Guid challengeId,
        string code, CancellationToken ct);
    Task<PhoneWorkflowResult> VerifyPhoneChangeAsync(Guid userId, Guid challengeId,
        string code, string currentPassword, string? ipAddress, CancellationToken ct);
}
