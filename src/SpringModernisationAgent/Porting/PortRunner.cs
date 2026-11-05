using SpringModernisationAgent.Behaviour;
using SpringModernisationAgent.Models;
using SpringModernisationAgent.Remediation;
using SpringModernisationAgent.Transformation;
using SpringModernisationAgent.Triage;
using SpringModernisationAgent.Workspace;

namespace SpringModernisationAgent.Porting;

/// <param name="Validation">The failed validation after OpenRewrite.</param>
/// <param name="Problems">Its classified problems.</param>
/// <param name="UnsupportedLibraries">Libraries the repository uses that the platform dropped (for classification).</param>
/// <param name="Baseline">The behaviour baseline, if one was recorded: the contract gate checks obligations against it.</param>
public sealed record PortRunInput(
    TransformationPlan Plan,
    RepositoryWorkspace Workspace,
    string OpenRewriteTreeId,
    string OpenRewritePatchPath,
    TransformationValidationResult Validation,
    IReadOnlyList<ClassifiedProblem> Problems,
    IReadOnlyCollection<string> UnsupportedLibraries,
    BehaviourReport? Baseline)
{
    /// <summary>A person's recorded choices (docs/system-design.md §12.1), already validated against the playbooks.</summary>
    public IReadOnlyList<DecisionRecord> Decisions { get; init; } = [];
}

/// <param name="FinalValidation">Maven's last result, which decides success.</param>
/// <param name="AttemptPatches">Every recorded attempt patch, in order, kept or reverted.</param>
/// <param name="Behaviour">The contract gate's comparison, when nothing changed after it; otherwise null.</param>
/// <param name="Problems">The final classification.</param>
public sealed record PortRunResult(
    RemediationReport Report,
    TransformationValidationResult FinalValidation,
    IReadOnlyList<PatchArtifact> AttemptPatches,
    BehaviourReport? Behaviour,
    IReadOnlyList<ClassifiedProblem> Problems);

public interface IPortRunner
{
    Task<PortRunResult> RunAsync(
        PortRunInput input,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Scoped ports in place of the open loop (docs/system-design.md §4.1).
/// Known API moves are worked one task at a time: an attempt is kept only if
/// the task's errors are gone and nothing new fails; otherwise it is
/// reverted and repaired once. Once everything is worked, one behaviour
/// comparison checks every kept port's obligations; a port that broke one
/// is repaired once more, then reverted. Everything else goes to a person.
/// </summary>
public sealed class PortRunner : IPortRunner
{
    private readonly IPortAgent _agent;
    private readonly RemediationPatchApplier _applier;
    private readonly IWorkspacePatchRecorder _recorder;
    private readonly IWorkspaceRestorer _restorer;
    private readonly ITransformationValidator _validator;
    private readonly ProblemKnowledge _knowledge;
    private readonly IBehaviourContractRecorder? _behaviour;
    private readonly IMigrationProgress _progress;
    private readonly IJavaClassInspectorFactory? _classInspectorFactory;

    public PortRunner(
        IPortAgent agent,
        RemediationPatchApplier applier,
        IWorkspacePatchRecorder recorder,
        IWorkspaceRestorer restorer,
        ITransformationValidator validator,
        ProblemKnowledge knowledge,
        IBehaviourContractRecorder? behaviour = null,
        IMigrationProgress? progress = null,
        IJavaClassInspectorFactory? classInspectorFactory = null)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(applier);
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(restorer);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(knowledge);

        _agent = agent;
        _applier = applier;
        _recorder = recorder;
        _restorer = restorer;
        _validator = validator;
        _knowledge = knowledge;
        _behaviour = behaviour;
        _progress = progress ?? NullMigrationProgress.Instance;
        _classInspectorFactory = classInspectorFactory;
    }

    public async Task<PortRunResult> RunAsync(
        PortRunInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var run = new Run(input);

        if (input.Validation.Passed)
        {
            return run.Finish(RemediationOutcome.NotNeeded);
        }

        var openRewritePatch = File.Exists(input.OpenRewritePatchPath)
            ? await File.ReadAllTextAsync(input.OpenRewritePatchPath, cancellationToken)
            : string.Empty;

        var context = new AttemptContext(
            input,
            openRewritePatch,
            await CreateClassInspectorAsync(input, cancellationToken));

        try
        {
            await PortAsync(run, context, cancellationToken);
            await DecisionsAsync(run, context, cancellationToken);
            await ContractGateAsync(run, context, cancellationToken);
        }
        catch (PortInfrastructureException exception)
        {
            _progress.Report(input.Plan.RepositoryName, $"ports stopped: {exception.Message}");
            return run.Finish(RemediationOutcome.Error);
        }

        var outcome = run.Validation.Passed ? RemediationOutcome.Fixed : RemediationOutcome.NeedsHumanDecision;

        _progress.Report(
            input.Plan.RepositoryName,
            $"ports finished: {string.Join(", ", run.Tasks.Values.Select(task => $"{task.Record.Mapping} {task.Record.Status}"))}");

        return run.Finish(outcome);
    }

    /// <summary>Every known API move, one task at a time, in order.</summary>
    private async Task PortAsync(
        Run run,
        AttemptContext context,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var plan = PortPlanner.Plan(run.Problems, _knowledge, run.Tasks.Keys);
            run.Left = plan.Unworkable;

            if (plan.Tasks.Count == 0)
            {
                return;
            }

            await WorkAsync(run, context, plan.Tasks[0], cancellationToken);
        }
    }

    /// <summary>
    /// The recorded decisions that are ports (docs/system-design.md §12.1),
    /// worked like any other task once the API moves are done, so the test
    /// they answer has been reached. A decision with nothing to port says why.
    /// </summary>
    private async Task DecisionsAsync(
        Run run,
        AttemptContext context,
        CancellationToken cancellationToken)
    {
        var input = context.Input;

        if (input.Decisions.Count == 0)
        {
            return;
        }

        var plan = DecisionTasks.Plan(input.Decisions, input.Baseline?.Baseline, input.Workspace.RepositoryPath, run.Problems);
        run.DecisionNotes.AddRange(plan.Notes);

        foreach (var task in plan.Tasks)
        {
            await WorkAsync(run, context, task, cancellationToken);
        }
    }

    /// <summary>One attempt at a task, one repair if it was not kept, and how it ended.</summary>
    private async Task WorkAsync(
        Run run,
        AttemptContext context,
        PortTask task,
        CancellationToken cancellationToken)
    {
        var state = run.Start(task);

        _progress.Report(
            context.Input.Plan.RepositoryName,
            $"port {task.Id}: {task.Files.Count} file(s), {task.Problems.Count} problem(s)");

        var first = await AttemptAsync(run, context, task, null, cancellationToken);

        var final = first.Kind == AttemptKind.NotKept
            ? await AttemptAsync(run, context, task, new PortRepair(first.Reason!, first.Feedback), cancellationToken)
            : first;

        state.Record = state.Record with
        {
            Status = final.Kind switch
            {
                AttemptKind.Kept => PortTaskStatus.Accepted,
                AttemptKind.CannotFix => PortTaskStatus.CannotFix,
                _ => PortTaskStatus.Reverted
            },
            Reason = final.Kind == AttemptKind.Kept ? null : final.Reason
        };
    }

    /// <summary>
    /// Once everything is worked: one behaviour comparison checks every kept
    /// port's obligations. A port that broke one gets one repair; if it still
    /// breaks one, its files go back to how the recipes left them.
    /// </summary>
    private async Task ContractGateAsync(
        Run run,
        AttemptContext context,
        CancellationToken cancellationToken)
    {
        var baseline = context.Input.Baseline;
        var accepted = run.Tasks.Values.Where(task => task.Record.Status == PortTaskStatus.Accepted).ToList();

        if (_behaviour is null || baseline?.Baseline is null || accepted.Count == 0)
        {
            return;
        }

        var broken = await CheckObligationsAsync(run, context, accepted, cancellationToken);

        if (broken.Count == 0)
        {
            return;
        }

        var repaired = new List<TaskState>();

        foreach (var (state, result) in broken)
        {
            var repair = new PortRepair(
                "it changed behaviour the task must keep",
                result.Broken.Select(Describe).ToList());

            var attempt = await AttemptAsync(run, context, state.Task, repair, cancellationToken);

            if (attempt.Kind == AttemptKind.Kept)
            {
                repaired.Add(state);
            }
            else
            {
                await RevertAsync(run, context, state, $"it changed kept behaviour ({string.Join("; ", result.Broken.Select(Describe))}), and the repair was not kept: {attempt.Reason}", cancellationToken);
            }
        }

        if (repaired.Count > 0)
        {
            foreach (var (state, result) in await CheckObligationsAsync(run, context, repaired, cancellationToken))
            {
                await RevertAsync(run, context, state, $"it still changed kept behaviour after its repair ({string.Join("; ", result.Broken.Select(Describe))})", cancellationToken);
            }
        }

        if (run.RevertedAfterGate)
        {
            // The files changed after the comparison: re-validate, and let the caller compare again.
            run.Behaviour = null;
            run.Validation = await _validator.ValidateAsync(context.Input.Plan, context.Input.Workspace.RepositoryPath, cancellationToken);
            run.Problems = Classify(run, run.Validation);
        }
    }

    /// <summary>Compares once and records each task's obligations; returns the tasks that broke one.</summary>
    private async Task<List<(TaskState State, ObligationResult Result)>> CheckObligationsAsync(
        Run run,
        AttemptContext context,
        IReadOnlyList<TaskState> tasks,
        CancellationToken cancellationToken)
    {
        var input = context.Input;

        _progress.Report(input.Plan.RepositoryName, $"contract gate: checking the obligations of {tasks.Count} port(s)");

        var report = await _behaviour!.CompareAsync(input.Plan, input.Workspace.RepositoryPath, input.Baseline!, run.Validation, cancellationToken);
        run.Behaviour = report;

        if (report.Comparison is null)
        {
            foreach (var state in tasks)
            {
                state.Record = state.Record with { ObligationsUnchecked = [$"not compared: {report.Note}"] };
            }

            return [];
        }

        var results = PortObligations.Check(tasks.Select(state => state.Task).ToList(), input.Baseline!.Baseline!, report.Comparison, _knowledge);
        var broken = new List<(TaskState, ObligationResult)>();

        foreach (var (state, result) in tasks.Zip(results))
        {
            state.Record = state.Record with
            {
                ObligationsKept = result.Kept,
                ObligationsBroken = result.Broken.Select(Describe).ToList(),
                ObligationsExplained = result.Explained.Select(explained => $"{Describe(explained.Difference)} ({explained.Signature.Id})").ToList(),
                ObligationsUnchecked = result.Unchecked
            };

            if (!result.Holds)
            {
                broken.Add((state, result));
            }
        }

        return broken;
    }

    /// <summary>One AI attempt at a task, judged at once; reverted unless kept.</summary>
    private async Task<AttemptResult> AttemptAsync(
        Run run,
        AttemptContext context,
        PortTask task,
        PortRepair? repair,
        CancellationToken cancellationToken)
    {
        var input = context.Input;
        var repository = input.Plan.RepositoryName;
        var path = input.Workspace.RepositoryPath;
        var number = run.Attempts.Count + 1;
        var failedCheck = run.Validation.Checks.FirstOrDefault(check => !check.Passed)?.Check ?? "contract";

        var tools = new RemediationWorkspaceTools(
            path,
            context.OpenRewritePatch,
            task.Evidence,
            classInspector: context.ClassInspector,
            allowedFiles: task.Files);

        var request = new PortRequest(
            repository,
            input.Plan.Target.SpringBootVersion,
            input.Plan.Target.JavaVersion,
            task,
            input.Baseline?.Baseline?.Items
                .Where(item => task.Obliges(item.Key))
                .ToDictionary(item => item.Key, item => item.Value) ?? [],
            repair);

        var attempt = new RemediationAttemptRecord(number, failedCheck, null, false, null, null, null, null, [], null, null)
        {
            Task = task.Id
        };

        run.Tasks[task.Id].Record = run.Tasks[task.Id].Record with { Attempts = [.. run.Tasks[task.Id].Record.Attempts, number] };

        _progress.Report(repository, $"attempt {number} ({task.Id}{(repair is null ? "" : ", repair")}): asking the agent");

        RemediationAgentResult proposed;

        try
        {
            proposed = await _agent.ProposeAsync(request, tools.AsAITools(), cancellationToken);
        }
        catch (InvalidRemediationProposalException exception)
        {
            run.Attempts.Add(attempt with { Error = exception.Message, ToolCalls = tools.Calls });
            return AttemptResult.NotKept($"the answer was not a usable proposal: {exception.Message}", [exception.Message]);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            run.Attempts.Add(attempt with { Error = exception.Message, ToolCalls = tools.Calls });
            run.Tasks[task.Id].Record = run.Tasks[task.Id].Record with { Status = PortTaskStatus.Error, Reason = exception.Message };
            throw new PortInfrastructureException($"the agent failed on {task.Id}: {exception.Message}");
        }

        attempt = attempt with
        {
            Proposal = proposed.Proposal,
            ToolCalls = tools.Calls,
            InputTokens = proposed.InputTokens,
            OutputTokens = proposed.OutputTokens
        };

        if (proposed.Proposal.Decision != RemediationDecision.Fix)
        {
            run.Attempts.Add(attempt);
            return AttemptResult.CannotFix($"the AI could not port it: {proposed.Proposal.Diagnosis}");
        }

        var applied = _applier.Apply(path, proposed.Proposal.Edits, task.Files);

        if (!applied.Applied)
        {
            _progress.Report(repository, $"attempt {number}: guardrails rejected: {applied.RejectionReason}");
            run.Attempts.Add(attempt with { RejectionReason = applied.RejectionReason });
            return AttemptResult.NotKept($"the guardrails rejected it: {applied.RejectionReason}", [applied.RejectionReason ?? string.Empty]);
        }

        var touched = task.Files.Union(applied.ChangedFiles, StringComparer.Ordinal).ToList();
        RecordedPatch patch;

        try
        {
            patch = await _recorder.RecordAsync(path, input.Workspace.RepositoryName, run.TreeId, $"{run.Patches.Count + 2:D2}-port-{Slug(task.Id)}", cancellationToken);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            // Without the patch the change cannot be reviewed: undo it and stop.
            await _restorer.RestoreAsync(path, run.TreeId, touched, cancellationToken);
            run.Attempts.Add(attempt with { Applied = true, AppliedEdits = applied.AppliedEdits, Error = $"Patch recording failed: {exception.Message}" });
            run.Tasks[task.Id].Record = run.Tasks[task.Id].Record with { Status = PortTaskStatus.Error, Reason = exception.Message };
            throw new PortInfrastructureException($"the patch for {task.Id} could not be recorded: {exception.Message}");
        }

        run.Patches.Add(patch.Artifact);

        var next = await _validator.ValidateAsync(input.Plan, path, cancellationToken);
        var nextProblems = Classify(run, next);
        var judgement = PortAcceptance.Judge(task, run.Validation, run.Problems, next, nextProblems);

        run.Attempts.Add(attempt with
        {
            Applied = true,
            AppliedEdits = applied.AppliedEdits,
            Patch = patch.Artifact,
            Validation = next,
            NotKept = judgement.Reason
        });

        if (judgement.Accepted)
        {
            _progress.Report(repository, $"attempt {number}: kept ({DescribeValidation(next)})");
            run.TreeId = patch.TreeId;
            run.Validation = next;
            run.Problems = nextProblems;
            return AttemptResult.Kept();
        }

        _progress.Report(repository, $"attempt {number}: not kept, reverted: {judgement.Reason}");
        await _restorer.RestoreAsync(path, run.TreeId, touched, cancellationToken);
        return AttemptResult.NotKept(judgement.Reason!, judgement.Feedback);
    }

    /// <summary>Puts a kept task's files back as the recipes left them, after later tasks were kept.</summary>
    private async Task RevertAsync(
        Run run,
        AttemptContext context,
        TaskState state,
        string reason,
        CancellationToken cancellationToken)
    {
        var input = context.Input;
        var path = input.Workspace.RepositoryPath;

        _progress.Report(input.Plan.RepositoryName, $"port {state.Task.Id} reverted: {reason}");

        await _restorer.RestoreAsync(path, state.BaseTreeId, state.Task.Files, cancellationToken);

        try
        {
            var patch = await _recorder.RecordAsync(path, input.Workspace.RepositoryName, run.TreeId, $"{run.Patches.Count + 2:D2}-port-{Slug(state.Task.Id)}-reverted", cancellationToken);
            run.Patches.Add(patch.Artifact);
            run.TreeId = patch.TreeId;
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            throw new PortInfrastructureException($"the revert of {state.Task.Id} could not be recorded: {exception.Message}");
        }

        state.Record = state.Record with { Status = PortTaskStatus.Reverted, Reason = reason };
        run.RevertedAfterGate = true;
    }

    private IReadOnlyList<ClassifiedProblem> Classify(
        Run run,
        TransformationValidationResult validation)
    {
        var failed = validation.Checks.FirstOrDefault(check => !check.Passed);

        return failed is null
            ? []
            : ProblemClassifier.Classify(_knowledge, ValidationDiagnosticsExtractor.ExtractUnbounded(failed), run.UnsupportedLibraries, run.Problems);
    }

    /// <summary>Class descriptions help the AI; without a classpath it works without them.</summary>
    private async Task<IJavaClassInspector?> CreateClassInspectorAsync(
        PortRunInput input,
        CancellationToken cancellationToken)
    {
        if (_classInspectorFactory is null)
        {
            return null;
        }

        try
        {
            return await _classInspectorFactory.CreateAsync(input.Workspace.RepositoryPath, cancellationToken);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            _progress.Report(input.Plan.RepositoryName, "class descriptions: unavailable (dependency classpath could not be resolved)");
            return null;
        }
    }

    private static string Describe(
        ContractDifference difference) =>
        $"{difference.Item}: {difference.Before} → {difference.After ?? "(not recorded)"}";

    private static string DescribeValidation(
        TransformationValidationResult validation) =>
        validation.Passed ? "validation passes" : $"{validation.Checks.First(check => !check.Passed).Check} still fails";

    // M-HTTPCLIENT-5-POOL → m-httpclient-5-pool (a safe patch file name).
    private static string Slug(
        string id) =>
        id.ToLowerInvariant();

    private sealed record AttemptContext(
        PortRunInput Input,
        string OpenRewritePatch,
        IJavaClassInspector? ClassInspector);

    private enum AttemptKind
    {
        Kept,
        NotKept,
        CannotFix
    }

    private sealed record AttemptResult(
        AttemptKind Kind,
        string? Reason,
        IReadOnlyList<string> Feedback)
    {
        public static AttemptResult Kept() => new(AttemptKind.Kept, null, []);

        public static AttemptResult NotKept(string reason, IReadOnlyList<string> feedback) => new(AttemptKind.NotKept, reason, feedback);

        public static AttemptResult CannotFix(string reason) => new(AttemptKind.CannotFix, reason, []);
    }

    /// <summary>A task in this run: the tree it started from, so it can be reverted on its own.</summary>
    private sealed class TaskState(
        PortTask task,
        string baseTreeId)
    {
        public PortTask Task { get; } = task;

        public string BaseTreeId { get; } = baseTreeId;

        public PortTaskRecord Record { get; set; } = new(
            task.Id,
            task.Mapping.Name,
            task.Files,
            task.Problems.Select(problem => problem.Id).ToList(),
            PortTaskStatus.Accepted,
            null,
            []);
    }

    /// <summary>The mutable state of one run.</summary>
    private sealed class Run(
        PortRunInput input)
    {
        public TransformationValidationResult Validation { get; set; } = input.Validation;

        public IReadOnlyList<ClassifiedProblem> Problems { get; set; } = input.Problems;

        public IReadOnlyCollection<string> UnsupportedLibraries { get; } = input.UnsupportedLibraries;

        public string TreeId { get; set; } = input.OpenRewriteTreeId;

        public List<RemediationAttemptRecord> Attempts { get; } = [];

        public List<PatchArtifact> Patches { get; } = [];

        // Insertion order is the order tasks were worked.
        public Dictionary<string, TaskState> Tasks { get; } = new(StringComparer.Ordinal);

        public IReadOnlyList<UnworkableProblem> Left { get; set; } = [];

        public List<string> DecisionNotes { get; } = [];

        public BehaviourReport? Behaviour { get; set; }

        public bool RevertedAfterGate { get; set; }

        public TaskState Start(
            PortTask task)
        {
            var state = new TaskState(task, TreeId);
            Tasks[task.Id] = state;
            return state;
        }

        public PortRunResult Finish(
            RemediationOutcome outcome) =>
            new(
                new RemediationReport(outcome, Attempts.ToList())
                {
                    Tasks = Tasks.Values.Select(task => task.Record).ToList(),
                    LeftForAPerson = Left.Select(left => $"{left.Problem.Id} {left.Problem.Subject}: {left.Reason}").ToList(),
                    DecisionNotes = DecisionNotes.ToList()
                },
                Validation,
                Patches.ToList(),
                Behaviour,
                Problems);
    }

    private sealed class PortInfrastructureException(string message) : Exception(message);
}
