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
                  recipe: "<APPROVED_RECIPE>"
                  artifact: "<APPROVED_ARTIFACT>"
                  version: "<PINNED_VERSION>"

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

            Assert.Equal(
                "<APPROVED_RECIPE>",
                route.OpenRewrite.Recipe);

            Assert.Equal(
                "<APPROVED_ARTIFACT>",
                route.OpenRewrite.Artifact);

            Assert.Equal(
                "<PINNED_VERSION>",
                route.OpenRewrite.Version);

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
