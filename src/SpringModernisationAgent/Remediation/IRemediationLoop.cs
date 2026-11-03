using SpringModernisationAgent.Models;
using SpringModernisationAgent.Workspace;

namespace SpringModernisationAgent.Remediation;

public interface IRemediationLoop
{
    /// <param name="plan">The repository's plan; its fixed validation checks are re-run after each fix.</param>
    /// <param name="workspace">The migrated workspace, before cleanup.</param>
    /// <param name="openRewriteTreeId">Tree id of the OpenRewrite snapshot; the first attempt's patch is relative to it.</param>
    /// <param name="openRewritePatchPath">The OpenRewrite patch file, shown to the agent.</param>
    /// <param name="validation">The failed validation after OpenRewrite.</param>
    Task<RemediationLoopResult> RunAsync(
        TransformationPlan plan,
        RepositoryWorkspace workspace,
        string openRewriteTreeId,
        string openRewritePatchPath,
        TransformationValidationResult validation,
        CancellationToken cancellationToken = default);
}

/// <param name="Report">Every attempt and how the run ended.</param>
/// <param name="FinalValidation">The last Maven validation result, which decides success.</param>
/// <param name="AttemptPatches">Patches of the applied attempts, in order.</param>
public sealed record RemediationLoopResult(
    RemediationReport Report,
    TransformationValidationResult FinalValidation,
    IReadOnlyList<PatchArtifact> AttemptPatches);
