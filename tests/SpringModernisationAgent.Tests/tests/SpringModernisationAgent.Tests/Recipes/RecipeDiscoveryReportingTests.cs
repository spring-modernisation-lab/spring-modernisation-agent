using SpringModernisationAgent.Models;
using SpringModernisationAgent.Publishing;
using SpringModernisationAgent.Reporting;

namespace SpringModernisationAgent.Tests.Recipes;

/// <summary>Step 5: discovery results in the job summary and the draft PR, using probe #6's real result.</summary>
public class RecipeDiscoveryReportingTests
{
    private const string Sha = "ce422587799bb8c49d2724f1e963e456b7e977d6";

    private static readonly RecipeDiscoveryReport ProbeSix = new(
        "compile",
        5,
        [
            new RecipeSuggestion(
                "org.openrewrite.hibernate.MigrateUserType",
                "org.openrewrite.recipe:rewrite-hibernate:2.25.0",
                2,
                ["UserType", "org.hibernate.usertype.UserType"],
                ["src/main/java/a/CustomerOrder.java", "src/main/java/a/MoneyUserType.java"],
                RecipeSuggestionVerdict.Unsuitable,
                ["CustomerOrder.java: incompatible types", "MoneyUserType.java: not abstract (nullSafeGet)", "MoneyUserType.java: does not override"],
                ["MoneyUserType.java: cannot find symbol | symbol: class BigDecimal", "MoneyUserType.java: cannot find symbol | symbol: class BigDecimal",
                 "MoneyUserType.java: not abstract (getSqlType)", "MoneyUserType.java: cannot find symbol | symbol: variable resultSet"],
                null),
            new RecipeSuggestion(
                "org.openrewrite.java.spring.security5.WebSecurityConfigurerAdapter",
                "org.openrewrite.recipe:rewrite-spring:6.37.1",
                2,
                ["WebSecurityConfigurerAdapter"],
                ["src/main/java/a/SecurityConfiguration.java"],
                RecipeSuggestionVerdict.AlreadyRun,
                [],
                [],
                null)
        ],
        null);

    [Fact]
    public void JobSummary_ListsEachCandidateWithItsVerdictAndEffect()
    {
        var markdown = TransformationSummaryWriter.Render([Result() with { RecipeDiscovery = ProbeSix }]);

        Assert.Contains("### orders-service: recipe candidates", markdown);
        Assert.Contains("nothing here was applied", markdown);
        Assert.Contains("| `MigrateUserType` | org.openrewrite.recipe:rewrite-hibernate:2.25.0 | CustomerOrder.java, MoneyUserType.java | ❌ unsuitable | removes 3 errors, adds 4 errors |", markdown);
        Assert.Contains("| `WebSecurityConfigurerAdapter` | org.openrewrite.recipe:rewrite-spring:6.37.1 | SecurityConfiguration.java | already ran | runs in the route already; the errors remain |", markdown);
    }

    [Fact]
    public void JobSummary_WithoutDiscovery_HasNoCandidatesSection()
    {
        Assert.DoesNotContain("recipe candidates", TransformationSummaryWriter.Render([Result()]));
    }

    [Fact]
    public void PullRequest_ShowsWhatAnUnsuitableRecipeWouldBreakAndWhatAlreadyRan()
    {
        var body = PullRequestContentBuilder.Build(Plan(), Result() with { RecipeDiscovery = ProbeSix }, [], null)!.Body;

        var flags = body.IndexOf("## Review flags", StringComparison.Ordinal);
        var recipes = body.IndexOf("## Possible recipes for the remaining errors (suggestions, not applied)", StringComparison.Ordinal);
        var changes = body.IndexOf("## What changed", StringComparison.Ordinal);

        Assert.True(flags < recipes && recipes < changes);
        Assert.Contains("None was applied", body);
        Assert.Contains("- **`org.openrewrite.hibernate.MigrateUserType`** (`org.openrewrite.recipe:rewrite-hibernate:2.25.0`), matched in CustomerOrder.java, MoneyUserType.java: ❌ unsuitable, removes 3 errors, adds 4 errors.", body);
        Assert.Contains("  - adds: `MoneyUserType.java: cannot find symbol | symbol: variable resultSet`", body);
        Assert.Contains("Already in the route, and these errors remain: `WebSecurityConfigurerAdapter`.", body);
    }

    [Fact]
    public void PullRequest_ASuitableRecipeShowsWhatItWouldFix()
    {
        var suitable = ProbeSix with
        {
            Suggestions = [ProbeSix.Suggestions[0] with { Verdict = RecipeSuggestionVerdict.Suitable, Added = [] }]
        };

        var body = PullRequestContentBuilder.Build(Plan(), Result() with { RecipeDiscovery = suitable }, [], null)!.Body;

        Assert.Contains("✅ suitable, removes 3 errors, adds 0 errors.", body);
        Assert.Contains("  - fixes: `CustomerOrder.java: incompatible types`", body);
    }

    private static TransformationPlan Plan() =>
        new("orders-service", Sha, 1, RepositoryKind.SpringBootApplication, "spring-boot-application-2.7-to-3",
            new TransformationTarget("3.5.x", "21"), [], ["compile"]);

    private static TransformationExecutionResult Result() =>
        new(
            new TransformationResult("orders-service", Sha, TransformationStatus.Succeeded, [], ["pom.xml"], null, null),
            new TransformationValidationResult("orders-service", Sha, TransformationValidationStatus.Passed,
                [new TransformationValidationCheckResult("compile", true, null, null)]))
        {
            Patches = [new PatchArtifact("cumulative", "patches/orders-service/cumulative.patch", "abc", ["pom.xml"])]
        };
}
