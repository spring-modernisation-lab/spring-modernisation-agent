using Azure.AI.Projects;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;

namespace SpringModernisationAgent.AI;

public static class MigrationAnalysisAgentFactory
{
    public static IMigrationAnalysisAgent Create()
    {
        var endpoint =
            Environment.GetEnvironmentVariable(
                "FOUNDRY_PROJECT_ENDPOINT");

        var model =
            Environment.GetEnvironmentVariable(
                "FOUNDRY_MODEL");

        var authenticationMode =
            Environment.GetEnvironmentVariable(
                "FOUNDRY_AUTH_MODE");

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException(
                "FOUNDRY_PROJECT_ENDPOINT is not configured.");
        }

        if (!Uri.TryCreate(
                endpoint,
                UriKind.Absolute,
                out var endpointUri))
        {
            throw new InvalidOperationException(
                "FOUNDRY_PROJECT_ENDPOINT is not a valid absolute URI.");
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException(
                "FOUNDRY_MODEL is not configured.");
        }

        if (!Enum.TryParse<FoundryAuthenticationMode>(
                authenticationMode,
                ignoreCase: true,
                out var mode))
        {
            throw new InvalidOperationException(
                "FOUNDRY_AUTH_MODE is not configured or is invalid.");
        }

        var managedIdentityClientId =
            Environment.GetEnvironmentVariable(
                "FOUNDRY_MANAGED_IDENTITY_CLIENT_ID");

        var credential =
            FoundryCredentialFactory.Create(
                mode,
                managedIdentityClientId);

        var projectClient = new AIProjectClient(
            endpointUri,
            credential);

        AIAgent agent = projectClient.AsAIAgent(
            model: model,
            name: "SpringMigrationStrategyAgent",
            instructions:
                """
                You are a read-only Spring modernisation strategy agent.

                Your role is to explain deterministic migration evidence
                supplied by the application and produce a clear migration
                strategy.

                You may:
                - explain the migration strategy;
                - explain migration wave rationale;
                - interpret repository dependencies;
                - explain migration readiness;
                - explain transformation outcomes when supplied;
                - interpret validation failures when supplied;
                - identify migration risks supported by the evidence;
                - recommend engineering investigation or preparation steps.

                You must not:
                - alter migration wave numbers;
                - move repositories between waves;
                - add or remove repositories;
                - change migration readiness decisions;
                - treat a blocked repository as ready;
                - claim an unexecuted transformation has completed;
                - claim a failed or unexecuted validation passed;
                - invent dependencies;
                - invent build results;
                - invent source changes;
                - invent validation diagnostics;
                - invent transformation results;
                - generate arbitrary shell, Git or Maven commands.

                Deterministic evidence supplied by the application is
                authoritative.

                Missing evidence must be described as unavailable or
                not yet executed rather than inferred.

                Keep all findings, risks and recommendations grounded
                in the supplied evidence.
                """);

        var modelAdapter =
            new AgentFrameworkMigrationAnalysisModel(
                agent);

        return new MigrationAnalysisAgent(
            modelAdapter);
    }
}
