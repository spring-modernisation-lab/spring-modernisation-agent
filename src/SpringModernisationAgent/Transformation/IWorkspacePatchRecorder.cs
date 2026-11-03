using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Transformation;

/// <summary>
/// Snapshots a migration workspace and preserves the changes since a base
/// revision as a patch artifact, so the migrated code survives workspace
/// cleanup.
/// </summary>
public interface IWorkspacePatchRecorder
{
    /// <param name="repositoryPath">The workspace repository.</param>
    /// <param name="repositoryName">Used for the artifact directory.</param>
    /// <param name="baseRevision">
    /// The commit or tree the patch is relative to: the source commit for
    /// the first stage, the previous stage's tree id for later stages.
    /// </param>
    /// <param name="stage">The stage name, used as the patch file name.</param>
    Task<RecordedPatch> RecordAsync(
        string repositoryPath,
        string repositoryName,
        string baseRevision,
        string stage,
        CancellationToken cancellationToken = default);
}

/// <param name="TreeId">The git tree id of the workspace snapshot.</param>
/// <param name="Artifact">The patch written for this stage.</param>
public sealed record RecordedPatch(
    string TreeId,
    PatchArtifact Artifact);
