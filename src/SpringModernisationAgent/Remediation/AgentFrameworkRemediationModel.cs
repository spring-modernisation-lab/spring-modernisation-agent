using Azure.AI.Projects;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace SpringModernisationAgent.Remediation;

/// <summary>
/// Runs the Remediation Agent on Foundry. A fresh agent is created for each
/// attempt because its tools are bound to that attempt's workspace. The
/// Agent Framework runs any tool calls the model makes in this process,
/// then returns the structured proposal.
/// </summary>
public sealed class AgentFrameworkRemediationModel : IRemediationModel
{
    private readonly AIProjectClient _projectClient;
    private readonly string _model;

    public AgentFrameworkRemediationModel(
        AIProjectClient projectClient,
        string model)
    {
        ArgumentNullException.ThrowIfNull(projectClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        _projectClient = projectClient;
        _model = model;
    }

    public async Task<RemediationModelResponse> ProposeAsync(
        string instructions,
        string prompt,
        IList<AITool> tools,
        CancellationToken cancellationToken = default)
    {
        AIAgent agent = _projectClient.AsAIAgent(
            model: _model,
            instructions: instructions,
            name: "SpringRemediationAgent",
            tools: tools);

        var response = await agent.RunAsync<RemediationProposal>(
            prompt,
            cancellationToken: cancellationToken);

        return new RemediationModelResponse(
            response.Result,
            response.Usage?.InputTokenCount,
            response.Usage?.OutputTokenCount);
    }
}
