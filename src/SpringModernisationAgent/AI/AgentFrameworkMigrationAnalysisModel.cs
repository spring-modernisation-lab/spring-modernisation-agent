using Microsoft.Agents.AI;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.AI;

public sealed class AgentFrameworkMigrationAnalysisModel
    : IMigrationAnalysisModel
{
    private readonly AIAgent _agent;

    public AgentFrameworkMigrationAnalysisModel(
        AIAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        _agent = agent;
    }

    public async Task<MigrationAnalysisResult> AnalyzeAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var response =
            await _agent.RunAsync<MigrationAnalysisResult>(
                prompt,
                cancellationToken: cancellationToken);

        return response.Result
            ?? throw new InvalidOperationException(
                "The migration analysis agent returned no structured result.");
    }
}