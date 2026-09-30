namespace HudhudNestApi.Infrastructure.Identity.Services;

internal static class SecurityStampCacheKeys
{
    public static string ForUser(Guid userId) => $"securitystamp:{userId:N}";
}
