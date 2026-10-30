namespace HudhudNestApi.Application.Services;

/// <summary>
/// Upper bounds the services validators share. Each stops a value the database or the clock
/// cannot hold from reaching it (QA Phase 11, P11-D04/D05); none is a business rule.
/// </summary>
public static class ServiceValidationLimits
{
    /// <summary>Ten years: far beyond any real inspection or valuation, well inside int.</summary>
    public const int MaxEstimatedDurationDays = 3650;

    /// <summary>ServiceProviders.ContactEmail is varchar(320), the RFC 5321 maximum.</summary>
    public const int MaxContactEmailLength = 320;

    /// <summary>ServiceRequests.ProviderNote is varchar(1000).</summary>
    public const int MaxProviderNoteLength = 1000;

    /// <summary>How far ahead a provider may schedule a visit.</summary>
    public static readonly TimeSpan MaxScheduleAhead = TimeSpan.FromDays(365 * 5);
}
