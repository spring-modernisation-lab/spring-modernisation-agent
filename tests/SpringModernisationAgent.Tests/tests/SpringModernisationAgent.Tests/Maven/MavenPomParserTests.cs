using SpringModernisationAgent.Maven;

namespace SpringModernisationAgent.Tests.Maven;

public class MavenPomParserTests
{
    [Fact]
    public void Parse_ExtractsProjectCoordinates()
    {
        const string pom = """
            <project xmlns="http://maven.apache.org/POM/4.0.0">
                <modelVersion>4.0.0</modelVersion>

                <groupId>com.example.services</groupId>
                <artifactId>customer-service</artifactId>
                <version>1.0.0</version>
            </project>
            """;

        var parser = new MavenPomParser();

        var result = parser.Parse(pom);

        Assert.Equal(
            "com.example.services",
            result.Coordinate.GroupId);

        Assert.Equal(
            "customer-service",
            result.Coordinate.ArtifactId);

        Assert.Equal(
            "1.0.0",
            result.Coordinate.Version);
    }

    [Fact]
    public void Parse_ExtractsParent()
    {
        const string pom = """
            <project xmlns="http://maven.apache.org/POM/4.0.0">
                <modelVersion>4.0.0</modelVersion>

                <parent>
                    <groupId>com.example.platform</groupId>
                    <artifactId>platform-parent</artifactId>
                    <version>1.0.0</version>
                </parent>

                <groupId>com.example.services</groupId>
                <artifactId>customer-service</artifactId>
                <version>1.0.0</version>
            </project>
            """;

        var parser = new MavenPomParser();

        var result = parser.Parse(pom);

        Assert.NotNull(result.Parent);

        Assert.Equal(
            "com.example.platform",
            result.Parent.GroupId);

        Assert.Equal(
            "platform-parent",
            result.Parent.ArtifactId);

        Assert.Equal(
            "1.0.0",
            result.Parent.Version);
    }

    [Fact]
    public void Parse_DependencyWithoutScope_DefaultsToCompile()
    {
        const string pom = """
            <project xmlns="http://maven.apache.org/POM/4.0.0">
                <modelVersion>4.0.0</modelVersion>

                <groupId>com.example.services</groupId>
                <artifactId>customer-service</artifactId>
                <version>1.0.0</version>

                <dependencies>
                    <dependency>
                        <groupId>com.example.platform</groupId>
                        <artifactId>shared-security</artifactId>
                        <version>1.0.0</version>
                    </dependency>
                </dependencies>
            </project>
            """;

        var parser = new MavenPomParser();

        var result = parser.Parse(pom);

        var dependency = Assert.Single(result.Dependencies);

        Assert.Equal(
            "shared-security",
            dependency.Coordinate.ArtifactId);

        Assert.Equal(
            "compile",
            dependency.Scope);
    }
}
