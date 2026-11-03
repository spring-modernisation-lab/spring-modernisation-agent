using SpringModernisationAgent.Models;
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

    public OpenRewriteExecutionCoordinator(
    IRepositoryWorkspaceProvider workspaceProvider,
    IRepositoryWorkspaceCleaner workspaceCleaner,
    OpenRewriteExecutionPlanBuilder executionPlanBuilder,
    IOpenRewriteRunner openRewriteRunner,
    TransformationValidator transformationValidator,
    IWorkspacePatchRecorder patchRecorder)
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

            // Record before validation so Maven build output can't leak
            // into the patch.
            var (patches, patchRecordingError) =
                await RecordPatchesAsync(
                    workspace,
                    cancellationToken);

            var validationResult =
                await _transformationValidator.ValidateAsync(
                    transformationPlan,
                    workspace.RepositoryPath,
                    cancellationToken);

            return new TransformationExecutionResult(
                Transformation: transformationResult,
                Validation: validationResult)
            {
                Patches = patches,
                PatchRecordingError = patchRecordingError
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
    /// Cancellation still propagates.
    /// </summary>
    private async Task<(IReadOnlyList<PatchArtifact> Patches, string? Error)>
        RecordPatchesAsync(
            RepositoryWorkspace workspace,
            CancellationToken cancellationToken)
    {
        try
        {
            var openRewrite = await _patchRecorder.RecordAsync(
                workspace.RepositoryPath,
                workspace.RepositoryName,
                workspace.CommitSha,
                "01-openrewrite",
                cancellationToken);

            var cumulative = await _patchRecorder.RecordAsync(
                workspace.RepositoryPath,
                workspace.RepositoryName,
                workspace.CommitSha,
                "cumulative",
                cancellationToken);

            return ([openRewrite.Artifact, cumulative.Artifact], null);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            return ([], exception.Message);
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
