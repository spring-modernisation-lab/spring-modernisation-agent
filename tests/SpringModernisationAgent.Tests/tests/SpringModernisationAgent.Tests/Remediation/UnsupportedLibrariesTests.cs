using SpringModernisationAgent.Remediation;

namespace SpringModernisationAgent.Tests.Remediation;

public class UnsupportedLibrariesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("unsupported-").FullName;

    [Fact]
    public void FindIn_ADependencyInThePomOrAnImportInMainCode_IsFound()
    {
        Write("pom.xml", "<dependency><groupId>net.sf.ehcache</groupId><artifactId>ehcache</artifactId></dependency>");
        Write("api/src/main/java/a/Docs.java", "import springfox.documentation.spring.web.plugins.Docket;\nclass Docs {}\n");

        var found = UnsupportedLibraries.FindIn(_root).Select(library => library.Name);

        Assert.Equal(["Ehcache 2", "Springfox"], found);
    }

    [Fact]
    public void FindIn_TestCodeBuildOutputAndOtherLibraries_AreIgnored()
    {
        Write("pom.xml", "<dependency><groupId>org.ehcache</groupId><artifactId>ehcache</artifactId></dependency>");
        Write("src/test/java/a/T.java", "import net.sf.ehcache.Element;\n");
        Write("target/classes/pom.xml", "<groupId>io.springfox</groupId>");

        Assert.Empty(UnsupportedLibraries.FindIn(_root));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(
        string relativePath,
        string content)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
