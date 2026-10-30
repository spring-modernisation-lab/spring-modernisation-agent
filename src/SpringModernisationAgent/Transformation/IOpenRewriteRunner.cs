using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Transformation;

public interface IOpenRewriteRunner
{
    Task<TransformationResult> RunAsync(
        OpenRewriteExecutionPlan executionPlan,
        CancellationToken cancellationToken = default);
}