using SpringModernisationAgent.AI;

namespace SpringModernisationAgent.Tests.AI;

public class MigrationAnalysisAgentFactoryTests
{
    [Fact]
    public void Create_FoundryEndpointMissing_ThrowsInvalidOperationException()
    {
        var originalEndpoint =
            Environment.GetEnvironmentVariable(
                "FOUNDRY_PROJECT_ENDPOINT");

        var originalModel =
            Environment.GetEnvironmentVariable(
                "FOUNDRY_MODEL");

        try
        {
            Environment.SetEnvironmentVariable(
                "FOUNDRY_PROJECT_ENDPOINT",
                null);

            Environment.SetEnvironmentVariable(
                "FOUNDRY_MODEL",
                "test-model");

            var exception =
                Assert.Throws<InvalidOperationException>(
                    MigrationAnalysisAgentFactory.Create);

            Assert.Equal(
                "FOUNDRY_PROJECT_ENDPOINT is not configured.",
                exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "FOUNDRY_PROJECT_ENDPOINT",
                originalEndpoint);

            Environment.SetEnvironmentVariable(
                "FOUNDRY_MODEL",
                originalModel);
        }
    }

    [Fact]
    public void Create_FoundryModelMissing_ThrowsInvalidOperationException()
    {
        var originalEndpoint =
            Environment.GetEnvironmentVariable(
                "FOUNDRY_PROJECT_ENDPOINT");

        var originalModel =
            Environment.GetEnvironmentVariable(
                "FOUNDRY_MODEL");

        try
        {
            Environment.SetEnvironmentVariable(
                "FOUNDRY_PROJECT_ENDPOINT",
                "https://example.test");

            Environment.SetEnvironmentVariable(
                "FOUNDRY_MODEL",
                null);

            var exception =
                Assert.Throws<InvalidOperationException>(
                    MigrationAnalysisAgentFactory.Create);

            Assert.Equal(
                "FOUNDRY_MODEL is not configured.",
                exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "FOUNDRY_PROJECT_ENDPOINT",
                originalEndpoint);

            Environment.SetEnvironmentVariable(
                "FOUNDRY_MODEL",
                originalModel);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    public void Create_BlankFoundryEndpoint_ThrowsInvalidOperationException(
        string endpoint)
    {
        var originalEndpoint =
            Environment.GetEnvironmentVariable(
                "FOUNDRY_PROJECT_ENDPOINT");

        var originalModel =
            Environment.GetEnvironmentVariable(
                "FOUNDRY_MODEL");

        try
        {
            Environment.SetEnvironmentVariable(
                "FOUNDRY_PROJECT_ENDPOINT",
                endpoint);

            Environment.SetEnvironmentVariable(
                "FOUNDRY_MODEL",
                "test-model");

            var exception =
                Assert.Throws<InvalidOperationException>(
                    MigrationAnalysisAgentFactory.Create);

            Assert.Equal(
                "FOUNDRY_PROJECT_ENDPOINT is not configured.",
                exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "FOUNDRY_PROJECT_ENDPOINT",
                originalEndpoint);

            Environment.SetEnvironmentVariable(
                "FOUNDRY_MODEL",
                originalModel);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    public void Create_BlankFoundryModel_ThrowsInvalidOperationException(
        string model)
    {
        var originalEndpoint =
            Environment.GetEnvironmentVariable(
                "FOUNDRY_PROJECT_ENDPOINT");

        var originalModel =
            Environment.GetEnvironmentVariable(
                "FOUNDRY_MODEL");

        try
        {
            Environment.SetEnvironmentVariable(
                "FOUNDRY_PROJECT_ENDPOINT",
                "https://example.test");

            Environment.SetEnvironmentVariable(
                "FOUNDRY_MODEL",
                model);

            var exception =
                Assert.Throws<InvalidOperationException>(
                    MigrationAnalysisAgentFactory.Create);

            Assert.Equal(
                "FOUNDRY_MODEL is not configured.",
                exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "FOUNDRY_PROJECT_ENDPOINT",
                originalEndpoint);

            Environment.SetEnvironmentVariable(
                "FOUNDRY_MODEL",
                originalModel);
        }
    }
}
