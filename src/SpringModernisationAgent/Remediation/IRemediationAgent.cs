using Microsoft.Extensions.AI;

namespace SpringModernisationAgent.Remediation;

public interface IRemediationAgent
{
    /// <exception cref="InvalidRemediationProposalException">The model's answer was not a usable proposal.</exception>
    Task<RemediationAgentResult> ProposeAsync(
        RemediationRequest request,
        IList<AITool> tools,
        CancellationToken cancellationToken = default);
}
