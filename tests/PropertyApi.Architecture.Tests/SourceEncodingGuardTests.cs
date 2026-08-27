using System.Text.RegularExpressions;

namespace PropertyApi.Architecture.Tests;

/// <summary>
/// Guards RELEASE-BLOCKERS-AR.md B-19: this codebase's Arabic comments were found saved as
/// literal ASCII '?' (0x3F) runs -- PhoneAuthController.cs, OtpCode.cs, and IHasDomainEvents.cs
/// each had entire comment blocks reduced to a run of '?' per lost Arabic character -- one
/// per character, so a whole word became a run several '?' long
/// (git history confirmed the original text was intact UTF-8 Arabic in an earlier commit;
/// something re-saved the file through a non-UTF-8 codec -- likely a Windows-locale editor or
/// tool -- and every non-ASCII character was replaced one-for-one with '?'). That corruption
/// is silent: the file still compiles, so nothing catches it short of a human reading the
/// comment and noticing it makes no sense.
///
/// This test fails the build when a tracked .cs file contains three or more consecutive '?'
/// characters, or the Unicode replacement character U+FFFD (the other common mojibake
/// signature, produced when a different wrong codec is used to *read* a UTF-8 file). The
/// three-run threshold is deliberate: it is long enough that "?" (nullable types),
/// "?:" (ternary), "??" and "??=" (null-coalescing) -- all single or double '?' -- never
/// trigger it, while even a two-letter corrupted Arabic word ("في" -> "??") mostly stays under
/// it too; the goal is catching gross corruption cheaply, not perfect recall. A file
/// legitimately needing three or more consecutive '?' in a string/comment (none do today --
/// see the negative test below) gets an explicit, reviewed entry in
/// <see cref="AllowlistedFiles"/> rather than a weakened pattern.
/// </summary>
public sealed class SourceEncodingGuardTests
{
    /// <summary>
    /// Repo-relative paths explicitly reviewed and confirmed to contain three-or-more '?' runs
    /// or U+FFFD legitimately -- e.g. a string literal or code comment that happens to need it,
    /// not corruption. Empty today: nothing in this codebase needs an exception yet.
    /// </summary>
    private static readonly HashSet<string> AllowlistedFiles =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Regex SuspiciousQuestionMarkRun = new(@"\?{3,}", RegexOptions.Compiled);

    [Fact(DisplayName = "No tracked .cs file contains mojibake (long '?' runs or U+FFFD)")]
    public void SourceFiles_Should_Not_Contain_EncodingCorruption()
    {
        var repoRoot = FindRepositoryRoot();
        var offenders = new List<string>();

        foreach (var file in EnumerateTrackedCsFiles(repoRoot))
        {
            var relativePath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            if (AllowlistedFiles.Contains(relativePath))
                continue;

            var text = File.ReadAllText(file);

            var findings = new List<string>();

            if (SuspiciousQuestionMarkRun.IsMatch(text))
            {
                var firstMatch = SuspiciousQuestionMarkRun.Match(text);
                var lineNumber = text[..firstMatch.Index].Count(c => c == '\n') + 1;
                findings.Add($"a run of {firstMatch.Length} '?' characters at line {lineNumber}");
            }

            // Built from a numeric char code, not typed as a literal, so this guard's own
            // source file never contains the byte it is searching for -- otherwise it would
            // flag itself.
            if (text.Contains((char)0xFFFD))
            {
                var index = text.IndexOf((char)0xFFFD);
                var lineNumber = text[..index].Count(c => c == '\n') + 1;
                findings.Add($"the Unicode replacement character (U+FFFD) at line {lineNumber}");
            }

            if (findings.Count > 0)
            {
                offenders.Add($"{relativePath}: {string.Join("; ", findings)}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These files look like they were saved through the wrong text encoding, " +
            "destroying non-ASCII (Arabic) comments the way RELEASE-BLOCKERS-AR.md B-19 " +
            "found in PhoneAuthController.cs/OtpCode.cs/IHasDomainEvents.cs. Re-save the " +
            "file as UTF-8 and restore the lost text (check git history for a pre-corruption " +
            "version first) rather than adding it to AllowlistedFiles:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    [Fact(DisplayName = "The corruption pattern would be caught if it recurred")]
    public void Guard_Detects_TheActualCorruptionPattern()
    {
        // Built up from a repeated single '?' rather than typed as a run, so this guard's
        // own source file does not itself trip SourceFiles_Should_Not_Contain_EncodingCorruption
        // above -- reproduces the exact shape git history showed for the corrupted files
        // (a whole word replaced by one '?' per character).
        var run = new string('?', 4);
        var corrupted = $"/// {run} {run} OTP {run.Substring(0, 2)} {run} Domain";

        Assert.Matches(SuspiciousQuestionMarkRun, corrupted);
    }

    [Theory(DisplayName = "The guard does not flag legitimate C# '?' syntax")]
    [InlineData("public string? Name { get; set; }")]
    [InlineData("var x = condition ? valueA : valueB;")]
    [InlineData("var y = maybeNull ?? fallback;")]
    [InlineData("value ??= fallback;")]
    [InlineData("var z = a?.b?.c;")]
    [InlineData("public List<int?>? Numbers { get; set; }")]
    public void Guard_Does_Not_Flag_LegitimateSyntax(string snippet)
    {
        Assert.DoesNotMatch(SuspiciousQuestionMarkRun, snippet);
    }

    private static IEnumerable<string> EnumerateTrackedCsFiles(string repoRoot)
        => Directory.EnumerateFiles(repoRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                var segments = path.Replace('\\', '/').Split('/');
                return !segments.Any(segment =>
                    segment is "bin" or "obj" or "node_modules" or ".git");
            });

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PropertyApi.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing PropertyApi.sln.");
    }
}
