namespace HudhudNestApi.Domain.Demand.Enums;

/// <summary>How a listing came to be matched with a search request.</summary>
public enum SearchRequestMatchSource
{
    /// <summary>The background matcher found it.</summary>
    Automatic = 1,

    /// <summary>The advertiser proposed it from the anonymous request board.</summary>
    Proposed = 2,
}
