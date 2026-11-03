namespace SpringModernisationAgent.Remediation;

public static class RemediationSettings
{
    public const int MaxAllowedAttempts = 5;

    /// <summary>
    /// Reads MIGRATION_REMEDIATION_MAX_ATTEMPTS: unset means the default;
    /// anything outside 1-5 is a configuration error rather than silently
    /// running more (or fewer) AI attempts than intended.
    /// </summary>
    public static int ParseMaxAttempts(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return RemediationLoop.DefaultMaxAttempts;
        }

        if (int.TryParse(value.Trim(), out var attempts)
            && attempts is >= 1 and <= MaxAllowedAttempts)
        {
            return attempts;
        }

        throw new InvalidOperationException(
            $"MIGRATION_REMEDIATION_MAX_ATTEMPTS must be a whole number between 1 and {MaxAllowedAttempts}; " +
            $"it is '{value}'.");
    }
}
