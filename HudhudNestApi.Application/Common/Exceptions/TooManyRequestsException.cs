namespace HudhudNestApi.Application.Common.Exceptions;

/// <summary>
/// The caller has done something too often. Mapped to HTTP 429 with a Retry-After header. Used for limits
/// that depend on stored data (e.g. messages already sent about one listing), which the per-minute HTTP
/// rate limiter cannot express.
/// </summary>
public sealed class TooManyRequestsException : Exception
{
    public TooManyRequestsException(string message, string code, TimeSpan retryAfter)
        : base(message)
    {
        Code = code;
        RetryAfter = retryAfter;
    }

    /// <summary>Stable code a localised client can translate.</summary>
    public string Code { get; }

    public TimeSpan RetryAfter { get; }
}
