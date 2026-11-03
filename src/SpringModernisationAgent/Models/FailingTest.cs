namespace SpringModernisationAgent.Models;

/// <summary>
/// One failing or erroring test from the Surefire XML reports, reduced to
/// what explains the failure.
/// </summary>
/// <param name="TestClass">Fully qualified test class.</param>
/// <param name="TestName">Test method name.</param>
/// <param name="Kind"><c>failure</c> (an assertion failed) or <c>error</c> (an exception was thrown).</param>
/// <param name="Type">Exception type reported by Surefire, if any.</param>
/// <param name="Message">First line of the failure message, if any.</param>
/// <param name="RootCause">The deepest <c>Caused by:</c> line, if the trace has one.</param>
/// <param name="Frames">Stack frames in the repository's own code, outermost framework frames removed.</param>
public sealed record FailingTest(
    string TestClass,
    string TestName,
    string Kind,
    string? Type,
    string? Message,
    string? RootCause,
    IReadOnlyList<string> Frames);
