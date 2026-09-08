namespace PropertyApi.Domain.Audit.Constants;

public static class AuditActions
{
    public const string Login = "Login";
    public const string ChangePassword = "ChangePassword";
    public const string RoleChanged = "RoleChanged";
    public const string DeleteProperty = "DeleteProperty";
    public const string RefreshTokenReuseDetected = "RefreshTokenReuseDetected";
    public const string PhoneRegistrationOtpRequested = "PhoneRegistrationOtpRequested";
    public const string PhoneRegistrationCompleted = "PhoneRegistrationCompleted";
    public const string PhoneLoginSucceeded = "PhoneLoginSucceeded";
    public const string PhoneLoginFailed = "PhoneLoginFailed";
    public const string PhonePasswordResetOtpRequested = "PhonePasswordResetOtpRequested";
    public const string PhonePasswordResetCompleted = "PhonePasswordResetCompleted";
    public const string PhoneReverificationRequested = "PhoneReverificationRequested";
    public const string PhoneOwnershipReverified = "PhoneOwnershipReverified";
    public const string PhoneVerificationDueSoon = "PhoneVerificationDueSoon";
    public const string PhoneVerificationGraceStarted = "PhoneVerificationGraceStarted";
    public const string PhoneVerificationRestricted = "PhoneVerificationRestricted";
    public const string PhoneNumberChangeRequested = "PhoneNumberChangeRequested";
    public const string PhoneNumberChanged = "PhoneNumberChanged";
    public const string AccountDeletionCompleted = "AccountDeletionCompleted";
    public const string AccountDeletionRequested = "AccountDeletionRequested";
    public const string AccountDeletionCancelled = "AccountDeletionCancelled";
    public const string DataExportRequested = "DataExportRequested";

    // -- Admin dashboard: subscription lifecycle --------------------------
    public const string PlanActivatedByAdmin = "PlanActivatedByAdmin";
    public const string PlanExtendedByAdmin = "PlanExtendedByAdmin";
    public const string PlanCancelledByAdmin = "PlanCancelledByAdmin";

    // -- Admin dashboard: listing lifecycle --------------------------------
    public const string ListingFeaturedByAdmin = "ListingFeaturedByAdmin";
    public const string ListingUnfeaturedByAdmin = "ListingUnfeaturedByAdmin";
    public const string ListingExtendedByAdmin = "ListingExtendedByAdmin";
}
