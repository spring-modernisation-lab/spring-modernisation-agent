using Microsoft.Extensions.AI;

namespace SpringModernisationAgent.Remediation;

/// <summary>The language model behind the Remediation Agent, isolated for testing.</summary>
public interface IRemediationModel
{
    Task<RemediationModelResponse> ProposeAsync(
        string instructions,
        string prompt,
        IList<AITool> tools,
        CancellationToken cancellationToken = default);
}

public sealed record RemediationModelResponse(
    RemediationProposal? Proposal,
    long? InputTokens,
    long? OutputTokens);
