using SpringModernisationAgent.Models;
using SpringModernisationAgent.Workspace;

namespace SpringModernisationAgent.Transformation;

public sealed class OpenRewriteExecutionCoordinator
{
    private readonly IRepositoryWorkspaceProvider _workspaceProvider;
    private readonly IRepositoryWorkspaceCleaner _workspaceCleaner;
    private readonly OpenRewriteExecutionPlanBuilder _executionPlanBuilder;
    private readonly IOpenRewriteRunner _openRewriteRunner;
    private readonly TransformationValidator _transformationValidator;

    public OpenRewriteExecutionCoordinator(
    IRepositoryWorkspaceProvider workspaceProvider,
    IRepositoryWorkspaceCleaner workspaceCleaner,
    OpenRewriteExecutionPlanBuilder executionPlanBuilder,
    IOpenRewriteRunner openRewriteRunner,
    TransformationValidator transformationValidator)
    {
        ArgumentNullException.ThrowIfNull(workspaceProvider);
        ArgumentNullException.ThrowIfNull(workspaceCleaner);
        ArgumentNullException.ThrowIfNull(executionPlanBuilder);
        ArgumentNullException.ThrowIfNull(openRewriteRunner);
        ArgumentNullException.ThrowIfNull(transformationValidator);

        _workspaceProvider = workspaceProvider;
        _workspaceCleaner = workspaceCleaner;
        _executionPlanBuilder = executionPlanBuilder;
        _openRewriteRunner = openRewriteRunner;
        _transformationValidator = transformationValidator;
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

            var validationResult =
                await _transformationValidator.ValidateAsync(
                    transformationPlan,
                    workspace.RepositoryPath,
                    cancellationToken);

            return new TransformationExecutionResult(
                Transformation: transformationResult,
                Validation: validationResult);
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