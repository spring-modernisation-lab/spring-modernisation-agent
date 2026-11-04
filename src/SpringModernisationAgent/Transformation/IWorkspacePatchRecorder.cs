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

/// <summary>
/// Puts files back as they were in an earlier snapshot: how a rejected port
/// is reverted to its task's base tree (docs/system-design.md §4.1).
/// </summary>
public interface IWorkspaceRestorer
{
    /// <param name="treeId">A tree id from <see cref="RecordedPatch.TreeId"/>.</param>
    /// <param name="files">Repository-relative paths. Each is restored as it was in the tree, or deleted if the tree did not have it. No other file changes.</param>
    Task RestoreAsync(
        string repositoryPath,
        string treeId,
        IReadOnlyList<string> files,
        CancellationToken cancellationToken = default);
}

/// <param name="TreeId">The git tree id of the workspace snapshot.</param>
/// <param name="Artifact">The patch written for this stage.</param>
/// <param name="FullPath">Where the patch file was written.</param>
public sealed record RecordedPatch(
    string TreeId,
    PatchArtifact Artifact,
    string FullPath);
