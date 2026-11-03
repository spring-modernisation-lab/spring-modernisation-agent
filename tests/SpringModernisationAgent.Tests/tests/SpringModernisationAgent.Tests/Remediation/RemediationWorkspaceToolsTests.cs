using SpringModernisationAgent.Remediation;

namespace SpringModernisationAgent.Tests.Remediation;

public class RemediationWorkspaceToolsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"remediation-tools-{Guid.NewGuid():N}");

    private string Repository => Path.Combine(_root, "orders-service");

    public RemediationWorkspaceToolsTests()
    {
        Write("pom.xml", "<project>\n  <version>3.5.16</version>\n</project>\n");
        Write(
            "src/main/java/com/example/MoneyUserType.java",
            "package com.example;\n" +
            "\n" +
            "public class MoneyUserType implements UserType {\n" +
            "    public int[] sqlTypes() { return null; }\n" +
            "}\n");
        Write("src/test/java/com/example/OrderTests.java", "class OrderTests {}\n");
        Write(".git/config", "[core]\n");
        Write(".github/workflows/ci.yml", "on: push\n");
        Write("target/classes/App.class", "binary");

        // A sibling whose name starts with the repository's name.
        Directory.CreateDirectory(Path.Combine(_root, "orders-service-evil"));
        File.WriteAllText(Path.Combine(_root, "orders-service-evil", "secret.txt"), "secret");
    }

    [Fact]
    public void ReadFile_ReturnsNumberedLines()
    {
        var text = CreateTools().ReadFile("src/main/java/com/example/MoneyUserType.java");

        Assert.Contains("3: public class MoneyUserType implements UserType {", text);
        Assert.Contains("4:     public int[] sqlTypes() { return null; }", text);
    }

    [Fact]
    public void ReadFile_LineRange_ReturnsOnlyThoseLines()
    {
        var text = CreateTools().ReadFile(
            "src/main/java/com/example/MoneyUserType.java",
            startLine: 3,
            endLine: 3);

        Assert.Contains("3: public class MoneyUserType", text);
        Assert.DoesNotContain("package com.example", text);
        Assert.DoesNotContain("sqlTypes", text);
    }

    [Fact]
    public void ReadFile_TestSourcesAndPom_AreReadable()
    {
        var tools = CreateTools();

        Assert.Contains("class OrderTests", tools.ReadFile("src/test/java/com/example/OrderTests.java"));
        Assert.Contains("3.5.16", tools.ReadFile("pom.xml"));
    }

    [Theory]
    [InlineData("../orders-service-evil/secret.txt")]
    [InlineData("src/../../orders-service-evil/secret.txt")]
    [InlineData("..\\orders-service-evil\\secret.txt")]
    [InlineData(".git/config")]
    [InlineData(".GIT/config")]
    [InlineData(".github/workflows/ci.yml")]
    [InlineData("")]
    [InlineData("missing.java")]
    [InlineData("src/main/java")]
    public void ReadFile_OutsideOrForbiddenOrMissing_ReturnsErrorWithoutContent(
        string path)
    {
        var text = CreateTools().ReadFile(path);

        Assert.StartsWith("Error:", text);
        Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("[core]", text);
    }

    [Fact]
    public void ReadFile_AbsolutePath_IsRejected()
    {
        var tools = CreateTools();

        Assert.StartsWith("Error:", tools.ReadFile(Path.Combine(Repository, "pom.xml")));
        Assert.StartsWith("Error:", tools.ReadFile("/etc/passwd"));
        Assert.StartsWith("Error:", tools.ReadFile("C:\\Windows\\win.ini"));
    }

    [Fact]
    public void ReadFile_SymbolicLinkOutOfTheWorkspace_IsRejected()
    {
        var link = Path.Combine(Repository, "src", "leak.txt");

        try
        {
            File.CreateSymbolicLink(
                link,
                Path.Combine(_root, "orders-service-evil", "secret.txt"));
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            // Creating links needs extra rights on Windows; Linux CI runs it.
            return;
        }

        var text = CreateTools().ReadFile("src/leak.txt");

        Assert.StartsWith("Error:", text);
        Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadFile_LargeFile_IsTruncated()
    {
        Write(
            "src/main/java/Big.java",
            string.Join("\n", Enumerable.Range(1, 2000).Select(i => $"// line {i}")));

        var text = CreateTools().ReadFile("src/main/java/Big.java");

        Assert.Contains($"{RemediationWorkspaceTools.MaxReadLines}: // line {RemediationWorkspaceTools.MaxReadLines}", text);
        Assert.DoesNotContain($"{RemediationWorkspaceTools.MaxReadLines + 1}: ", text);
        Assert.Contains("truncated", text);
    }

    [Fact]
    public void SearchSource_FindsLiteralMatchesWithRelativePaths()
    {
        var text = CreateTools().SearchSource("implements UserType");

        Assert.Equal(
            "src/main/java/com/example/MoneyUserType.java:3: " +
            "public class MoneyUserType implements UserType {",
            text.Trim());
    }

    [Fact]
    public void SearchSource_SkipsGitBuildOutputAndCiFiles_AndNeverShowsAbsolutePaths()
    {
        var tools = CreateTools();

        Assert.Equal("No matches.", tools.SearchSource("[core]"));
        Assert.Equal("No matches.", tools.SearchSource("binary"));
        Assert.Equal("No matches.", tools.SearchSource("on: push"));
        Assert.DoesNotContain(_root, tools.SearchSource("class"));
    }

    [Fact]
    public void SearchSource_GlobRestrictsFiles()
    {
        var tools = CreateTools();

        Assert.Contains("pom.xml", tools.SearchSource("version", "*.xml"));
        Assert.Equal("No matches.", tools.SearchSource("version", "*.java"));
    }

    [Fact]
    public void SearchSource_ResultCount_IsBounded()
    {
        Write(
            "src/main/java/Many.java",
            string.Join("\n", Enumerable.Range(1, 500).Select(i => $"int needle{i};")));

        var lines = CreateTools().SearchSource("needle").Split('\n');

        Assert.Equal(RemediationWorkspaceTools.MaxSearchResults + 1, lines.Length);
        Assert.Contains("more matches omitted", lines[^1]);
    }

    [Fact]
    public void ListFiles_ListsSourceFilesButNotGitBuildOutputOrCi()
    {
        var text = CreateTools().ListFiles();

        Assert.Contains("pom.xml", text);
        Assert.Contains("src/main/java/com/example/MoneyUserType.java", text);
        Assert.DoesNotContain(".git/", text);
        Assert.DoesNotContain(".github/", text);
        Assert.DoesNotContain("target/", text);
        Assert.DoesNotContain("evil", text);
    }

    [Fact]
    public void ListFiles_Directory_IsContained()
    {
        var tools = CreateTools();

        Assert.Equal(
            "src/main/java/com/example/MoneyUserType.java",
            tools.ListFiles("src/main").Trim());

        Assert.StartsWith("Error:", tools.ListFiles(".."));
    }

    [Fact]
    public void GetOpenRewritePatchAndDiagnostics_ReturnSuppliedEvidence()
    {
        var tools = CreateTools();

        Assert.Equal("diff --git a/pom.xml b/pom.xml", tools.GetOpenRewritePatch());
        Assert.Equal("[ERROR] cannot find symbol", tools.GetDiagnostics());
    }

    [Fact]
    public void EveryCall_IsRecorded()
    {
        var tools = CreateTools();

        tools.ReadFile("pom.xml");
        tools.ReadFile("../orders-service-evil/secret.txt");
        tools.SearchSource("UserType");

        Assert.Collection(
            tools.Calls,
            call =>
            {
                Assert.Equal("read_file", call.Tool);
                Assert.Equal("pom.xml", call.Arguments);
                Assert.True(call.Succeeded);
            },
            call =>
            {
                Assert.Equal("read_file", call.Tool);
                Assert.False(call.Succeeded);
            },
            call => Assert.Equal("search_source", call.Tool));
    }

    [Fact]
    public void CallBudget_WhenExhausted_RefusesFurtherCalls()
    {
        var tools = CreateTools(maxCalls: 2);

        tools.ReadFile("pom.xml");
        tools.ListFiles();

        var refused = tools.ReadFile("pom.xml");

        Assert.StartsWith("Error: tool call budget", refused);
        Assert.Equal(3, tools.Calls.Count);
    }

    [Fact]
    public void AsAITools_ExposesExactlyTheReadOnlyTools()
    {
        var names = CreateTools().AsAITools().Select(tool => tool.Name).Order();

        Assert.Equal(
            ["get_diagnostics", "get_openrewrite_patch", "list_files", "read_file", "search_source"],
            names);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private RemediationWorkspaceTools CreateTools(
        int maxCalls = RemediationWorkspaceTools.DefaultMaxCalls)
    {
        return new RemediationWorkspaceTools(
            Repository,
            openRewritePatch: "diff --git a/pom.xml b/pom.xml",
            diagnostics: ["[ERROR] cannot find symbol"],
            maxCalls: maxCalls);
    }

    private void Write(
        string relativePath,
        string content)
    {
        var path = Path.Combine(Repository, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
