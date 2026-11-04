using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Reporting;

/// <summary>The same wording for recipe discovery in the job summary, the PR and the results document.</summary>
public static class RecipeDiscoveryText
{
    public static string Verdict(
        RecipeSuggestionVerdict verdict) =>
        verdict switch
        {
            RecipeSuggestionVerdict.Suitable => "✅ suitable",
            RecipeSuggestionVerdict.Unsuitable => "❌ unsuitable",
            RecipeSuggestionVerdict.Unverified => "❔ unverified",
            _ => "already ran"
        };

    /// <summary>"removes 3 errors, adds 4", or why it was not measured.</summary>
    public static string Effect(
        RecipeSuggestion suggestion) =>
        suggestion.Verdict switch
        {
            RecipeSuggestionVerdict.Suitable or RecipeSuggestionVerdict.Unsuitable =>
                $"removes {Count(suggestion.Removed.Count, "error")}, adds {Count(suggestion.Added.Count, "error")}",
            RecipeSuggestionVerdict.AlreadyRun => "runs in the route already; the errors remain",
            _ => suggestion.Note ?? "not measured"
        };

    /// <summary>The short recipe name, for tables: <c>org.openrewrite.hibernate.MigrateUserType</c> → <c>MigrateUserType</c>.</summary>
    public static string ShortName(
        string recipe) =>
        recipe[(recipe.LastIndexOf('.') + 1)..];

    public static string Files(
        RecipeSuggestion suggestion) =>
        string.Join(", ", suggestion.Files.Select(Path.GetFileName).Distinct());

    private static string Count(
        int count,
        string noun) =>
        $"{count} {noun}{(count == 1 ? "" : "s")}";
}
