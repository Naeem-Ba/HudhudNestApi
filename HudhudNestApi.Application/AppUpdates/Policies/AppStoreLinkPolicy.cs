using HudhudNestApi.Domain.AppUpdates.Enums;

namespace HudhudNestApi.Application.AppUpdates.Policies;

/// <summary>
/// What an <c>AppRelease.StoreUrl</c> may be. The link is shown to every installed client and opened by the
/// "Update now" button, so an admin typo (or a compromised admin account) must not be able to send the whole
/// user base to an arbitrary site. Rules:
/// <list type="bullet">
/// <item>https only, no user info, default port, absolute;</item>
/// <item>Android: <c>play.google.com</c>; iOS: <c>apps.apple.com</c> or <c>itunes.apple.com</c>;</item>
/// <item>Web has no store: any https link is accepted (the client never reads it);</item>
/// <item>an ENABLED Android or iOS release must have a link. Otherwise "Update now" does nothing, and a
/// mandatory update (a raised minimum version) would lock users out with no way to update.</item>
/// </list>
/// The Angular client checks the same host list again before opening the link.
/// </summary>
public static class AppStoreLinkPolicy
{
    private static readonly string[] AndroidHosts = ["play.google.com"];
    private static readonly string[] IosHosts = ["apps.apple.com", "itunes.apple.com"];

    public const string MissingLinkMessage =
        "الإصدار المفعّل لأندرويد أو iOS يحتاج رابط المتجر الرسمي، وإلا لن يستطيع المستخدم التحديث.";

    public static bool RequiresLink(AppPlatform platform) => platform is AppPlatform.Android or AppPlatform.IOS;

    /// <summary>True for a blank link (absence is judged by <see cref="Check"/>) or a trusted one.</summary>
    public static bool IsAcceptableLink(AppPlatform platform, string? storeUrl)
    {
        if (string.IsNullOrWhiteSpace(storeUrl))
            return true;

        if (!Uri.TryCreate(storeUrl.Trim(), UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo))
            return false;

        return platform switch
        {
            AppPlatform.Android => AndroidHosts.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase),
            AppPlatform.IOS => IosHosts.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase),
            _ => true,
        };
    }

    public static string InvalidLinkMessage(AppPlatform platform) => platform switch
    {
        AppPlatform.Android => "رابط المتجر لأندرويد يجب أن يكون https من play.google.com.",
        AppPlatform.IOS => "رابط المتجر لـ iOS يجب أن يكون https من apps.apple.com.",
        _ => "رابط المتجر يجب أن يكون رابط https صحيحًا.",
    };

    /// <summary>The first problem with this link for a release that is (or is about to be) enabled or not;
    /// null when it is fine.</summary>
    public static string? Check(AppPlatform platform, string? storeUrl, bool enabled)
    {
        if (!IsAcceptableLink(platform, storeUrl))
            return InvalidLinkMessage(platform);

        if (enabled && RequiresLink(platform) && string.IsNullOrWhiteSpace(storeUrl))
            return MissingLinkMessage;

        return null;
    }
}
