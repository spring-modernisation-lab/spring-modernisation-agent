namespace SpringModernisationAgent.Remediation;

/// <summary>A library the target platform no longer supports.</summary>
/// <param name="Name">For people, for example "Ehcache 2".</param>
/// <param name="Package">Java package prefix of its API, for example <c>net.sf.ehcache</c>.</param>
/// <param name="PomMarkers">Text in a <c>pom.xml</c> that shows the dependency is declared.</param>
/// <param name="Replacements">The supported replacements, for the agent and the reviewer.</param>
public sealed record UnsupportedLibrary(
    string Name,
    string Package,
    IReadOnlyList<string> PomMarkers,
    string Replacements);

/// <summary>
/// Libraries Spring Boot 3 dropped. Replacing one is a design decision, so
/// the Remediation Agent may not write code against them (the applier
/// rejects it) and is told which ones a repository uses.
/// </summary>
public static class UnsupportedLibraries
{
    public static IReadOnlyList<UnsupportedLibrary> All { get; } =
    [
        new(
            "Ehcache 2",
            "net.sf.ehcache",
            ["<groupId>net.sf.ehcache</groupId>"],
            "Ehcache 3 through JCache (org.ehcache, javax.cache), Caffeine, or Redis"),
        new(
            "Keycloak Spring adapter",
            "org.keycloak.adapters",
            ["keycloak-spring-boot", "keycloak-spring-security-adapter"],
            "Spring Security's OAuth2 resource server or OAuth2 login"),
        new(
            "Springfox",
            "springfox.documentation",
            ["<groupId>io.springfox</groupId>"],
            "springdoc-openapi")
    ];

    /// <summary>The unsupported libraries a repository declares or imports, in list order.</summary>
    public static IReadOnlyList<UnsupportedLibrary> FindIn(
        string repositoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = true
        };

        var found = new HashSet<UnsupportedLibrary>();

        foreach (var file in Directory.EnumerateFiles(repositoryPath, "*", options))
        {
            var relative = Path.GetRelativePath(repositoryPath, file).Replace('\\', '/');

            if (relative.Split('/').Any(segment => segment is ".git" or "target" or "node_modules"))
            {
                continue;
            }

            var isPom = Path.GetFileName(file) == "pom.xml";
            var isSource = relative.Contains("src/main/", StringComparison.Ordinal)
                           && Path.GetExtension(file) is ".java" or ".kt";

            if (!isPom && !isSource)
            {
                continue;
            }

            var text = File.ReadAllText(file);

            foreach (var library in All)
            {
                if (isPom
                        ? library.PomMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal))
                        : text.Contains($"import {library.Package}.", StringComparison.Ordinal))
                {
                    found.Add(library);
                }
            }
        }

        return All.Where(found.Contains).ToList();
    }
}
