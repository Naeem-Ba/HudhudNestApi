namespace HudhudNestApi.Application.Auth.Interfaces;

public interface IJwtTokenSettings
{
    int AccessTokenMinutes { get; }
    int RefreshTokenDays { get; }

    /// <summary>
    /// How long (seconds) after a refresh token was rotated away its replacement may still be requested
    /// again from the same old token without that counting as theft. A phone loses the response of a
    /// refresh when the network drops or the OS suspends the app mid-request; the client then retries with
    /// the cookie it still holds. 0 turns the allowance off.
    /// </summary>
    int RefreshReuseGraceSeconds { get; }

    /// <summary>
    /// How many devices may stay signed in at once. Signing in on a further device ends the oldest session.
    /// 1 reproduces the former single-session behaviour.
    /// </summary>
    int MaxActiveSessions { get; }
}

