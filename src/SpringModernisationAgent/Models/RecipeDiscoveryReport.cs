namespace SpringModernisationAgent.Models;

public enum RecipeSuggestionVerdict
{
    /// <summary>Measured: removes at least one error and adds none.</summary>
    Suitable,

    /// <summary>Measured: adds errors, or removes none.</summary>
    Unsuitable,

    /// <summary>Not measured: it failed to run, or the failure is not a compile error.</summary>
    Unverified,

    /// <summary>The route already runs it and the errors remain; reported, never suggested.</summary>
    AlreadyRun
}

/// <summary>A recipe that matched the errors the route's recipes left, and what measuring it showed.</summary>
/// <param name="Recipe">The recipe's full name.</param>
/// <param name="Coordinates">The module that publishes it, group:artifact:version.</param>
/// <param name="MatchedOn">The error terms it matched.</param>
/// <param name="Files">The files whose errors it matched.</param>
/// <param name="Removed">Errors it removed, when measured.</param>
/// <param name="Added">Errors it added, when measured.</param>
/// <param name="Note">Why it is unverified, if it is.</param>
public sealed record RecipeSuggestion(
    string Recipe,
    string Coordinates,
    int Score,
    IReadOnlyList<string> MatchedOn,
    IReadOnlyList<string> Files,
    RecipeSuggestionVerdict Verdict,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Added,
    string? Note);

/// <summary>
/// Recipe discovery for one repository (docs/system-design.md §5.1). It is
/// evidence for a person: nothing here is ever applied to the migration.
/// </summary>
/// <param name="FailedCheck">The validation check the errors came from.</param>
/// <param name="Errors">How many errors (or failing tests) were matched.</param>
/// <param name="Error">Why discovery could not run, if it could not. The migration result is unaffected.</param>
public sealed record RecipeDiscoveryReport(
    string FailedCheck,
    int Errors,
    IReadOnlyList<RecipeSuggestion> Suggestions,
    string? Error);
