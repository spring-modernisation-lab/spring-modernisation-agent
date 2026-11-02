namespace SpringModernisationAgent.AI;

public enum FoundryAuthenticationMode
{
    // Local developer machine: DefaultAzureCredential chain (e.g. az login).
    Development,

    // CI: the Azure CLI session created by GitHub OIDC (azure/login), with
    // no long-lived secret. Exactly this credential, not a chain.
    AzureCli,

    // Production hosting: system- or user-assigned managed identity.
    ManagedIdentity
}
