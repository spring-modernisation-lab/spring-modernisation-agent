using SpringModernisationAgent.Config;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Tests.Config;

public class SpringMigrationPolicyLoaderTests
{
    [Fact]
    public void Load_DeserializesSpringMigrationPolicy()
    {
        const string yaml = """
            routes:
              - id: spring-boot-2.7-to-3

                source:
                  repositoryKinds:
                    - SpringBootApplication
                  springBootMinimum: "2.7.0"
                  springBootMaximum: "2.7.x"

                target:
                  springBoot: "3.5.x"
                  java: "21"

                openRewrite:
                  recipes:
                    - recipe: "org.openrewrite.java.spring.boot3.UpgradeSpringBoot_3_5"
                      artifact: "org.openrewrite.recipe:rewrite-spring"
                      version: "6.43.0"
                    - recipe: "org.openrewrite.java.migrate.UpgradeToJava21"
                      artifact: "org.openrewrite.recipe:rewrite-migrate-java"
                      version: "3.47.0"

                requirements:
                  buildSystems:
                    - maven

                  baselineBuildMustPass: true

                validation:
                  - compile
                  - unit-tests
            """;

        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString());

        Directory.CreateDirectory(tempDirectory);

        var path = Path.Combine(
            tempDirectory,
            "spring.yaml");

        try
        {
            File.WriteAllText(path, yaml);

            var loader = new SpringMigrationPolicyLoader();

            var policy = loader.Load(path);

            var route = Assert.Single(policy.Routes);

            Assert.Equal(
                "spring-boot-2.7-to-3",
                route.Id);

            Assert.Contains(
                RepositoryKind.SpringBootApplication,
                route.Source.RepositoryKinds);

            Assert.Equal(
                "2.7.0",
                route.Source.SpringBootMinimum);

            Assert.Equal(
                "2.7.x",
                route.Source.SpringBootMaximum);

            Assert.Equal(
                "3.5.x",
                route.Target.SpringBoot);

            Assert.Equal(
                "21",
                route.Target.Java);

            Assert.Collection(
                route.OpenRewrite.Recipes,
                recipe =>
                {
                    Assert.Equal(
                        "org.openrewrite.java.spring.boot3.UpgradeSpringBoot_3_5",
                        recipe.Recipe);
                    Assert.Equal(
                        "org.openrewrite.recipe:rewrite-spring",
                        recipe.Artifact);
                    Assert.Equal(
                        "6.43.0",
                        recipe.Version);
                },
                recipe =>
                {
                    Assert.Equal(
                        "org.openrewrite.java.migrate.UpgradeToJava21",
                        recipe.Recipe);
                    Assert.Equal(
                        "org.openrewrite.recipe:rewrite-migrate-java",
                        recipe.Artifact);
                    Assert.Equal(
                        "3.47.0",
                        recipe.Version);
                });

            Assert.Contains(
                "maven",
                route.Requirements.BuildSystems);

            Assert.True(
                route.Requirements.BaselineBuildMustPass);

            Assert.Equal(
                ["compile", "unit-tests"],
                route.Validation);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(
                    tempDirectory,
                    recursive: true);
            }
        }
    }

    [Fact]
    public void Load_FileDoesNotExist_ThrowsFileNotFoundException()
    {
        var loader = new SpringMigrationPolicyLoader();

        var path = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString(),
            "spring.yaml");

        Assert.Throws<FileNotFoundException>(
            () => loader.Load(path));
    }
}
