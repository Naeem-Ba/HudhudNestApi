namespace HudhudNestApi.Architecture.Tests.BackgroundJobs;

/// <summary>
/// Guards the B-6/B-6b decision that <c>SecurityAlertBackgroundService</c> deliberately does
/// not take a <c>BackgroundJobLock</c> advisory lock, unlike the other recurring hosted
/// services (<c>ListingExpiryHostedService</c>, <c>SavedSearchMatchHostedService</c>,
/// <c>PhoneVerificationHostedService</c>, <c>AuditLogRetentionHostedService</c>,
/// <c>AccountDeletionSweepHostedService</c>, <c>SocialPublicationDispatchHostedService</c>,
/// <c>ValuationInquiryExpiryHostedService</c>).
///
/// The reasoning (see <c>BackgroundJobLockKeys.cs</c>): the other seven sweep rows that every
/// deployed instance can see and mutate, so an unlocked sweep double-processes the same rows.
/// <c>SecurityAlertBackgroundService</c> instead drains an in-process <c>Channel</c> fed only
/// by requests that landed on that same instance -- there is no shared state across instances
/// for a lock to protect, and taking one would serialise alert delivery onto a single instance
/// process-wide, starving the bounded (DropOldest) queue on every other instance under a
/// credential-stuffing burst. That is a functional regression, not a safety improvement.
///
/// These tests pin that reasoning as source rather than re-deriving it from behaviour, so a
/// future change to either file has to consciously touch this test instead of silently
/// re-introducing (or silently removing) the exclusion.
/// </summary>
public sealed class BackgroundJobLockExclusionTests
{
    [Fact(DisplayName = "BackgroundJobLockKeys documents why SecurityAlertBackgroundService is excluded")]
    public void BackgroundJobLockKeys_Should_Document_SecurityAlertBackgroundService_Exclusion()
    {
        var source = ReadSource("HudhudNestApi.Infrastructure", "Persistence", "BackgroundJobLockKeys.cs");

        Assert.Contains("SecurityAlertBackgroundService", source);
        Assert.Contains("Deliberately excluded", source);

        // Exactly eight keys exist today (see the Theory test below for the locked services;
        // OtpCleanup's key is not a BackgroundService of its own). If another recurring
        // hosted service is added, it must either get a key here (and a corresponding case in
        // AdvisoryLockConcurrencyTests) or be added to the excluded list with its own
        // reasoning -- not silently fall through either check.
        Assert.Equal(8, System.Text.RegularExpressions.Regex.Matches(source, @"public const long \w+ =").Count);
    }

    [Fact(DisplayName = "SecurityAlertBackgroundService does not take a BackgroundJobLock")]
    public void SecurityAlertBackgroundService_Should_Not_Reference_BackgroundJobLock()
    {
        var source = ReadSource("HudhudNestApi.Infrastructure", "Email", "SecurityAlertBackgroundService.cs");

        Assert.DoesNotContain("BackgroundJobLock", source);

        // It must still explain, in its own file, why it can run unlocked on every instance --
        // the property that makes the exclusion safe (an in-process queue, not a shared scan).
        Assert.Contains("Channel", source);
    }

    [Theory(DisplayName = "The cross-instance sweeps still take their BackgroundJobLock")]
    [InlineData("Listings", "ListingExpiryHostedService.cs")]
    [InlineData("Search", "SavedSearchMatchHostedService.cs")]
    [InlineData("Auth\\Services", "PhoneVerificationHostedService.cs")]
    [InlineData("Audit", "AuditLogRetentionHostedService.cs")]
    [InlineData("Users", "AccountDeletionSweepHostedService.cs")]
    [InlineData("SocialDistribution", "SocialPublicationDispatchHostedService.cs")]
    [InlineData("Valuation", "ValuationInquiryExpiryHostedService.cs")]
    public void HostedService_Should_Reference_BackgroundJobLock(string relativeDir, string fileName)
    {
        var source = ReadSource(
            "HudhudNestApi.Infrastructure",
            relativeDir.Replace('\\', Path.DirectorySeparatorChar),
            fileName);

        Assert.Contains("BackgroundJobLock", source);
    }

    private static string ReadSource(params string[] relativeSegments)
    {
        var path = Path.Combine(FindRepositoryRoot(), Path.Combine(relativeSegments));
        Assert.True(File.Exists(path), $"Expected source file not found: {path}");
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HudhudNestApi.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing HudhudNestApi.sln.");
    }
}
