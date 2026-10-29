using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Config;

public sealed class SpringMigrationPolicy
{
    public List<SpringMigrationRoute> Routes { get; set; } = [];
}

public sealed class SpringMigrationRoute
{
    public string Id { get; set; } = string.Empty;

    public SpringSourceCriteria Source { get; set; } = new();

    public SpringTarget Target { get; set; } = new();

    public OpenRewriteConfiguration OpenRewrite { get; set; } = new();

    public MigrationRequirements Requirements { get; set; } = new();

    public List<string> Validation { get; set; } = [];
}

public sealed class SpringSourceCriteria
{
    public List<RepositoryKind> RepositoryKinds { get; set; } = [];

    public string SpringBootMinimum { get; set; } = string.Empty;

    public string SpringBootMaximum { get; set; } = string.Empty;
}

public sealed class SpringTarget
{
    public string SpringBoot { get; set; } = string.Empty;

    public string Java { get; set; } = string.Empty;
}

public sealed class OpenRewriteConfiguration
{
    public string Recipe { get; set; } = string.Empty;

    public string Artifact { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;
}

public sealed class MigrationRequirements
{
    public List<string> BuildSystems { get; set; } = [];

    public bool BaselineBuildMustPass { get; set; }
}
