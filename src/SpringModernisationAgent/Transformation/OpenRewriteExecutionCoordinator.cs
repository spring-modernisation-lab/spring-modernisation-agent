using SpringModernisationAgent.Porting;
using SpringModernisationAgent.Behaviour;
using SpringModernisationAgent.Models;
using SpringModernisationAgent.Recipes;
using SpringModernisationAgent.Remediation;
using SpringModernisationAgent.Triage;
using SpringModernisationAgent.Workspace;

namespace SpringModernisationAgent.Transformation;

public sealed class OpenRewriteExecutionCoordinator : IOpenRewriteExecutionCoordinator
{
    private readonly IRepositoryWorkspaceProvider _workspaceProvider;
    private readonly IRepositoryWorkspaceCleaner _workspaceCleaner;
    private readonly OpenRewriteExecutionPlanBuilder _executionPlanBuilder;
    private readonly IOpenRewriteRunner _openRewriteRunner;
    private readonly TransformationValidator _transformationValidator;
    private readonly IWorkspacePatchRecorder _patchRecorder;
    private readonly IPortRunner? _ports;
    private readonly IMigrationProgress _progress;
    private readonly IRecipeDiscovery? _recipeDiscovery;
    private readonly ProblemKnowledge? _problemKnowledge;
    private readonly IBehaviourContractRecorder? _behaviour;

    public OpenRewriteExecutionCoordinator(
    IRepositoryWorkspaceProvider workspaceProvider,
    IRepositoryWorkspaceCleaner workspaceCleaner,
    OpenRewriteExecutionPlanBuilder executionPlanBuilder,
    IOpenRewriteRunner openRewriteRunner,
    TransformationValidator transformationValidator,
    IWorkspacePatchRecorder patchRecorder,
    IPortRunner? ports = null,
    IMigrationProgress? progress = null,
    IRecipeDiscovery? recipeDiscovery = null,
    ProblemKnowledge? problemKnowledge = null,
    IBehaviourContractRecorder? behaviour = null)
    {
        ArgumentNullException.ThrowIfNull(workspaceProvider);
        ArgumentNullException.ThrowIfNull(workspaceCleaner);
        ArgumentNullException.ThrowIfNull(executionPlanBuilder);
        ArgumentNullException.ThrowIfNull(openRewriteRunner);
        ArgumentNullException.ThrowIfNull(transformationValidator);
        ArgumentNullException.ThrowIfNull(patchRecorder);

        _workspaceProvider = workspaceProvider;
        _workspaceCleaner = workspaceCleaner;
        _executionPlanBuilder = executionPlanBuilder;
        _openRewriteRunner = openRewriteRunner;
        _transformationValidator = transformationValidator;
        _patchRecorder = patchRecorder;

        // Null means the AI is not enabled for this run.
        _ports = ports;
        _progress = progress ?? NullMigrationProgress.Instance;

        // Null means recipe discovery is off for this run.
        _recipeDiscovery = recipeDiscovery;

        // Null means failures are not classified (tests that don't need it).
        _problemKnowledge = problemKnowledge;

        // Null means the behaviour contract is off for this run.
        _behaviour = behaviour;
    }

    private IReadOnlyList<ClassifiedProblem> Classify(
        string repository,
        TransformationValidationResult validation,
        IReadOnlyCollection<string> unsupportedLibraries)
    {
        var failed = validation.Checks.FirstOrDefault(check => !check.Passed);

        if (_problemKnowledge is null || validation.Passed || failed is null)
        {
            return [];
        }

        var problems = ProblemClassifier.Classify(
            _problemKnowledge,
            ValidationDiagnosticsExtractor.ExtractUnbounded(failed),
            unsupportedLibraries);

        _progress.Report(
            repository,
            "problems left by the recipes: " + string.Join(", ", problems
                .GroupBy(problem => problem.Kind)
                .Select(group => $"{group.Count()} {group.Key}")));

        return problems;
    }

    public async Task<OpenRewriteExecutionPlan> CreateExecutionPlanAsync(
        string organisation,
        TransformationPlan transformationPlan,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organisation);
        ArgumentNullException.ThrowIfNull(transformationPlan);

        var workspace = await _workspaceProvider.CreateAsync(
            organisation,
            transformationPlan.RepositoryName,
            transformationPlan.SourceCommitSha,
            cancellationToken);

        EnsureWorkspaceMatchesPlan(
            transformationPlan,
            workspace);

        return _executionPlanBuilder.Build(
            transformationPlan,
            workspace.RepositoryPath);
    }

    public async Task<TransformationExecutionResult> ExecuteAsync(
        string organisation,
        TransformationPlan transformationPlan,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organisation);
        ArgumentNullException.ThrowIfNull(transformationPlan);

        RepositoryWorkspace? workspace = null;
        var repository = transformationPlan.RepositoryName;
        var started = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            _progress.Report(
                repository,
                $"cloning at {transformationPlan.SourceCommitSha[..Math.Min(7, transformationPlan.SourceCommitSha.Length)]}");

            workspace = await _workspaceProvider.CreateAsync(
                organisation,
                transformationPlan.RepositoryName,
                transformationPlan.SourceCommitSha,
                cancellationToken);

            EnsureWorkspaceMatchesPlan(
                transformationPlan,
                workspace);

            var executionPlan = _executionPlanBuilder.Build(
                transformationPlan,
                workspace.RepositoryPath);

            // Read from the assessed source, before anything changes it.
            // The baseline: the assessed commit, before anything changes it.
            var behaviourBaseline = _behaviour is null
                ? null
                : await _behaviour.RecordBaselineAsync(transformationPlan, workspace.RepositoryPath, cancellationToken);

            var unsupportedLibraries = UnsupportedLibraries.FindIn(workspace.RepositoryPath)
                .Select(library => library.Name)
                .ToList();

            if (unsupportedLibraries.Count > 0)
            {
                _progress.Report(
                    repository,
                    $"unsupported libraries (need a person): {string.Join(", ", unsupportedLibraries)}");
            }

            _progress.Report(
                repository,
                $"running OpenRewrite ({executionPlan.Recipes.Count} recipes)");

            var openRewriteStarted = System.Diagnostics.Stopwatch.StartNew();

            var transformationResult =
                await _openRewriteRunner.RunAsync(
                    executionPlan,
                    cancellationToken);

            _progress.Report(
                repository,
                $"OpenRewrite {transformationResult.Status}: " +
                $"{transformationResult.ChangedFiles.Count} files changed " +
                $"({openRewriteStarted.Elapsed.TotalSeconds:F0}s)");

            if (transformationResult.Status !=
                TransformationStatus.Succeeded)
            {
                return new TransformationExecutionResult(
                    Transformation: transformationResult,
                    Validation: CreateNotRunValidation(
                        transformationPlan))
                {
                    UnsupportedLibraries = unsupportedLibraries
                };
            }

            // Snapshot OpenRewrite's changes before Maven runs.
            var (openRewrite, patchRecordingError) = await TryRecordAsync(
                workspace,
                "01-openrewrite",
                cancellationToken);

            var validationResult =
                await _transformationValidator.ValidateAsync(
                    transformationPlan,
                    workspace.RepositoryPath,
                    cancellationToken);

            // What the route's recipes left, classified from facts before any AI work.
            var problems = Classify(transformationPlan.RepositoryName, validationResult, unsupportedLibraries);

            // On the code the route's recipes left, before the AI changes
            // anything. Suggestions only; it works on a throwaway copy.
            var recipeDiscovery = _recipeDiscovery is null || validationResult.Passed
                ? null
                : await _recipeDiscovery.DiscoverAsync(
                    transformationPlan,
                    workspace.RepositoryPath,
                    validationResult,
                    cancellationToken);

            var patches = new List<PatchArtifact>();

            if (openRewrite is not null)
            {
                patches.Add(openRewrite.Artifact);
            }

            RemediationReport? remediation = null;
            var remaining = problems;

            BehaviourReport? portsBehaviour = null;

            if (_ports is not null)
            {
                if (validationResult.Passed)
                {
                    remediation = new RemediationReport(RemediationOutcome.NotNeeded, []);
                }
                else if (openRewrite is null)
                {
                    // Edits that could not be attributed are never made.
                    _progress.Report(repository, "remediation skipped: the OpenRewrite patch could not be recorded");
                    remediation = new RemediationReport(RemediationOutcome.Error, []);
                }
                else
                {
                    // Scoped ports: one known API move at a time (docs/system-design.md §4.1).
                    var ported = await _ports.RunAsync(
                        new PortRunInput(
                            transformationPlan,
                            workspace,
                            openRewrite.TreeId,
                            openRewrite.FullPath,
                            validationResult,
                            problems,
                            unsupportedLibraries,
                            behaviourBaseline),
                        cancellationToken);

                    // Maven's last result is the authoritative outcome.
                    validationResult = ported.FinalValidation;
                    patches.AddRange(ported.AttemptPatches);
                    remediation = ported.Report;
                    portsBehaviour = ported.Behaviour;
                    remaining = ported.Problems;
                }
            }

            // Taken last so it includes any remediation edits; this is the
            // patch a draft pull request applies.
            if (openRewrite is not null)
            {
                var (cumulative, cumulativeError) = await TryRecordAsync(
                    workspace,
                    "cumulative",
                    cancellationToken);

                if (cumulative is not null)
                {
                    patches.Add(cumulative.Artifact);
                }

                patchRecordingError ??= cumulativeError;
            }

            // After the cumulative patch, so the probe (which it writes and
            // deletes) can never reach a patch.
            // The contract gate's comparison stands when nothing changed after it.
            var behaviour = behaviourBaseline is null
                ? null
                : portsBehaviour ?? await _behaviour!.CompareAsync(
                    transformationPlan,
                    workspace.RepositoryPath,
                    behaviourBaseline,
                    validationResult,
                    cancellationToken);

            if (behaviour?.Comparison is { } comparison && _problemKnowledge is not null)
            {
                behaviour = behaviour with
                {
                    Explained = comparison.Differences
                        .Select(difference => (difference.Item, Signature: _problemKnowledge.Explain(difference)))
                        .Where(entry => entry.Signature is not null)
                        .ToDictionary(entry => entry.Item, entry => entry.Signature!.Id)
                };
            }

            var result = new TransformationExecutionResult(
                Transformation: transformationResult,
                Validation: validationResult)
            {
                Patches = patches,
                PatchRecordingError = patchRecordingError,
                Remediation = remediation,
                UnsupportedLibraries = unsupportedLibraries,
                RecipeDiscovery = recipeDiscovery,
                Problems = problems,
                RemainingProblems = validationResult.Passed ? [] : remaining,
                Behaviour = behaviour
            };

            _progress.Report(
                repository,
                $"finished: {result.Verdict.State}" +
                (result.Verdict.Reasons.Count == 0 ? " " : $" ({string.Join(", ", result.Verdict.Reasons.Select(reason => reason.Id).Distinct())}) ") +
                $"({started.Elapsed.TotalSeconds:F0}s)");

            return result;
        }
        finally
        {
            if (workspace is not null)
            {
                await _workspaceCleaner.DeleteAsync(
                    workspace,
                    CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// A recording failure must not discard the migration and validation
    /// evidence, so it is reported on the result rather than thrown.
    /// Cancellation still propagates. Every stage is relative to the
    /// source commit except remediation attempts, which the loop chains.
    /// </summary>
    private async Task<(RecordedPatch? Patch, string? Error)> TryRecordAsync(
        RepositoryWorkspace workspace,
        string stage,
        CancellationToken cancellationToken)
    {
        try
        {
            var patch = await _patchRecorder.RecordAsync(
                workspace.RepositoryPath,
                workspace.RepositoryName,
                workspace.CommitSha,
                stage,
                cancellationToken);

            return (patch, null);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            return (null, exception.Message);
        }
    }

    private static TransformationValidationResult CreateNotRunValidation(
        TransformationPlan transformationPlan)
    {
        return new TransformationValidationResult(
            RepositoryName: transformationPlan.RepositoryName,
            SourceCommitSha: transformationPlan.SourceCommitSha,
            Status: TransformationValidationStatus.NotRun,
            Checks: []);
    }

    private static void EnsureWorkspaceMatchesPlan(
        TransformationPlan transformationPlan,
        RepositoryWorkspace workspace)
    {
        if (!string.Equals(
                transformationPlan.RepositoryName,
                workspace.RepositoryName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Workspace repository '{workspace.RepositoryName}' " +
                $"does not match transformation repository " +
                $"'{transformationPlan.RepositoryName}'.");
        }

        if (!string.Equals(
                transformationPlan.SourceCommitSha,
                workspace.CommitSha,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Workspace commit '{workspace.CommitSha}' " +
                $"does not match transformation source commit " +
                $"'{transformationPlan.SourceCommitSha}'.");
        }
    }
}
