namespace PropertyApi.Domain.Audit.Constants;

public static class AuditActions
{
    public const string Login = "Login";
    public const string ChangePassword = "ChangePassword";
    public const string RoleChanged = "RoleChanged";
    public const string DeleteProperty = "DeleteProperty";
    public const string RefreshTokenReuseDetected = "RefreshTokenReuseDetected";
}
