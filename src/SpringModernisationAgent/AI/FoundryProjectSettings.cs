using Azure.AI.Projects;
using Azure.Core;

namespace SpringModernisationAgent.AI;

/// <summary>
/// Foundry connection settings shared by every agent, read from
/// FOUNDRY_PROJECT_ENDPOINT, FOUNDRY_MODEL, FOUNDRY_AUTH_MODE and
/// FOUNDRY_MANAGED_IDENTITY_CLIENT_ID.
/// </summary>
public sealed record FoundryProjectSettings(
    Uri Endpoint,
    string Model,
    TokenCredential Credential)
{
    public AIProjectClient CreateProjectClient() =>
        new(Endpoint, Credential);

    public static FoundryProjectSettings FromEnvironment()
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

        return new FoundryProjectSettings(
            endpointUri,
            model,
            credential);
    }
}
