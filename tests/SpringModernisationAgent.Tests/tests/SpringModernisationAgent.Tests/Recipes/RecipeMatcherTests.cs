using System.IO.Compression;
using SpringModernisationAgent.Recipes;
using SpringModernisationAgent.Transformation;

namespace SpringModernisationAgent.Tests.Recipes;

public class RecipeMatcherTests : IDisposable
{
    // Shapes of probe #5's real compile errors (orders-service after OpenRewrite).
    private static readonly string[] Diagnostics =
    [
        "[ERROR] src/main/java/com/springmodernisationlab/orders/config/SecurityConfiguration.java:[12,72] cannot find symbol",
        "  symbol:   class WebSecurityConfigurerAdapter",
        "  location: package org.springframework.security.config.annotation.web.configuration",
        "[ERROR] src/main/java/com/springmodernisationlab/orders/config/SecurityConfiguration.java:[23,5] method does not override or implement a method from a supertype",
        "[ERROR] src/main/java/com/springmodernisationlab/orders/domain/CustomerOrder.java:[25,24] incompatible types: java.lang.Class<com.springmodernisationlab.orders.persistence.MoneyUserType> cannot be converted to java.lang.Class<? extends org.hibernate.usertype.UserType<?>>",
        "[ERROR] src/main/java/com/springmodernisationlab/orders/config/InventoryClientConfiguration.java:[28,17] cannot find symbol",
        "  symbol:   method setMaxConnTotal(int)",
        "  location: class org.apache.hc.client5.http.impl.classic.HttpClientBuilder"
    ];

    private readonly string _root = Directory.CreateTempSubdirectory("matcher-").FullName;

    [Fact]
    public void Problems_TakeTheMigrationRelevantNamesAndLeaveOutOwnCodeAndTheJdk()
    {
        var problems = RecipeMatcher.Problems(Diagnostics);

        // The "does not override" error names nothing, so it is not a problem to match.
        Assert.Equal(
            ["SecurityConfiguration.java", "CustomerOrder.java", "InventoryClientConfiguration.java"],
            problems.Select(problem => Path.GetFileName(problem.File)));

        Assert.Equal(
            ["WebSecurityConfigurerAdapter", "org.springframework.security.config.annotation.web.configuration.WebSecurityConfigurerAdapter"],
            problems[0].Terms);

        Assert.Equal(["UserType", "org.hibernate.usertype.UserType"], problems[1].Terms);

        Assert.Equal(
            ["HttpClientBuilder", "org.apache.hc.client5.http.impl.classic.HttpClientBuilder", "setMaxConnTotal"],
            problems[2].Terms);
    }

    [Fact]
    public void Match_ARecipeTheRouteDoesNotRunComesFirstAndAlreadyRunOnesAreLabelled()
    {
        var catalog = Catalog(
            ("lab.hibernate.MigrateUserType", "Migrate `UserType` to Hibernate 6", "", null),
            ("lab.security.WebSecurityConfigurerAdapter", "Use `SecurityFilterChain`", "Replaces `WebSecurityConfigurerAdapter`.", null),
            ("lab.http.PoolSettings", "Move `setMaxConnTotal`", "", null),
            ("lab.unrelated.Thing", "Does `SomethingElse`", "", null));

        var candidates = RecipeMatcher.Match(
            catalog,
            alreadyRun: new HashSet<string> { "lab.security.WebSecurityConfigurerAdapter" },
            RecipeMatcher.Problems(Diagnostics));

        Assert.Equal(
            ["lab.hibernate.MigrateUserType", "lab.http.PoolSettings", "lab.security.WebSecurityConfigurerAdapter"],
            candidates.Select(candidate => candidate.Recipe.Name));

        var userType = candidates[0];
        Assert.Equal(RecipeCandidateStatus.NotRun, userType.Status);
        Assert.Equal(["UserType", "org.hibernate.usertype.UserType"], userType.MatchedOn);
        Assert.EndsWith("CustomerOrder.java", Assert.Single(userType.Files));

        Assert.Equal(RecipeCandidateStatus.AlreadyRun, candidates[^1].Status);
    }

    [Fact]
    public void Match_NeverSuggestsVersionUpgradesSearchRecipesOrVersionsBeyondTheRoute()
    {
        var catalog = Catalog(
            ("lab.spring.framework.UpgradeSpringFramework_7_0", "Upgrade", "Handles `UserType`.", null),
            ("lab.hibernate.MigrateToHibernate61", "Upgrade", "Handles `UserType`.", null),
            ("lab.spring.search.FindUserTypes", "Find `UserType`", "", null),
            ("lab.spring.boot4.UserTypeFix", "Boot 4 `UserType`", "", null),
            ("lab.spring.boot3.UserTypeFix", "Boot 3 `UserType`", "", null));

        var candidates = RecipeMatcher.Match(
            catalog,
            alreadyRun: new HashSet<string> { "lab.spring.boot3.UpgradeSpringBoot_3_5" }, // the route reaches boot3
            RecipeMatcher.Problems(Diagnostics));

        Assert.Equal("lab.spring.boot3.UserTypeFix", Assert.Single(candidates).Recipe.Name);
    }

    [Fact]
    public void Match_AnExactTypeOutranksASimpleNameAndAPackage()
    {
        var catalog = Catalog(
            ("lab.a.ByPackage", "x", "", "org.apache.hc.client5.http"),
            ("lab.b.BySimpleName", "Fix `HttpClientBuilder`", "", null),
            ("lab.c.ByType", "x", "", "org.apache.hc.client5.http.impl.classic.HttpClientBuilder"));

        var candidates = RecipeMatcher.Match(catalog, new HashSet<string>(), RecipeMatcher.Problems(Diagnostics));

        Assert.Equal(["lab.c.ByType", "lab.b.BySimpleName", "lab.a.ByPackage"], candidates.Select(candidate => candidate.Recipe.Name));
        Assert.Equal([3, 2, 1], candidates.Select(candidate => candidate.Score));
    }

    [Fact]
    public void ExtractUnbounded_KeepsLongLinesWholeForMatching()
    {
        var longError =
            "[ERROR] src/main/java/a/MoneyUserType.java:[17,8] a.MoneyUserType is not abstract and does not override abstract method " +
            new string('x', 400) + " in org.hibernate.usertype.UserType";

        Assert.EndsWith("...", ValidationDiagnosticsExtractor.Extract(longError, null)[0]);
        Assert.EndsWith("org.hibernate.usertype.UserType", ValidationDiagnosticsExtractor.ExtractUnbounded(longError, null)[0]);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <param name="recipes">Name, display name, description, and an optional option value naming a type or package.</param>
    private RecipeCatalog Catalog(
        params (string Name, string Display, string Description, string? Option)[] recipes)
    {
        var path = Path.Combine(_root, $"{Guid.NewGuid():N}.jar");

        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var csv = new StreamWriter(archive.CreateEntry("META-INF/rewrite/recipes.csv").Open()))
            {
                csv.WriteLine("ecosystem,packageName,name,displayName,description");

                foreach (var recipe in recipes)
                {
                    csv.WriteLine($"maven,x,{recipe.Name},{recipe.Display},{recipe.Description}");
                }
            }

            using var yaml = new StreamWriter(archive.CreateEntry("META-INF/rewrite/recipes.yml").Open());

            foreach (var recipe in recipes.Where(recipe => recipe.Option is not null))
            {
                yaml.WriteLine("---");
                yaml.WriteLine("type: specs.openrewrite.org/v1beta/recipe");
                yaml.WriteLine($"name: {recipe.Name}");
                yaml.WriteLine("recipeList:");
                yaml.WriteLine("  - org.openrewrite.java.ChangeType:");
                yaml.WriteLine($"      oldFullyQualifiedTypeName: {recipe.Option}");
            }
        }

        var module = new RecipeModule("lab", "recipes", "1", RecipeModuleRole.Candidate, "8.89.0");
        return RecipeCatalog.FromJars([(module, path)]);
    }
}
