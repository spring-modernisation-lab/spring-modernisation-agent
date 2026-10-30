using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Tests.Models;

public class TransformationExecutionResultTests
{
    private const string CommitSha =
        "078718f4100ea1340e9a175c13c50bbf71806ae3";

    [Fact]
    public void Succeeded_TransformationSucceededAndValidationPassed_ReturnsTrue()
    {
        var result = new TransformationExecutionResult(
            Transformation: CreateTransformationResult(
                TransformationStatus.Succeeded),
            Validation: CreateValidationResult(
                TransformationValidationStatus.Passed));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Succeeded_TransformationFailedAndValidationPassed_ReturnsFalse()
    {
        var result = new TransformationExecutionResult(
            Transformation: CreateTransformationResult(
                TransformationStatus.Failed),
            Validation: CreateValidationResult(
                TransformationValidationStatus.Passed));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Succeeded_TransformationSucceededAndValidationFailed_ReturnsFalse()
    {
        var result = new TransformationExecutionResult(
            Transformation: CreateTransformationResult(
                TransformationStatus.Succeeded),
            Validation: CreateValidationResult(
                TransformationValidationStatus.Failed));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Succeeded_TransformationNotRun_ReturnsFalse()
    {
        var result = new TransformationExecutionResult(
            Transformation: CreateTransformationResult(
                TransformationStatus.NotRun),
            Validation: CreateValidationResult(
                TransformationValidationStatus.NotRun));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Succeeded_TransformationSucceededButValidationNotRun_ReturnsFalse()
    {
        var result = new TransformationExecutionResult(
            Transformation: CreateTransformationResult(
                TransformationStatus.Succeeded),
            Validation: CreateValidationResult(
                TransformationValidationStatus.NotRun));

        Assert.False(result.Succeeded);
    }

    private static TransformationResult CreateTransformationResult(
        TransformationStatus status)
    {
        return new TransformationResult(
            RepositoryName: "customer-service",
            SourceCommitSha: CommitSha,
            Status: status,
            AppliedRecipes: [],
            ChangedFiles: [],
            StandardOutput: null,
            StandardError: null);
    }

    private static TransformationValidationResult CreateValidationResult(
        TransformationValidationStatus status)
    {
        return new TransformationValidationResult(
            RepositoryName: "customer-service",
            SourceCommitSha: CommitSha,
            Status: status,
            Checks: []);
    }
}