namespace SpringModernisationAgent.Workspace;

public sealed record RepositoryWorkspace(
    string RepositoryName,
    string RepositoryPath,
    string CommitSha);