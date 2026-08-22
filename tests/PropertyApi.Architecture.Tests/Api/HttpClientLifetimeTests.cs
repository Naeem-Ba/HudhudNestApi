namespace PropertyApi.Architecture.Tests.Api;

/// <summary>
/// Production code must obtain HttpClient from IHttpClientFactory, never construct one.
///
/// PasswordSecurityService used to be registered with a scoped factory that did
/// `new HttpClient { Timeout = ... }` and returned it. Nothing disposed it, so every
/// registration attempt leaked a client and its socket, and DNS changes were never
/// picked up -- the textbook route to SocketException under load. Three other
/// integrations in the same solution already used AddHttpClient, so this was an
/// inconsistency as much as a bug.
///
/// A source-level check rather than a reflection one, because the defect is in how the
/// object is created, which does not survive into metadata.
/// </summary>
public sealed class HttpClientLifetimeTests
{
    private static readonly string[] ProductionProjects =
    [
        "PropertyApi",
        "PropertyApi.Application",
        "PropertyApi.Domain",
        "PropertyApi.Infrastructure"
    ];

    [Fact(DisplayName = "Production code must not construct HttpClient directly")]
    public void ProductionCode_Should_Not_Construct_HttpClient()
    {
        var repositoryRoot = FindRepositoryRoot();
        var offenders = new List<string>();

        foreach (var project in ProductionProjects)
        {
            var projectDirectory = Path.Combine(repositoryRoot, project);
            if (!Directory.Exists(projectDirectory)) continue;

            var sourceFiles = Directory
                .EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

            foreach (var file in sourceFiles)
            {
                var lines = File.ReadAllLines(file);

                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    var trimmed = line.TrimStart();

                    // Comments explaining the old pattern are fine; code is not.
                    if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
                        trimmed.StartsWith("///", StringComparison.Ordinal) ||
                        trimmed.StartsWith("*", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (line.Contains("new HttpClient", StringComparison.Ordinal))
                    {
                        offenders.Add($"{Path.GetRelativePath(repositoryRoot, file)}:{i + 1}");
                    }
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These lines construct an HttpClient directly. Register the consumer with " +
            "services.AddHttpClient<TInterface, TImplementation>(...) so the handler is " +
            "pooled and its lifetime is managed:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PropertyApi.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing PropertyApi.sln.");
    }
}
