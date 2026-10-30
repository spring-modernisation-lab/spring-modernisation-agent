using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.AI;

public interface IMigrationAnalysisModel
{
    Task<MigrationAnalysisResult> AnalyzeAsync(
        string prompt,
        CancellationToken cancellationToken = default);
}