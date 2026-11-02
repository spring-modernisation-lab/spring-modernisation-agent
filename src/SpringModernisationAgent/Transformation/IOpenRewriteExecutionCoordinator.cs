using SpringModernisationAgent.Models;

public interface IOpenRewriteExecutionCoordinator
{
    Task<TransformationExecutionResult> ExecuteAsync(
        string organisation,
        TransformationPlan transformationPlan,
        CancellationToken cancellationToken = default);
}