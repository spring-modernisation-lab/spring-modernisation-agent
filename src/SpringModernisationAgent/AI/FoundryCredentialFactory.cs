using Azure.Core;
using Azure.Identity;

namespace SpringModernisationAgent.AI;

public static class FoundryCredentialFactory
{
    public static TokenCredential Create(
        FoundryAuthenticationMode mode,
        string? managedIdentityClientId = null)
    {
        return mode switch
        {
            FoundryAuthenticationMode.Development =>
                new DefaultAzureCredential(),

            FoundryAuthenticationMode.ManagedIdentity =>
                CreateManagedIdentityCredential(
                    managedIdentityClientId),

            _ => throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "Unsupported Foundry authentication mode.")
        };
    }

    private static TokenCredential CreateManagedIdentityCredential(
        string? clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return new ManagedIdentityCredential(
                ManagedIdentityId.SystemAssigned);
        }

        return new ManagedIdentityCredential(
            ManagedIdentityId.FromUserAssignedClientId(
                clientId));
    }
}
