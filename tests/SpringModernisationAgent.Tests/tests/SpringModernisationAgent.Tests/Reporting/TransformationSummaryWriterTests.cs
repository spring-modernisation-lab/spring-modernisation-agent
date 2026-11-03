using SpringModernisationAgent.Models;
using SpringModernisationAgent.Remediation;
using SpringModernisationAgent.Reporting;

namespace SpringModernisationAgent.Tests.Reporting;

public class TransformationSummaryWriterTests
{
    private const string Sha = "b39866542351f9c79f05df7558e84aa9799ef33f";

    [Fact]
    public void Render_ListsEveryRepositoryWithItsOutcome()
    {
        var markdown = TransformationSummaryWriter.Render(
        [
            Result("platform-parent", passed: true, changedFiles: 2),
            Result("orders-service", passed: false, changedFiles: 7)
        ]);

        Assert.Contains("## Migration wave results", markdown);
        Assert.Contains("| Repository | OpenRewrite | Validation | Remediation | Outcome |", markdown);
        Assert.Contains("| platform-parent | 2 files changed | Passed | not enabled | ✅ Succeeded |", markdown);
        Assert.Contains("| orders-service | 7 files changed | Failed (compile) | not enabled | ❌ Failed |", markdown);
    }

    [Fact]
    public void Render_RemediationAttempts_ShowWhatEachAttemptDid()
    {
        var edits = new[]
        {
            new AppliedEdit("src/main/java/a/InventoryClientConfiguration.java", RemediationEditKind.WholeFileRewrite, 5),
            new AppliedEdit("src/main/java/a/MoneyUserType.java", RemediationEditKind.SearchReplace, 6)
        };

        var attempts = new[]
        {
            Attempt(1, "compile", applied: true, rejection: null, edits, passed: false, inputTokens: 16230, outputTokens: 3679),
            Attempt(2, "compile", applied: false, rejection: "edit 1 ('x'): the search | text was not found", [], passed: null, inputTokens: 900, outputTokens: 80)
        };

        var result = Result("orders-service", passed: false, changedFiles: 7) with
        {
            Remediation = new RemediationReport(RemediationOutcome.AttemptsExhausted, attempts)
        };

        var markdown = TransformationSummaryWriter.Render([result]);

        Assert.Contains("| orders-service | 7 files changed | Failed (compile) | AttemptsExhausted (2 attempts) | ❌ Failed |", markdown);
        Assert.Contains("### orders-service: remediation", markdown);
        Assert.Contains("| # | Check | Decision | Edits | Guardrails | Validation after | Tool calls | Tokens in/out |", markdown);

        Assert.Contains(
            "| 1 | compile | Fix | InventoryClientConfiguration.java (whole file, 5 lines); MoneyUserType.java (search/replace, 6 lines) | accepted | Failed (compile) | 3 (1 previews) | 16,230 / 3,679 |",
            markdown);

        // Pipes in free text must not break the table.
        Assert.Contains("rejected: edit 1 ('x'): the search \\| text was not found", markdown);
    }

    private static TransformationExecutionResult Result(
        string repository,
        bool passed,
        int changedFiles)
    {
        return new TransformationExecutionResult(
            new TransformationResult(
                repository,
                Sha,
                TransformationStatus.Succeeded,
                [],
                Enumerable.Range(0, changedFiles).Select(index => $"file{index}").ToList(),
                null,
                null),
            Validation(repository, passed));
    }

    private static TransformationValidationResult Validation(
        string repository,
        bool passed) =>
        new(
            repository,
            Sha,
            passed ? TransformationValidationStatus.Passed : TransformationValidationStatus.Failed,
            [new TransformationValidationCheckResult(passed ? "effective-pom" : "compile", passed, null, null)]);

    private static RemediationAttemptRecord Attempt(
        int number,
        string check,
        bool applied,
        string? rejection,
        IReadOnlyList<AppliedEdit> edits,
        bool? passed,
        long inputTokens,
        long outputTokens)
    {
        return new RemediationAttemptRecord(
            Number: number,
            FailedCheck: check,
            Proposal: new RemediationProposal(RemediationDecision.Fix, "d", "r", [], []),
            Applied: applied,
            RejectionReason: rejection,
            Error: null,
            Patch: null,
            Validation: passed is null ? null : Validation("orders-service", passed.Value),
            ToolCalls:
            [
                new RemediationToolCall("read_file", "a", true),
                new RemediationToolCall("search_source", "b", true),
                new RemediationToolCall("preview_edits", "2 edits", true)
            ],
            InputTokens: inputTokens,
            OutputTokens: outputTokens)
        {
            AppliedEdits = edits
        };
    }
}
