using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.AI;

public interface IMigrationAnalysisAgent
{
    Task<MigrationAnalysisResult> AnalyzeAsync(
        MigrationAnalysisInput input,
        CancellationToken cancellationToken = default);
}