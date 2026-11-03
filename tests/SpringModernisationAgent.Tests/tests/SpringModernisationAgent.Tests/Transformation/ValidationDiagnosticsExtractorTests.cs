using SpringModernisationAgent.Transformation;

namespace SpringModernisationAgent.Tests.Transformation;

public class ValidationDiagnosticsExtractorTests
{
    private const string Workspace =
        "/home/runner/work/_temp/spring-modernisation-workspaces/" +
        "orders-service-89e7dc93f35949b0ab93fdaa64a81d11";

    // Shape of a real Maven compile failure (run 8): the error block is
    // printed once under COMPILATION ERROR and again after "Failed to
    // execute goal", followed by generic help text.
    private static readonly string CompileFailure = string.Join(
        "\n",
        "[INFO] Scanning for projects...",
        "[INFO] --- compiler:3.14.1:compile (default-compile) @ orders-service ---",
        $"[INFO] {Workspace}/src/main/java/A.java uses or overrides a deprecated API.",
        "[INFO] -------------------------------------------------------------",
        "[ERROR] COMPILATION ERROR : ",
        "[INFO] -------------------------------------------------------------",
        $"[ERROR] {Workspace}/src/main/java/com/example/SecurityConfiguration.java:[12,72] cannot find symbol",
        "  symbol:   class WebSecurityConfigurerAdapter",
        $"[ERROR] {Workspace}/src/main/java/com/example/MoneyUserType.java:[17,8] MoneyUserType is not abstract",
        "[INFO] 2 errors ",
        "[ERROR] BUILD FAILURE",
        "[ERROR] Failed to execute goal org.apache.maven.plugins:maven-compiler-plugin:3.14.1:compile (default-compile) on project orders-service: Compilation failure: Compilation failure: ",
        $"[ERROR] {Workspace}/src/main/java/com/example/SecurityConfiguration.java:[12,72] cannot find symbol",
        "[ERROR]   symbol:   class WebSecurityConfigurerAdapter",
        "[ERROR] -> [Help 1]",
        "[ERROR] ",
        "[ERROR] To see the full stack trace of the errors, re-run Maven with the -e switch.",
        "[ERROR] Re-run Maven using the -X switch to enable full debug logging.",
        "[ERROR] For more information about the errors and possible solutions, please read the following articles:",
        "[ERROR] [Help 1] http://cwiki.apache.org/confluence/display/MAVEN/MojoFailureException");

    [Fact]
    public void Extract_CompileFailure_ReturnsEachErrorOnceWithoutBoilerplate()
    {
        var diagnostics = ValidationDiagnosticsExtractor.Extract(
            CompileFailure,
            standardError: null);

        Assert.Equal(
            [
                "[ERROR] COMPILATION ERROR :",
                "[ERROR] src/main/java/com/example/SecurityConfiguration.java:[12,72] cannot find symbol",
                "  symbol:   class WebSecurityConfigurerAdapter",
                "[ERROR] src/main/java/com/example/MoneyUserType.java:[17,8] MoneyUserType is not abstract"
            ],
            diagnostics);
    }

    [Fact]
    public void Extract_IndentedLineAfterInfo_IsNotTreatedAsContinuation()
    {
        var output = string.Join(
            "\n",
            "[INFO] Compiling 13 source files",
            "  not an error detail",
            "[ERROR] real error");

        Assert.Equal(
            ["[ERROR] real error"],
            ValidationDiagnosticsExtractor.Extract(output, null));
    }

    [Fact]
    public void Extract_NeverExposesWorkspacePaths()
    {
        var diagnostics = ValidationDiagnosticsExtractor.Extract(
            CompileFailure,
            standardError: null);

        Assert.DoesNotContain(
            diagnostics,
            line => line.Contains("/home/runner")
                    || line.Contains("spring-modernisation-workspaces")
                    || line.Contains("89e7dc93f35949b0ab93fdaa64a81d11"));
    }

    [Fact]
    public void Extract_NormalisesWindowsWorkspacePaths()
    {
        var output =
            @"[ERROR] C:\temp\workspaces\orders-service-0123456789abcdef0123456789abcdef\src\main\java\A.java:[1,1] error";

        var diagnostics = ValidationDiagnosticsExtractor.Extract(
            output,
            standardError: null);

        Assert.Equal(
            [@"[ERROR] src\main\java\A.java:[1,1] error"],
            diagnostics);
    }

    [Fact]
    public void Extract_TestFailures_KeepsSurefireSummary()
    {
        var output = string.Join(
            "\n",
            "[INFO] Results:",
            "[ERROR] Failures: ",
            "[ERROR]   OrderControllerTests.legacyClients:84 Status expected:<200> but was:<404>",
            "[ERROR] Tests run: 8, Failures: 1, Errors: 0, Skipped: 0",
            "[ERROR] Failed to execute goal org.apache.maven.plugins:maven-surefire-plugin:3.5.3:test (default-test) on project orders-service: There are test failures.",
            "[ERROR] -> [Help 1]");

        var diagnostics = ValidationDiagnosticsExtractor.Extract(
            output,
            standardError: null);

        Assert.Equal(
            [
                "[ERROR] Failures:",
                "[ERROR]   OrderControllerTests.legacyClients:84 Status expected:<200> but was:<404>",
                "[ERROR] Tests run: 8, Failures: 1, Errors: 0, Skipped: 0"
            ],
            diagnostics);
    }

    [Fact]
    public void Extract_OnlyGoalFailure_KeepsGoalLine()
    {
        // e.g. dependency resolution failures have no detail block, so the
        // "Failed to execute goal" line is the diagnostic.
        var output = string.Join(
            "\n",
            "[ERROR] Failed to execute goal on project orders-service: Could not resolve dependencies: org.example:missing:jar:1.0 was not found",
            "[ERROR] -> [Help 1]");

        var diagnostics = ValidationDiagnosticsExtractor.Extract(
            output,
            standardError: null);

        Assert.Equal(
            ["[ERROR] Failed to execute goal on project orders-service: Could not resolve dependencies: org.example:missing:jar:1.0 was not found"],
            diagnostics);
    }

    [Fact]
    public void Extract_IncludesNonEmptyStandardErrorLines()
    {
        var diagnostics = ValidationDiagnosticsExtractor.Extract(
            standardOutput: null,
            standardError: "mvn: command not found\n\n");

        Assert.Equal(
            ["mvn: command not found"],
            diagnostics);
    }

    [Fact]
    public void Extract_NoErrors_ReturnsEmpty()
    {
        var diagnostics = ValidationDiagnosticsExtractor.Extract(
            "[INFO] BUILD SUCCESS",
            standardError: null);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Extract_NullInput_ReturnsEmpty()
    {
        Assert.Empty(
            ValidationDiagnosticsExtractor.Extract(null, null));
    }

    [Fact]
    public void Extract_TooManyLines_CapsAndReportsOmittedCount()
    {
        var output = string.Join(
            "\n",
            Enumerable.Range(1, ValidationDiagnosticsExtractor.MaxLines + 5)
                .Select(index => $"[ERROR] error {index}"));

        var diagnostics = ValidationDiagnosticsExtractor.Extract(
            output,
            standardError: null);

        Assert.Equal(
            ValidationDiagnosticsExtractor.MaxLines + 1,
            diagnostics.Count);

        Assert.Equal(
            "... 5 more diagnostic lines omitted",
            diagnostics[^1]);
    }

    [Fact]
    public void Extract_LongLine_IsTruncated()
    {
        var output = "[ERROR] " +
                     new string('x', ValidationDiagnosticsExtractor.MaxLineLength * 2);

        var line = Assert.Single(
            ValidationDiagnosticsExtractor.Extract(output, null));

        Assert.Equal(
            ValidationDiagnosticsExtractor.MaxLineLength + 3,
            line.Length);

        Assert.EndsWith("...", line);
    }
}
