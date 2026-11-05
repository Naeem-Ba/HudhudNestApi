namespace HudhudNestApi.Infrastructure.Repositories;

/// <summary>Settings for how the default listing list mixes featured and ordinary listings.</summary>
public sealed class ListingRankingOptions
{
    public const string SectionName = "Listings:Ranking";

    /// <summary>
    /// Most featured listings one page of the default list may carry (further limited to about a
    /// quarter of the page). The featured listings beyond it are not hidden: they lead the
    /// following pages. 0 turns featured placement off in the list.
    /// </summary>
    public int MaxFeaturedPerPage { get; set; } = HudhudNestApi.Application.Properties.Ranking.FeaturedPageLayout.DefaultMaxFeaturedPerPage;
}
