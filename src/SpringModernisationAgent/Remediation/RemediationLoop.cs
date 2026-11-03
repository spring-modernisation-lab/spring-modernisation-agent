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

    public RemediationLoop(
        IRemediationAgent agent,
        RemediationPatchApplier applier,
        IWorkspacePatchRecorder patchRecorder,
        TransformationValidator validator,
        int maxAttempts = DefaultMaxAttempts)
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
    }

    public async Task<RemediationLoopResult> RunAsync(
        TransformationPlan plan,
        RepositoryWorkspace workspace,
        string openRewriteTreeId,
        string openRewritePatchPath,
        TransformationValidationResult validation,
        CancellationToken cancellationToken = default)
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
                diagnostics);

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

            RemediationAgentResult proposed;

            try
            {
                proposed = await _agent.ProposeAsync(request, tools.AsAITools(), cancellationToken);
            }
            catch (InvalidRemediationProposalException exception)
            {
                run.Add(attempt with { Error = exception.Message, ToolCalls = tools.Calls });
                run.Feedback.Add(new RemediationAttemptSummary(number, "Invalid proposal", exception.Message));
                continue;
            }
            catch (Exception exception)
                when (exception is not OperationCanceledException)
            {
                run.Add(attempt with { Error = exception.Message, ToolCalls = tools.Calls });
                return run.Finish(RemediationOutcome.Error);
            }

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
                run.Add(attempt with { RejectionReason = applied.RejectionReason });
                run.Feedback.Add(new RemediationAttemptSummary(
                    number,
                    "Rejected by the guardrails; nothing was changed",
                    applied.RejectionReason ?? string.Empty));
                continue;
            }

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
