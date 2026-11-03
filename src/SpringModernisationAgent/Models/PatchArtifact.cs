namespace SpringModernisationAgent.Models;

/// <summary>
/// A preserved, binary-safe git patch for one stage of a repository
/// migration (for example the OpenRewrite changes), written under the
/// artifacts directory.
/// </summary>
/// <param name="Stage">Stage name, such as <c>01-openrewrite</c> or <c>cumulative</c>.</param>
/// <param name="RelativePath">Path relative to the artifacts directory, using forward slashes.</param>
/// <param name="Sha256">Lower-case hex SHA-256 of the patch file bytes.</param>
/// <param name="ChangedFiles">Repository-relative paths changed by this patch.</param>
public sealed record PatchArtifact(
    string Stage,
    string RelativePath,
    string Sha256,
    IReadOnlyList<string> ChangedFiles);
