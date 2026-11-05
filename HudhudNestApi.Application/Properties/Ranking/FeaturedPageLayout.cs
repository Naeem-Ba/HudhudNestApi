namespace HudhudNestApi.Application.Properties.Ranking;

/// <summary>
/// How one page of the default (newest-first) listing list is made of featured and ordinary
/// listings. Pure arithmetic with no database, so the paging rule can be tested exhaustively.
///
/// The list is two sequences that never overlap: the currently featured listings (their own
/// order: ads in the caller's interest area first, then the rest, each newest first) and the
/// ordinary ones (newest first). A page takes at most <c>cap</c> featured listings and fills the
/// remaining slots from the ordinary sequence, so a page is never a wall of paid ads and every
/// featured listing is still reached: the ones beyond the cap on page 1 simply lead page 2, 3 and
/// so on, instead of being demoted for good. When the ordinary listings run out, featured ones
/// fill the page (and the reverse), so a page is short only on the last page.
///
/// Page N is a pure function of (N, pageSize, featuredCount, ordinaryCount, cap): the same
/// request always produces the same slice of each sequence, which is what makes paging
/// duplicate-free and gap-free.
/// </summary>
public static class FeaturedPageLayout
{
    /// <summary>Per-page featured limit used when nothing is configured.</summary>
    public const int DefaultMaxFeaturedPerPage = 5;

    public readonly record struct Slice(int FeaturedSkip, int FeaturedTake, int OrdinarySkip, int OrdinaryTake);

    /// <summary>The featured limit for a page: about a quarter of it, never more than <paramref name="configuredMax"/>.</summary>
    public static int CapFor(int pageSize, int configuredMax)
        => configuredMax <= 0 ? 0 : Math.Clamp((pageSize + 3) / 4, 1, configuredMax);

    public static Slice Plan(int page, int pageSize, long featuredCount, long ordinaryCount, int configuredMax)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        var cap = CapFor(pageSize, configuredMax);

        long featuredUsed = 0;
        long ordinaryUsed = 0;
        var featuredTake = 0;
        var ordinaryTake = 0;

        for (var current = 1; current <= page; current++)
        {
            var featuredLeft = Math.Max(featuredCount - featuredUsed, 0);
            var ordinaryLeft = Math.Max(ordinaryCount - ordinaryUsed, 0);

            var f = (int)Math.Min(cap, featuredLeft);
            var o = (int)Math.Min(pageSize - f, ordinaryLeft);
            if (f + o < pageSize)
                f = (int)Math.Min(featuredLeft, pageSize - o);

            if (current == page)
                return new Slice((int)featuredUsed, f, (int)ordinaryUsed, o);

            featuredUsed += f;
            ordinaryUsed += o;
            featuredTake = f;
            ordinaryTake = o;
        }

        return new Slice((int)featuredUsed, featuredTake, (int)ordinaryUsed, ordinaryTake);
    }
}
