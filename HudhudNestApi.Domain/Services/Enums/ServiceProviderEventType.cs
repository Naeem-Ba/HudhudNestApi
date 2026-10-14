namespace HudhudNestApi.Domain.Services.Enums;

/// <summary>What one <see cref="Entities.ServiceProviderEvent"/> row records.</summary>
public enum ServiceProviderEventType
{
    ApplicationSubmitted = 0,
    InfoRequested = 1,
    ApplicantResponded = 2,
    Note = 3,
    ApplicationApproved = 4,
    ApplicationRejected = 5,
    ApplicationWithdrawn = 6,
    ProviderCreated = 7,
    ProviderLinked = 8,
    ProviderUpdated = 9,
    ProviderSuspended = 10,
    ProviderReactivated = 11,
    ProviderRevoked = 12,
    VerificationChanged = 13,
}
