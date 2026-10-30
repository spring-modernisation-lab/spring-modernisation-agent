using SpringModernisationAgent.Transformation;

namespace SpringModernisationAgent.Tests.Transformation;

public class TransformationValidationMappingTests
{
    [Fact]
    public void GetMavenArguments_Compile_ReturnsCompileCommand()
    {
        var result =
            TransformationValidationMapping.GetMavenArguments(
                "compile");

        Assert.Equal(
            [
                "--batch-mode",
                "compile"
            ],
            result);
    }

    [Fact]
    public void GetMavenArguments_UnitTests_ReturnsTestCommand()
    {
        var result =
            TransformationValidationMapping.GetMavenArguments(
                "unit-tests");

        Assert.Equal(
            [
                "--batch-mode",
                "test"
            ],
            result);
    }

    [Fact]
    public void GetMavenArguments_ApplicationContextTest_ReturnsTestCommand()
    {
        var result =
            TransformationValidationMapping.GetMavenArguments(
                "application-context-test");

        Assert.Equal(
            [
                "--batch-mode",
                "test"
            ],
            result);
    }

    [Fact]
    public void GetMavenArguments_EffectivePom_ReturnsEffectivePomCommand()
    {
        var result =
            TransformationValidationMapping.GetMavenArguments(
                "effective-pom");

        Assert.Equal(
            [
                "--batch-mode",
                "help:effective-pom"
            ],
            result);
    }

    [Fact]
    public void GetMavenArguments_DependencyResolution_ReturnsDependencyResolveCommand()
    {
        var result =
            TransformationValidationMapping.GetMavenArguments(
                "dependency-resolution");

        Assert.Equal(
            [
                "--batch-mode",
                "dependency:resolve"
            ],
            result);
    }

    [Theory]
    [InlineData("deploy")]
    [InlineData("install")]
    [InlineData("clean")]
    [InlineData("arbitrary-command")]
    public void GetMavenArguments_UnsupportedCheck_ThrowsInvalidOperationException(
        string check)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => TransformationValidationMapping.GetMavenArguments(
                check));

        Assert.Equal(
            $"Unsupported transformation validation check '{check}'.",
            exception.Message);
    }

    [Fact]
    public void GetMavenArguments_EmptyCheck_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(
            () => TransformationValidationMapping.GetMavenArguments(
                string.Empty));
    }
}