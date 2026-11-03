using SpringModernisationAgent.Models;
using SpringModernisationAgent.Remediation;
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
    private readonly IRemediationLoop? _remediationLoop;

    public OpenRewriteExecutionCoordinator(
    IRepositoryWorkspaceProvider workspaceProvider,
    IRepositoryWorkspaceCleaner workspaceCleaner,
    OpenRewriteExecutionPlanBuilder executionPlanBuilder,
    IOpenRewriteRunner openRewriteRunner,
    TransformationValidator transformationValidator,
    IWorkspacePatchRecorder patchRecorder,
    IRemediationLoop? remediationLoop = null)
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

        // Null means remediation is not enabled for this run.
        _remediationLoop = remediationLoop;
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

        try
        {
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

            var transformationResult =
                await _openRewriteRunner.RunAsync(
                    executionPlan,
                    cancellationToken);

            if (transformationResult.Status !=
                TransformationStatus.Succeeded)
            {
                return new TransformationExecutionResult(
                    Transformation: transformationResult,
                    Validation: CreateNotRunValidation(
                        transformationPlan));
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

            var patches = new List<PatchArtifact>();

            if (openRewrite is not null)
            {
                patches.Add(openRewrite.Artifact);
            }

            RemediationReport? remediation = null;

            if (_remediationLoop is not null)
            {
                if (validationResult.Passed)
                {
                    remediation = new RemediationReport(RemediationOutcome.NotNeeded, []);
                }
                else if (openRewrite is null)
                {
                    // Edits that could not be attributed are never made.
                    remediation = new RemediationReport(RemediationOutcome.Error, []);
                }
                else
                {
                    var loop = await _remediationLoop.RunAsync(
                        transformationPlan,
                        workspace,
                        openRewrite.TreeId,
                        openRewrite.FullPath,
                        validationResult,
                        cancellationToken);

                    // Maven's last result is the authoritative outcome.
                    validationResult = loop.FinalValidation;
                    patches.AddRange(loop.AttemptPatches);
                    remediation = loop.Report;
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

            return new TransformationExecutionResult(
                Transformation: transformationResult,
                Validation: validationResult)
            {
                Patches = patches,
                PatchRecordingError = patchRecordingError,
                Remediation = remediation
            };
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
