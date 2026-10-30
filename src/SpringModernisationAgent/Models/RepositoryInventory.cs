using SpringModernisationAgent.Maven;

namespace SpringModernisationAgent.Models;

public sealed record RepositoryInventory(
    string RepositoryName,
    string CommitSha,
    MavenProject MavenProject);
