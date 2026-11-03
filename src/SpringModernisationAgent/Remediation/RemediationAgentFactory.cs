using SpringModernisationAgent.AI;

namespace SpringModernisationAgent.Remediation;

public static class RemediationAgentFactory
{
    /// <summary>Creates the Remediation Agent using the shared Foundry settings.</summary>
    public static RemediationAgent Create()
    {
        var foundry = FoundryProjectSettings.FromEnvironment();

        return new RemediationAgent(
            new AgentFrameworkRemediationModel(
                foundry.CreateProjectClient(),
                foundry.Model));
    }
}
