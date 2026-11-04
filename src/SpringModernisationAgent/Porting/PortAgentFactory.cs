using SpringModernisationAgent.AI;
using SpringModernisationAgent.Remediation;

namespace SpringModernisationAgent.Porting;

public static class PortAgentFactory
{
    /// <summary>Creates the porting agent using the shared Foundry settings.</summary>
    public static PortAgent Create()
    {
        var foundry = FoundryProjectSettings.FromEnvironment();

        return new PortAgent(
            new AgentFrameworkRemediationModel(
                foundry.CreateProjectClient(),
                foundry.Model));
    }
}
