using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using SpringModernisationAgent.Recipes;

namespace SpringModernisationAgent.Tests.Recipes;

public class RecipeCatalogTests : IDisposable
{
    private static readonly RecipeModule Spring = new("org.openrewrite.recipe", "rewrite-spring", "6.37.1", RecipeModuleRole.Route, "8.90.2");
    private static readonly RecipeModule Hibernate = new("org.openrewrite.recipe", "rewrite-hibernate", "2.25.0", RecipeModuleRole.Candidate, "8.89.0");

    private readonly string _root = Directory.CreateTempSubdirectory("recipes-").FullName;

    [Fact]
    public void FromJars_ReadsEveryListedRecipeWithItsChildrenAndMentions()
    {
        var spring = Jar("spring.jar",
            csv:
            """
            ecosystem,packageName,name,displayName,description,recipeCount
            maven,org.openrewrite.recipe:rewrite-spring,lab.UpgradeBoot,Upgrade Boot,"Runs the Boot upgrade, ""end to end"",
            over two lines.",3
            maven,org.openrewrite.recipe:rewrite-spring,lab.Security,Migrate `WebSecurityConfigurerAdapter`,Uses a `SecurityFilterChain` bean.,1
            """,
            yaml:
            """
            ---
            type: specs.openrewrite.org/v1beta/recipe
            name: lab.UpgradeBoot
            recipeList:
              - lab.Security
              - org.openrewrite.java.ChangePackageInStringLiteral:
                  oldPackageName: javax.servlet
                  newPackageName: jakarta.servlet
            preconditions:
              - lab.OnlyIfSpring
            """);

        var catalog = RecipeCatalog.FromJars([(Spring, spring)]);

        var boot = catalog.Find("lab.UpgradeBoot")!;
        Assert.Equal("Runs the Boot upgrade, \"end to end\",\nover two lines.", boot.Description);
        Assert.Equal(["lab.Security", "org.openrewrite.java.ChangePackageInStringLiteral"], boot.Children);
        Assert.Contains("javax.servlet", boot.Mentions);
        Assert.Contains("jakarta.servlet", boot.Mentions);
        Assert.DoesNotContain("lab.OnlyIfSpring", boot.Children); // preconditions filter, they don't run

        var security = catalog.Find("lab.Security")!;
        Assert.Equal(["SecurityFilterChain", "WebSecurityConfigurerAdapter"], security.Mentions);
        Assert.Same(Spring, security.Module);
        Assert.Empty(catalog.Warnings);
    }

    [Fact]
    public void ReachableFrom_IsEveryRecipeTheRootsRunThroughCompositesAcrossModules()
    {
        var spring = Jar("spring.jar",
            csv: "ecosystem,packageName,name,displayName,description\nmaven,x,lab.Boot3,Boot 3,d\nmaven,x,lab.Boot2,Boot 2,d\n",
            yaml: "type: specs.openrewrite.org/v1beta/recipe\nname: lab.Boot3\nrecipeList:\n  - lab.Boot2\n---\ntype: specs.openrewrite.org/v1beta/recipe\nname: lab.Boot2\nrecipeList:\n  - lab.hibernate.Migrate61\n");
        var hibernate = Jar("hibernate.jar",
            csv: "ecosystem,packageName,name,displayName,description\nmaven,x,lab.hibernate.Migrate61,Hibernate 6.1,d\nmaven,x,lab.hibernate.MigrateUserType,Migrate `UserType`,d\n",
            yaml: null);

        var catalog = RecipeCatalog.FromJars([(Spring, spring), (Hibernate, hibernate)]);
        var route = catalog.ReachableFrom(["lab.Boot3"]);

        Assert.Equal(["lab.Boot2", "lab.Boot3", "lab.hibernate.Migrate61"], route.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("lab.hibernate.MigrateUserType", route); // the gap recipe discovery looks for
    }

    [Fact]
    public void FromJars_RepairsQuotedStringsWrappedIntoColumnZero()
    {
        // rewrite-spring 6.37.1's spring-boot-20-properties.yml does this.
        var spring = Jar("spring.jar",
            csv: null,
            yaml:
            """
            type: specs.openrewrite.org/v1beta/recipe
            name: lab.Properties
            recipeList:
              - lab.CommentOutProperty:
                  propertyKey: management.endpoint.sensitive
                  comment: "This property is deprecated: no longer customizable
            . Create or adapt your security configuration accordingly."
              - lab.Other
            """);

        var catalog = RecipeCatalog.FromJars([(Spring, spring)]);

        Assert.Equal(["lab.CommentOutProperty", "lab.Other"], catalog.Find("lab.Properties")!.Children);
        Assert.DoesNotContain(catalog.Warnings, warning => warning.Contains("skipped"));
    }

    [Fact]
    public void FromJars_ABadDocumentIsSkippedWithAWarningAndTheRestIsRead()
    {
        var spring = Jar("spring.jar",
            csv: null,
            yaml: "type: specs.openrewrite.org/v1beta/recipe\nname: lab.Good\nrecipeList:\n  - lab.X\n---\ntype: specs.openrewrite.org/v1beta/recipe\nname: [unclosed\n");

        var catalog = RecipeCatalog.FromJars([(Spring, spring)]);

        Assert.NotNull(catalog.Find("lab.Good"));
        Assert.Contains(catalog.Warnings, warning => warning.Contains("document 2 was skipped"));
        Assert.Contains(catalog.Warnings, warning => warning.Contains("no META-INF/rewrite/recipes.csv"));
    }

    [Fact]
    public void RecipeSources_ShippedFile_PinsEveryModuleWithItsRole()
    {
        var sources = RecipeSources.Load(Path.Combine(AppContext.BaseDirectory, RecipeSources.DefaultRelativePath));

        Assert.Equal("8.89.0", sources.PluginCoreVersion);
        Assert.Contains(sources.Modules, module => module is { ArtifactId: "rewrite-java", Role: RecipeModuleRole.Core });
        Assert.Contains(sources.Modules, module => module is { ArtifactId: "rewrite-spring", Version: "6.37.1", Role: RecipeModuleRole.Route });
        Assert.Contains(sources.Modules, module => module is { ArtifactId: "rewrite-hibernate", Role: RecipeModuleRole.Candidate });
    }

    [Theory]
    [InlineData("pluginCoreVersion: \"8.89.0\"\nmodules:\n  - coordinates: \"a:b\"\n    role: route\n    coreVersion: \"1\"\n", "group:artifact:version")]
    [InlineData("pluginCoreVersion: \"8.89.0\"\nmodules:\n  - coordinates: \"a:b:1\"\n    role: maybe\n    coreVersion: \"1\"\n", "needs a role")]
    [InlineData("modules: []\n", "needs pluginCoreVersion")]
    public void RecipeSources_InvalidEntries_FailNamingTheProblem(
        string yaml,
        string expected)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => RecipeSources.Parse(yaml));

        Assert.Contains(expected, exception.Message);
    }

    [Fact]
    public async Task JarCache_KeepsAJarOnlyIfItMatchesThePublishedSha1()
    {
        var bytes = Encoding.UTF8.GetBytes("jar bytes");
        var sha1 = Convert.ToHexStringLower(SHA1.HashData(bytes));
        var cache = new MavenCentralJarCache(_root, new HttpClient(new Repository(bytes, sha1)), "https://repo.test/maven2");

        var path = await cache.GetAsync(Hibernate);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.EndsWith(Path.Combine("org.openrewrite.recipe", "rewrite-hibernate-2.25.0.jar"), path);

        var tampered = new MavenCentralJarCache(Path.Combine(_root, "other"), new HttpClient(new Repository(bytes, new string('0', 40))), "https://repo.test/maven2");
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => tampered.GetAsync(Hibernate));

        Assert.Contains("it was not used", exception.Message);
        Assert.False(Directory.Exists(Path.Combine(_root, "other", "org.openrewrite.recipe"))
                     && Directory.EnumerateFiles(Path.Combine(_root, "other", "org.openrewrite.recipe")).Any());
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Jar(
        string name,
        string? csv,
        string? yaml)
    {
        var path = Path.Combine(_root, name);

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

        if (csv is not null)
        {
            using var writer = new StreamWriter(archive.CreateEntry("META-INF/rewrite/recipes.csv").Open());
            writer.Write(csv);
        }

        if (yaml is not null)
        {
            using var writer = new StreamWriter(archive.CreateEntry("META-INF/rewrite/recipes.yml").Open());
            writer.Write(yaml);
        }

        return path;
    }

    private sealed class Repository(byte[] jar, string sha1) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Assert.StartsWith("https://repo.test/maven2/org/openrewrite/recipe/rewrite-hibernate/2.25.0/rewrite-hibernate-2.25.0.jar", url);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = url.EndsWith(".sha1", StringComparison.Ordinal)
                    ? new StringContent(sha1 + "  rewrite-hibernate-2.25.0.jar\n")
                    : new ByteArrayContent(jar)
            });
        }
    }
}
