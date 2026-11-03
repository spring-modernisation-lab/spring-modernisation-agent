using SpringModernisationAgent.Models;
using SpringModernisationAgent.Transformation;
using SpringModernisationAgent.Workspace;

namespace SpringModernisationAgent.Remediation;

/// <summary>
/// Bounded remediation: ask the agent, apply its proposal under the
/// guardrails, record the attempt's patch, and re-run the same fixed
/// validation checks. It stops when validation passes, the agent declines,
/// an attempt makes no progress, the infrastructure fails, or the attempt
/// limit is reached. Success is decided only by the final Maven result.
/// </summary>
public sealed class RemediationLoop : IRemediationLoop
{
    public const int DefaultMaxAttempts = 3;

    private const int FeedbackLines = 8;

    private readonly IRemediationAgent _agent;
    private readonly RemediationPatchApplier _applier;
    private readonly IWorkspacePatchRecorder _patchRecorder;
    private readonly TransformationValidator _validator;
    private readonly int _maxAttempts;
    private readonly IMigrationProgress _progress;
    private readonly IJavaClassInspectorFactory? _classInspectorFactory;

    public RemediationLoop(
        IRemediationAgent agent,
        RemediationPatchApplier applier,
        IWorkspacePatchRecorder patchRecorder,
        TransformationValidator validator,
        int maxAttempts = DefaultMaxAttempts,
        IMigrationProgress? progress = null,
        IJavaClassInspectorFactory? classInspectorFactory = null)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(applier);
        ArgumentNullException.ThrowIfNull(patchRecorder);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);

        _agent = agent;
        _applier = applier;
        _patchRecorder = patchRecorder;
        _validator = validator;
        _maxAttempts = maxAttempts;
        _progress = progress ?? NullMigrationProgress.Instance;
        _classInspectorFactory = classInspectorFactory;
    }

    public async Task<RemediationLoopResult> RunAsync(
        TransformationPlan plan,
        RepositoryWorkspace workspace,
        string openRewriteTreeId,
        string openRewritePatchPath,
        TransformationValidationResult validation,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAttemptsAsync(
            plan,
            workspace,
            openRewriteTreeId,
            openRewritePatchPath,
            validation,
            cancellationToken);

        var attempts = result.Report.Attempts.Count;

        _progress.Report(
            plan.RepositoryName,
            $"remediation finished: {result.Report.Outcome} after {attempts} attempt{(attempts == 1 ? "" : "s")}");

        return result;
    }

    private async Task<RemediationLoopResult> RunAttemptsAsync(
        TransformationPlan plan,
        RepositoryWorkspace workspace,
        string openRewriteTreeId,
        string openRewritePatchPath,
        TransformationValidationResult validation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(openRewriteTreeId);
        ArgumentNullException.ThrowIfNull(validation);

        var run = new Run(validation, openRewriteTreeId);

        if (validation.Passed)
        {
            return run.Finish(RemediationOutcome.NotNeeded);
        }

        var openRewritePatch = File.Exists(openRewritePatchPath)
            ? await File.ReadAllTextAsync(openRewritePatchPath, cancellationToken)
            : string.Empty;

        // Resolved once: the dependencies do not change between attempts.
        var classInspector = await CreateClassInspectorAsync(plan, workspace, cancellationToken);

        for (var number = 1; number <= _maxAttempts; number++)
        {
            var failedCheck = run.Validation.Checks.FirstOrDefault(check => !check.Passed);

            if (failedCheck is null)
            {
                // Validation failed without a failed check (e.g. it did not run).
                return run.Finish(RemediationOutcome.Error);
            }

            var diagnostics = ValidationDiagnosticsExtractor.Extract(failedCheck);

            var tools = new RemediationWorkspaceTools(
                workspace.RepositoryPath,
                openRewritePatch,
                diagnostics,
                classInspector: classInspector);

            var request = new RemediationRequest(
                RepositoryName: plan.RepositoryName,
                SpringBootVersion: plan.Target.SpringBootVersion,
                JavaVersion: plan.Target.JavaVersion,
                FailedCheck: failedCheck.Check,
                Diagnostics: diagnostics,
                PreviousAttempts: run.Feedback.ToList());

            var attempt = new RemediationAttemptRecord(
                Number: number,
                FailedCheck: failedCheck.Check,
                Proposal: null,
                Applied: false,
                RejectionReason: null,
                Error: null,
                Patch: null,
                Validation: null,
                ToolCalls: [],
                InputTokens: null,
                OutputTokens: null);

            var repository = plan.RepositoryName;

            _progress.Report(
                repository,
                $"remediation attempt {number}/{_maxAttempts}: asking the agent " +
                $"({failedCheck.Check}, {diagnostics.Count} diagnostic lines)");

            RemediationAgentResult proposed;

            try
            {
                proposed = await _agent.ProposeAsync(request, tools.AsAITools(), cancellationToken);
            }
            catch (InvalidRemediationProposalException exception)
            {
                _progress.Report(repository, $"attempt {number}: invalid proposal: {exception.Message}");
                run.Add(attempt with { Error = exception.Message, ToolCalls = tools.Calls });
                run.Feedback.Add(new RemediationAttemptSummary(number, "Invalid proposal", exception.Message));
                continue;
            }
            catch (Exception exception)
                when (exception is not OperationCanceledException)
            {
                _progress.Report(repository, $"attempt {number}: error: {exception.Message}");
                run.Add(attempt with { Error = exception.Message, ToolCalls = tools.Calls });
                return run.Finish(RemediationOutcome.Error);
            }

            var calls = tools.Calls;

            _progress.Report(
                repository,
                $"attempt {number}: {proposed.Proposal.Decision}; {calls.Count} tool calls " +
                $"({calls.Count(call => call.Tool == "preview_edits")} previews); " +
                $"tokens {proposed.InputTokens?.ToString() ?? "?"}/{proposed.OutputTokens?.ToString() ?? "?"}");

            attempt = attempt with
            {
                Proposal = proposed.Proposal,
                ToolCalls = tools.Calls,
                InputTokens = proposed.InputTokens,
                OutputTokens = proposed.OutputTokens
            };

            switch (proposed.Proposal.Decision)
            {
                case RemediationDecision.CannotFix:
                    run.Add(attempt);
                    return run.Finish(RemediationOutcome.CannotFix);

                case RemediationDecision.NeedsHumanDecision:
                    run.Add(attempt);
                    return run.Finish(RemediationOutcome.NeedsHumanDecision);
            }

            var applied = _applier.Apply(workspace.RepositoryPath, proposed.Proposal.Edits);

            if (!applied.Applied)
            {
                _progress.Report(repository, $"attempt {number}: guardrails rejected: {applied.RejectionReason}");
                run.Add(attempt with { RejectionReason = applied.RejectionReason });
                run.Feedback.Add(new RemediationAttemptSummary(
                    number,
                    "Rejected by the guardrails; nothing was changed",
                    applied.RejectionReason ?? string.Empty));
                continue;
            }

            _progress.Report(
                repository,
                $"attempt {number}: guardrails accepted {DescribeEdits(applied.AppliedEdits)}; re-validating");

            RecordedPatch patch;

            try
            {
                patch = await _patchRecorder.RecordAsync(
                    workspace.RepositoryPath,
                    workspace.RepositoryName,
                    run.TreeId,
                    $"{number + 1:D2}-remediation-attempt-{number}",
                    cancellationToken);
            }
            catch (Exception exception)
                when (exception is not OperationCanceledException)
            {
                // Without the patch the change cannot be attributed or reviewed.
                run.Add(attempt with
                {
                    Applied = true,
                    AppliedEdits = applied.AppliedEdits,
                    Error = $"Patch recording failed: {exception.Message}"
                });
                return run.Finish(RemediationOutcome.Error);
            }

            run.TreeId = patch.TreeId;
            run.Patches.Add(patch.Artifact);

            var next = await _validator.ValidateAsync(plan, workspace.RepositoryPath, cancellationToken);

            run.Add(attempt with
            {
                Applied = true,
                AppliedEdits = applied.AppliedEdits,
                Patch = patch.Artifact,
                Validation = next
            });
            run.Validation = next;

            if (next.Passed)
            {
                return run.Finish(RemediationOutcome.Fixed);
            }

            var nextFailedCheck = next.Checks.FirstOrDefault(check => !check.Passed);

            if (nextFailedCheck is null)
            {
                return run.Finish(RemediationOutcome.Error);
            }

            var nextDiagnostics = ValidationDiagnosticsExtractor.Extract(nextFailedCheck);

            if (nextFailedCheck.Check == failedCheck.Check
                && nextDiagnostics.SequenceEqual(diagnostics, StringComparer.Ordinal))
            {
                return run.Finish(RemediationOutcome.NoProgress);
            }

            run.Feedback.Add(new RemediationAttemptSummary(
                number,
                "Applied, but validation still fails",
                $"Check '{nextFailedCheck.Check}' now fails with: " +
                string.Join(" | ", nextDiagnostics.Take(FeedbackLines))));
        }

        return run.Finish(RemediationOutcome.AttemptsExhausted);
    }

    /// <summary>
    /// Class descriptions are a help, not a requirement: if the classpath
    /// cannot be resolved the agent works without them.
    /// </summary>
    private async Task<IJavaClassInspector?> CreateClassInspectorAsync(
        TransformationPlan plan,
        RepositoryWorkspace workspace,
        CancellationToken cancellationToken)
    {
        if (_classInspectorFactory is null)
        {
            return null;
        }

        IJavaClassInspector? inspector;

        try
        {
            inspector = await _classInspectorFactory.CreateAsync(workspace.RepositoryPath, cancellationToken);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            inspector = null;
        }

        _progress.Report(
            plan.RepositoryName,
            inspector is null
                ? "class descriptions: unavailable (dependency classpath could not be resolved)"
                : "class descriptions: available from the dependency classpath");

        return inspector;
    }

    // "3 edits (2 whole-file rewrites, 1 search and replace)"
    private static string DescribeEdits(
        IReadOnlyList<AppliedEdit> edits)
    {
        var kinds = edits
            .GroupBy(edit => edit.Kind)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var count = group.Count();

                return group.Key switch
                {
                    RemediationEditKind.WholeFileRewrite => $"{count} whole-file rewrite{(count == 1 ? "" : "s")}",
                    RemediationEditKind.NewFile => $"{count} new file{(count == 1 ? "" : "s")}",
                    _ => $"{count} search and replace"
                };
            });

        return $"{edits.Count} edit{(edits.Count == 1 ? "" : "s")} ({string.Join(", ", kinds)})";
    }

    /// <summary>The mutable state of one remediation run.</summary>
    private sealed class Run(
        TransformationValidationResult validation,
        string treeId)
    {
        private readonly List<RemediationAttemptRecord> _attempts = [];

        public TransformationValidationResult Validation { get; set; } = validation;

        public string TreeId { get; set; } = treeId;

        public List<PatchArtifact> Patches { get; } = [];

        public List<RemediationAttemptSummary> Feedback { get; } = [];

        public void Add(RemediationAttemptRecord attempt) => _attempts.Add(attempt);

        public RemediationLoopResult Finish(RemediationOutcome outcome) =>
            new(new RemediationReport(outcome, _attempts.ToList()), Validation, Patches.ToList());
    }
}
