using SpringModernisationAgent.Maven;

namespace SpringModernisationAgent.Models;

public sealed record RepositoryInventory(
    string RepositoryName,
    MavenProject MavenProject);
