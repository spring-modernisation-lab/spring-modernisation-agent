using SpringModernisationAgent.Transformation;

namespace SpringModernisationAgent.Tests.Transformation;

public class SurefireReportReaderTests : IDisposable
{
    private static readonly DateTime Since =
        new(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);

    private readonly string _repository = Path.Combine(
        Path.GetTempPath(),
        $"surefire-{Guid.NewGuid():N}",
        "orders-service-0123456789abcdef0123456789abcdef");

    [Fact]
    public void Read_AssertionFailure_ExtractsTestAndOnlyRepositoryFrames()
    {
        WriteReport(
            "TEST-com.springmodernisationlab.orders.web.OrderControllerTests.xml",
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <testsuite name="com.springmodernisationlab.orders.web.OrderControllerTests" tests="2" failures="1" errors="0">
              <testcase name="listsOrders" classname="com.springmodernisationlab.orders.web.OrderControllerTests" time="0.1"/>
              <testcase name="legacyClientsCanListOrdersUsingTrailingSlash" classname="com.springmodernisationlab.orders.web.OrderControllerTests" time="0.2">
                <failure message="Status expected:&lt;200&gt; but was:&lt;404&gt;" type="java.lang.AssertionError">java.lang.AssertionError: Status expected:&lt;200&gt; but was:&lt;404&gt;
            	at org.springframework.test.util.AssertionErrors.fail(AssertionErrors.java:59)
            	at org.springframework.test.web.servlet.result.StatusResultMatchers.lambda$matcher$9(StatusResultMatchers.java:637)
            	at com.springmodernisationlab.orders.web.OrderControllerTests.legacyClientsCanListOrdersUsingTrailingSlash(OrderControllerTests.java:42)
            	at java.base/java.lang.reflect.Method.invoke(Method.java:580)
            	at org.junit.platform.commons.util.ReflectionUtils.invokeMethod(ReflectionUtils.java:728)
            </failure>
                <system-out>noise</system-out>
              </testcase>
            </testsuite>
            """);

        var test = Assert.Single(SurefireReportReader.Read(_repository, Since));

        Assert.Equal(
            "com.springmodernisationlab.orders.web.OrderControllerTests",
            test.TestClass);

        Assert.Equal("legacyClientsCanListOrdersUsingTrailingSlash", test.TestName);
        Assert.Equal("failure", test.Kind);
        Assert.Equal("java.lang.AssertionError", test.Type);
        Assert.Equal("Status expected:<200> but was:<404>", test.Message);
        Assert.Null(test.RootCause);

        Assert.Equal(
            [
                "com.springmodernisationlab.orders.web.OrderControllerTests" +
                ".legacyClientsCanListOrdersUsingTrailingSlash(OrderControllerTests.java:42)"
            ],
            test.Frames);
    }

    [Fact]
    public void Read_ContextStartupError_ReportsDeepestCause()
    {
        WriteReport(
            "TEST-com.springmodernisationlab.orders.OrdersApplicationTests.xml",
            """
            <testsuite name="com.springmodernisationlab.orders.OrdersApplicationTests" tests="1" failures="0" errors="1">
              <testcase name="contextLoads" classname="com.springmodernisationlab.orders.OrdersApplicationTests">
                <error message="Failed to load ApplicationContext for [WebMergedContextConfiguration@1]" type="java.lang.IllegalStateException">java.lang.IllegalStateException: Failed to load ApplicationContext
            	at org.springframework.test.context.cache.DefaultCacheAwareContextLoaderDelegate.loadContext(DefaultCacheAwareContextLoaderDelegate.java:180)
            Caused by: org.springframework.beans.factory.BeanCreationException: Error creating bean with name 'servletContainerCompatibilityCheck'
            	at org.springframework.beans.factory.support.AbstractAutowireCapableBeanFactory.initializeBean(AbstractAutowireCapableBeanFactory.java:1806)
            Caused by: java.lang.ClassNotFoundException: javax.servlet.http.HttpServletRequest
            	at java.base/jdk.internal.loader.BuiltinClassLoader.loadClass(BuiltinClassLoader.java:641)
            	at java.base/java.lang.Class.forName(Class.java:421)
            	at com.springmodernisationlab.orders.platform.ServletContainerCompatibilityCheck.verify(ServletContainerCompatibilityCheck.java:31)
            	... 40 more
            </error>
              </testcase>
            </testsuite>
            """);

        var test = Assert.Single(SurefireReportReader.Read(_repository, Since));

        Assert.Equal("error", test.Kind);
        Assert.Equal("java.lang.IllegalStateException", test.Type);

        Assert.Equal(
            "java.lang.ClassNotFoundException: javax.servlet.http.HttpServletRequest",
            test.RootCause);

        Assert.Equal(
            [
                "com.springmodernisationlab.orders.platform.ServletContainerCompatibilityCheck" +
                ".verify(ServletContainerCompatibilityCheck.java:31)"
            ],
            test.Frames);
    }

    [Fact]
    public void Read_SkippedAndPassingTests_AreIgnored()
    {
        WriteReport(
            "TEST-A.xml",
            """
            <testsuite name="A" tests="2" skipped="1">
              <testcase name="passes" classname="A"/>
              <testcase name="skipped" classname="A"><skipped/></testcase>
            </testsuite>
            """);

        Assert.Empty(SurefireReportReader.Read(_repository, Since));
    }

    [Fact]
    public void Read_NoReports_ReturnsEmpty()
    {
        Directory.CreateDirectory(_repository);

        Assert.Empty(SurefireReportReader.Read(_repository, Since));
    }

    [Fact]
    public void Read_ReportsWrittenBeforeTheCheck_AreIgnored()
    {
        var path = WriteReport("TEST-Stale.xml", FailingSuite("Stale", "old"));

        File.SetLastWriteTimeUtc(path, Since.AddMinutes(-5));

        Assert.Empty(SurefireReportReader.Read(_repository, Since));
    }

    [Fact]
    public void Read_NestedModuleReports_AreFound()
    {
        WriteReport(
            "TEST-ModuleTest.xml",
            FailingSuite("ModuleTest", "fails"),
            module: "orders-core");

        var test = Assert.Single(SurefireReportReader.Read(_repository, Since));

        Assert.Equal("ModuleTest", test.TestClass);
    }

    [Fact]
    public void Read_WorkspacePathsInMessages_AreRemoved()
    {
        var message =
            $"Could not read {_repository}{Path.DirectorySeparatorChar}src" +
            $"{Path.DirectorySeparatorChar}main{Path.DirectorySeparatorChar}resources" +
            $"{Path.DirectorySeparatorChar}application.yml";

        WriteReport("TEST-Paths.xml", FailingSuite("Paths", "fails", message));

        var test = Assert.Single(SurefireReportReader.Read(_repository, Since));

        Assert.DoesNotContain(Path.GetTempPath().TrimEnd('\\', '/'), test.Message);
        Assert.DoesNotContain("0123456789abcdef", test.Message);
        Assert.EndsWith("application.yml", test.Message);
    }

    [Fact]
    public void Read_DocumentTypeDeclarations_AreRejectedAndOtherReportsStillRead()
    {
        // Reports come from the migrated code's build and are untrusted.
        WriteReport(
            "TEST-Entity.xml",
            """
            <?xml version="1.0"?>
            <!DOCTYPE testsuite [<!ENTITY secret SYSTEM "file:///etc/passwd">]>
            <testsuite name="Entity">
              <testcase name="leaks" classname="Entity"><failure message="&secret;"/></testcase>
            </testsuite>
            """);

        WriteReport("TEST-Malformed.xml", "<testsuite><testcase");
        WriteReport("TEST-Good.xml", FailingSuite("Good", "fails"));

        var test = Assert.Single(SurefireReportReader.Read(_repository, Since));

        Assert.Equal("Good", test.TestClass);
    }

    [Fact]
    public void Read_LargeInput_IsBounded()
    {
        var longMessage = new string('x', 2000);

        var cases = string.Join(
            "\n",
            Enumerable.Range(0, SurefireReportReader.MaxTests + 5)
                .Select(index =>
                    $"""<testcase name="t{index:D2}" classname="Many"><failure message="{longMessage}" type="E"/></testcase>"""));

        WriteReport("TEST-Many.xml", $"<testsuite name=\"Many\">{cases}</testsuite>");

        var tests = SurefireReportReader.Read(_repository, Since);

        Assert.Equal(SurefireReportReader.MaxTests, tests.Count);
        Assert.Equal("t00", tests[0].TestName);

        Assert.All(tests, test =>
            Assert.True(test.Message!.Length <= SurefireReportReader.MaxTextLength + 3));
    }

    public void Dispose()
    {
        var root = Path.GetDirectoryName(_repository)!;

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private string WriteReport(
        string fileName,
        string content,
        string? module = null)
    {
        var directory = Path.Combine(
            module is null ? _repository : Path.Combine(_repository, module),
            "target",
            "surefire-reports");

        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, fileName);

        File.WriteAllText(path, content.TrimStart());
        File.SetLastWriteTimeUtc(path, Since.AddSeconds(30));

        return path;
    }

    private static string FailingSuite(
        string testClass,
        string testName,
        string message = "boom")
    {
        var escaped = System.Security.SecurityElement.Escape(message);

        return $"""
            <testsuite name="{testClass}">
              <testcase name="{testName}" classname="{testClass}">
                <failure message="{escaped}" type="java.lang.AssertionError"/>
              </testcase>
            </testsuite>
            """;
    }
}
