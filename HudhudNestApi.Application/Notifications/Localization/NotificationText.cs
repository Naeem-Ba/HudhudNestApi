namespace HudhudNestApi.Application.Notifications.Localization;

/// <summary>
/// A notification sentence that is not yet in any language: a catalog key plus its arguments.
/// Handlers describe WHAT happened; the notification service renders it in the RECIPIENT's
/// language when it persists the notification. This is what keeps an Arabic-speaking host's
/// action from reaching a German guest in Arabic.
///
/// Argument values may be strings and numbers (formatted invariantly), dates (formatted by the
/// renderer), or another <see cref="NotificationText"/> (rendered in the same language).
/// User-written content (a property title, a note) is passed through verbatim, never translated.
/// </summary>
public sealed record NotificationText
{
    private static readonly IReadOnlyDictionary<string, object?> NoArgs =
        new Dictionary<string, object?>();

    private NotificationText(string? key, string? literal, IReadOnlyDictionary<string, object?> args)
    {
        Key = key;
        Literal = literal;
        Args = args;
    }

    /// <summary>Catalog key, or null for a <see cref="Literal"/>.</summary>
    public string? Key { get; }

    /// <summary>Pre-written text shown as-is in every language. Legacy callers only.</summary>
    public string? Literal { get; }

    public IReadOnlyDictionary<string, object?> Args { get; }

    public static NotificationText Of(string key, params (string Name, object? Value)[] args)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("A notification key is required.", nameof(key));
        }

        var map = new Dictionary<string, object?>(args.Length, StringComparer.Ordinal);
        foreach (var (name, value) in args)
        {
            map[name] = value;
        }

        return new NotificationText(key, null, map);
    }

    /// <summary>Text that is shown identically in every language (user content, or an old caller).</summary>
    public static NotificationText FromLiteral(string? text) =>
        new(null, text ?? string.Empty, NoArgs);

    public static NotificationText Empty { get; } = FromLiteral(string.Empty);

    public static implicit operator NotificationText(string? text) => FromLiteral(text);
}
