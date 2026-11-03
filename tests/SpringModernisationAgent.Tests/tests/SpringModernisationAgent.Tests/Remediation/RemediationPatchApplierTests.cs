using SpringModernisationAgent.Remediation;

namespace SpringModernisationAgent.Tests.Remediation;

public class RemediationPatchApplierTests : IDisposable
{
    private const string SecurityPath =
        "src/main/java/com/example/config/SecurityConfiguration.java";

    private const string ClientPath =
        "src/main/java/com/example/config/InventoryClientConfiguration.java";

    private const string SecuritySource =
        "package com.example.config;\n" +
        "\n" +
        "public class SecurityConfiguration extends WebSecurityConfigurerAdapter {\n" +
        "    void configure() {}\n" +
        "}\n";

    private const string ClientSource =
        "class InventoryClientConfiguration {\n" +
        "    void pool(HttpClientBuilder builder) {\n" +
        "        builder.setMaxConnTotal(20);\n" +
        "    }\n" +
        "}\n";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"remediation-apply-{Guid.NewGuid():N}");

    private string Repository => Path.Combine(_root, "orders-service");

    public RemediationPatchApplierTests()
    {
        Write(SecurityPath, SecuritySource);
        Write(ClientPath, ClientSource);
        Write("src/test/java/com/example/OrderControllerTests.java", "class OrderControllerTests {}\n");
        Write("pom.xml", "<project/>\n");
        Write("src/main/resources/application.yml", "server:\n  port: 8080\n");
    }

    [Fact]
    public void Apply_ValidEditsAcrossFiles_AppliesAllAndListsChangedFiles()
    {
        var result = Apply(
            Edit(SecurityPath, " extends WebSecurityConfigurerAdapter", string.Empty),
            Edit(SecurityPath, "    void configure() {}\n", "    SecurityFilterChain chain() { return null; }\n"),
            Edit(ClientPath, "builder.setMaxConnTotal(20);", "connectionManager.setMaxTotal(20);"));

        Assert.True(result.Applied, result.RejectionReason);
        Assert.Null(result.RejectionReason);
        Assert.Equal([ClientPath, SecurityPath], result.ChangedFiles);

        Assert.Equal(
            "package com.example.config;\n" +
            "\n" +
            "public class SecurityConfiguration {\n" +
            "    SecurityFilterChain chain() { return null; }\n" +
            "}\n",
            Read(SecurityPath));

        Assert.Contains("connectionManager.setMaxTotal(20);", Read(ClientPath));
    }

    [Fact]
    public void Apply_NewFileUnderSrcMain_IsCreated()
    {
        const string path = "src/main/java/com/example/persistence/MoneyType.java";

        var result = Apply(Edit(path, string.Empty, "class MoneyType {}\n"));

        Assert.True(result.Applied, result.RejectionReason);
        Assert.Equal("class MoneyType {}\n", Read(path));
    }

    [Fact]
    public void Apply_ResourceFile_IsAllowed()
    {
        var result = Apply(
            Edit("src/main/resources/application.yml", "port: 8080", "port: 9090"));

        Assert.True(result.Applied, result.RejectionReason);
    }

    [Theory]
    [InlineData("src/test/java/com/example/OrderControllerTests.java", "test sources")]
    [InlineData("pom.xml", "src/main")]
    [InlineData(".github/workflows/ci.yml", "src/main")]
    [InlineData("mvnw", "src/main")]
    [InlineData("src/main/java/../../../pom.xml", "'..'")]
    [InlineData("../other-repo/src/main/java/A.java", "'..'")]
    [InlineData("/etc/src/main/A.java", "relative")]
    [InlineData("src/main/resources/logback.xml", "file type")]
    [InlineData("src/main/.git/config.yml", "src/main")]
    [InlineData("module/target/src/main/A.java", "src/main")]
    public void Apply_ForbiddenPath_RejectsWholeProposalAndChangesNothing(
        string path,
        string expectedReason)
    {
        var result = Apply(
            Edit(SecurityPath, " extends WebSecurityConfigurerAdapter", string.Empty),
            Edit(path, "x", "y"));

        Assert.False(result.Applied);
        Assert.Contains(expectedReason, result.RejectionReason);
        Assert.Empty(result.ChangedFiles);

        // The valid first edit was not applied either.
        Assert.Equal(SecuritySource, Read(SecurityPath));
    }

    [Fact]
    public void Apply_SearchNotFound_Rejects()
    {
        var result = Apply(Edit(SecurityPath, "class Missing", "class Found"));

        Assert.False(result.Applied);
        Assert.Contains("not found", result.RejectionReason);
        Assert.Equal(SecuritySource, Read(SecurityPath));
    }

    [Fact]
    public void Apply_SearchMatchesMoreThanOnce_Rejects()
    {
        // "{" opens both the class and the method.
        var result = Apply(Edit(SecurityPath, "{", "{ // changed"));

        Assert.False(result.Applied);
        Assert.Contains("more than once", result.RejectionReason);
    }

    [Fact]
    public void Apply_EditsToSameFile_ApplyInOrderAgainstUpdatedContent()
    {
        var result = Apply(
            Edit(ClientPath, "setMaxConnTotal(20)", "setMaxTotal(20)"),
            Edit(ClientPath, "builder.setMaxTotal(20);", "manager.setMaxTotal(20);"));

        Assert.True(result.Applied, result.RejectionReason);
        Assert.Contains("manager.setMaxTotal(20);", Read(ClientPath));
    }

    [Fact]
    public void Apply_CrlfFile_MatchesLfSearchAndKeepsCrlf()
    {
        const string path = "src/main/java/Windows.java";

        Write(path, "class Windows {\r\n    int a;\r\n}\r\n");

        var result = Apply(Edit(path, "{\n    int a;\n}", "{\n    long a;\n}"));

        Assert.True(result.Applied, result.RejectionReason);
        Assert.Equal("class Windows {\r\n    long a;\r\n}\r\n", Read(path));
    }

    [Fact]
    public void Apply_CreatingExistingFile_Rejects()
    {
        var result = Apply(Edit(SecurityPath, string.Empty, "class Replaced {}\n"));

        Assert.False(result.Applied);
        Assert.Contains("already exists", result.RejectionReason);
    }

    [Fact]
    public void Apply_EditingMissingFile_Rejects()
    {
        var result = Apply(Edit("src/main/java/Missing.java", "a", "b"));

        Assert.False(result.Applied);
        Assert.Contains("does not exist", result.RejectionReason);
    }

    [Fact]
    public void Apply_NoEdits_Rejects()
    {
        var result = new RemediationPatchApplier().Apply(Repository, []);

        Assert.False(result.Applied);
        Assert.Contains("no edits", result.RejectionReason);
    }

    [Fact]
    public void Apply_TooManyFiles_Rejects()
    {
        var edits = Enumerable.Range(0, RemediationPatchApplier.MaxFiles + 1)
            .Select(index => Edit($"src/main/java/New{index}.java", string.Empty, "class X {}\n"))
            .ToArray();

        var result = Apply(edits);

        Assert.False(result.Applied);
        Assert.Contains($"at most {RemediationPatchApplier.MaxFiles} files", result.RejectionReason);
        Assert.False(File.Exists(Path.Combine(Repository, "src/main/java/New0.java")));
    }

    [Fact]
    public void Apply_TooManyChangedLines_Rejects()
    {
        var bigReplacement = string.Concat(
            Enumerable.Repeat("int x;\n", RemediationPatchApplier.MaxChangedLines + 1));

        var result = Apply(Edit("src/main/java/Big.java", string.Empty, bigReplacement));

        Assert.False(result.Applied);
        Assert.Contains($"at most {RemediationPatchApplier.MaxChangedLines} lines", result.RejectionReason);
    }

    [Fact]
    public void Apply_SymbolicLinkTarget_Rejects()
    {
        var outside = Path.Combine(_root, "outside.java");
        File.WriteAllText(outside, "class Outside {}\n");

        try
        {
            File.CreateSymbolicLink(Path.Combine(Repository, "src/main/java/Link.java"), outside);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            // Creating links needs extra rights on Windows; Linux CI runs it.
            return;
        }

        var result = Apply(Edit("src/main/java/Link.java", "Outside", "Changed"));

        Assert.False(result.Applied);
        Assert.Equal("class Outside {}\n", File.ReadAllText(outside));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private RemediationApplyResult Apply(
        params RemediationEdit[] edits) =>
        new RemediationPatchApplier().Apply(Repository, edits);

    private static RemediationEdit Edit(
        string path,
        string search,
        string replace) =>
        new(path, search, replace, Rationale: "test");

    private string Read(
        string relativePath) =>
        File.ReadAllText(Path.Combine(Repository, relativePath));

    private void Write(
        string relativePath,
        string content)
    {
        var path = Path.Combine(Repository, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
