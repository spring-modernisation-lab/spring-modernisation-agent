using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Config;

public sealed class MigrationRouteSelector
{
    public MigrationRouteSelection Select(
        RepositoryKind repositoryKind,
        string? springBootVersion,
        SpringMigrationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (string.IsNullOrWhiteSpace(springBootVersion))
        {
            return new MigrationRouteSelection(
                MigrationRouteSelectionStatus.NoMatch,
                Route: null,
                Reason: "Spring Boot version is unknown.");
        }

        if (!TryParseVersion(
                springBootVersion,
                out var detectedVersion))
        {
            return new MigrationRouteSelection(
                MigrationRouteSelectionStatus.NoMatch,
                Route: null,
                Reason:
                    $"Spring Boot version '{springBootVersion}' " +
                    "could not be parsed.");
        }

        var matches = policy.Routes
            .Where(route => MatchesRepositoryKind(repositoryKind, route.Source))
            .Where(route => Matches(detectedVersion, route.Source))
            .ToList();

        return matches.Count switch
        {
            0 => new MigrationRouteSelection(
                MigrationRouteSelectionStatus.NoMatch,
                Route: null,
                Reason:
                    $"No approved migration route matches {repositoryKind} " +
                    $"using Spring Boot {springBootVersion}."),

            1 => new MigrationRouteSelection(
                MigrationRouteSelectionStatus.Matched,
                Route: matches[0],
                Reason: null),

            _ => new MigrationRouteSelection(
                MigrationRouteSelectionStatus.Ambiguous,
                Route: null,
                Reason:
                    $"Multiple approved migration routes match {repositoryKind} " +
                    $"using Spring Boot {springBootVersion}.")
        };
    }

    private static bool MatchesRepositoryKind(
        RepositoryKind repositoryKind,
        SpringSourceCriteria source)
    {
        return source.RepositoryKinds.Count == 0 ||
               source.RepositoryKinds.Contains(repositoryKind);
    }

    private static bool Matches(
        Version detectedVersion,
        SpringSourceCriteria source)
    {
        if (!TryParseVersion(
                source.SpringBootMinimum,
                out var minimum))
        {
            return false;
        }

        if (detectedVersion < minimum)
        {
            return false;
        }

        return MatchesMaximum(
            detectedVersion,
            source.SpringBootMaximum);
    }

    private static bool MatchesMaximum(
        Version version,
        string maximum)
    {
        if (maximum.EndsWith(
                ".x",
                StringComparison.OrdinalIgnoreCase))
        {
            var prefix =
                maximum[..^2];

            if (!TryParseVersion(
                    prefix,
                    out var prefixVersion))
            {
                return false;
            }

            return version.Major == prefixVersion.Major
                && version.Minor == prefixVersion.Minor;
        }

        return TryParseVersion(
                maximum,
                out var maximumVersion)
            && version <= maximumVersion;
    }

    private static bool TryParseVersion(
        string value,
        out Version version)
    {
        return Version.TryParse(
            NormaliseVersion(value),
            out version!);
    }

    private static string NormaliseVersion(string value)
    {
        var cleanValue = value.Trim();

        return cleanValue.Count(character => character == '.') switch
        {
            0 => $"{cleanValue}.0.0",
            1 => $"{cleanValue}.0",
            _ => cleanValue
        };
    }
}
