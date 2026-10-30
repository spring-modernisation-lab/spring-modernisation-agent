namespace SpringModernisationAgent.Transformation;

public interface IRepositoryChangeDetector
{
    Task<IReadOnlyList<string>> GetChangedFilesAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default);
}