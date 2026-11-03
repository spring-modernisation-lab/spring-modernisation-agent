using SpringModernisationAgent.Remediation;

namespace SpringModernisationAgent.Tests.Remediation;

public class RemediationSettingsTests
{
    [Theory]
    [InlineData(null, 3)]
    [InlineData("", 3)]
    [InlineData("1", 1)]
    [InlineData("5", 5)]
    [InlineData(" 4 ", 4)]
    public void ParseMaxAttempts_ValidOrMissing_ReturnsTheValueOrTheDefault(
        string? value,
        int expected)
    {
        Assert.Equal(expected, RemediationSettings.ParseMaxAttempts(value));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("6")]
    [InlineData("-1")]
    [InlineData("three")]
    public void ParseMaxAttempts_OutOfRangeOrInvalid_Throws(
        string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => RemediationSettings.ParseMaxAttempts(value));

        Assert.Contains("between 1 and 5", exception.Message);
    }
}
