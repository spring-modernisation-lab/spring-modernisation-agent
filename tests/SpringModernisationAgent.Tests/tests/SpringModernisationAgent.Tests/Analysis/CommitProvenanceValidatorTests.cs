using SpringModernisationAgent.Analysis;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Tests.Analysis;

public class CommitProvenanceValidatorTests
{
    [Fact]
    public void Validate_MatchingCommitShas_ReturnsMatched()
    {
        var validator = new CommitProvenanceValidator();

        var result = validator.Validate(
            "abc123",
            "abc123");

        Assert.Equal(
            CommitProvenanceStatus.Matched,
            result.Status);

        Assert.True(result.IsValid);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void Validate_DifferentCommitShas_ReturnsMismatched()
    {
        var validator = new CommitProvenanceValidator();

        var result = validator.Validate(
            "abc123",
            "def456");

        Assert.Equal(
            CommitProvenanceStatus.Mismatched,
            result.Status);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void Validate_MissingAssessedCommit_ReturnsMissing()
    {
        var validator = new CommitProvenanceValidator();

        var result = validator.Validate(
            null,
            "abc123");

        Assert.Equal(
            CommitProvenanceStatus.Missing,
            result.Status);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_MissingBuiltCommit_ReturnsMissing()
    {
        var validator = new CommitProvenanceValidator();

        var result = validator.Validate(
            "abc123",
            null);

        Assert.Equal(
            CommitProvenanceStatus.Missing,
            result.Status);

        Assert.False(result.IsValid);
    }
}