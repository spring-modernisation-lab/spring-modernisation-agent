using Azure.Identity;
using SpringModernisationAgent.AI;

namespace SpringModernisationAgent.Tests.AI;

public class FoundryCredentialFactoryTests
{
    [Fact]
    public void Create_Development_ReturnsDefaultAzureCredential()
    {
        var credential = FoundryCredentialFactory.Create(
            FoundryAuthenticationMode.Development);

        Assert.IsType<DefaultAzureCredential>(credential);
    }

    [Fact]
    public void Create_AzureCli_ReturnsAzureCliCredentialOnly()
    {
        // CI signs in with GitHub OIDC (azure/login), which leaves an Azure
        // CLI session. Using exactly that credential, instead of a chain,
        // means CI cannot silently authenticate as some other identity.
        var credential = FoundryCredentialFactory.Create(
            FoundryAuthenticationMode.AzureCli);

        Assert.IsType<AzureCliCredential>(credential);
    }

    [Fact]
    public void Create_ManagedIdentity_ReturnsManagedIdentityCredential()
    {
        var credential = FoundryCredentialFactory.Create(
            FoundryAuthenticationMode.ManagedIdentity);

        Assert.IsType<ManagedIdentityCredential>(credential);
    }

    [Theory]
    [InlineData("AzureCli", FoundryAuthenticationMode.AzureCli)]
    [InlineData("azurecli", FoundryAuthenticationMode.AzureCli)]
    [InlineData("Development", FoundryAuthenticationMode.Development)]
    [InlineData("ManagedIdentity", FoundryAuthenticationMode.ManagedIdentity)]
    public void AuthenticationMode_ParsesConfiguredValues(
        string configuredValue,
        FoundryAuthenticationMode expected)
    {
        Assert.True(
            Enum.TryParse<FoundryAuthenticationMode>(
                configuredValue,
                ignoreCase: true,
                out var mode));

        Assert.Equal(expected, mode);
    }
}
