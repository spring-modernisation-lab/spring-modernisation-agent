namespace SpringModernisationAgent.Transformation;

/// <summary>
/// Live progress for long-running migration work, so a CI log shows what
/// each repository is doing while it happens rather than only at the end.
/// Progress is informational: nothing depends on it.
/// </summary>
public interface IMigrationProgress
{
    void Report(string repositoryName, string message);
}

/// <summary>Writes "HH:mm:ss repository: message" lines (UTC).</summary>
public sealed class ConsoleMigrationProgress : IMigrationProgress
{
    private readonly TextWriter _writer;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Lock _gate = new();

    public ConsoleMigrationProgress()
        : this(Console.Out, () => DateTimeOffset.UtcNow)
    {
    }

    public ConsoleMigrationProgress(
        TextWriter writer,
        Func<DateTimeOffset> clock)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(clock);

        _writer = writer;
        _clock = clock;
    }

    public void Report(
        string repositoryName,
        string message)
    {
        lock (_gate)
        {
            _writer.WriteLine($"{_clock():HH:mm:ss} {repositoryName}: {message}");
            _writer.Flush();
        }
    }
}

/// <summary>Reports nothing; the default when no progress is wanted.</summary>
public sealed class NullMigrationProgress : IMigrationProgress
{
    public static readonly NullMigrationProgress Instance = new();

    private NullMigrationProgress()
    {
    }

    public void Report(string repositoryName, string message)
    {
    }
}
