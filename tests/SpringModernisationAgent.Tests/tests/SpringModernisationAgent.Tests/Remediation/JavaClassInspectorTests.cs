using SpringModernisationAgent.Build;
using SpringModernisationAgent.Remediation;

namespace SpringModernisationAgent.Tests.Remediation;

public class JavaClassInspectorTests : IDisposable
{
    private const string ClassName = "org.apache.hc.client5.http.impl.classic.HttpClientBuilder";

    private const string Classpath = "/home/runner/.m2/repository/httpclient5-5.4.jar:/home/runner/.m2/repository/httpcore5-5.3.jar";

    private readonly string _repository = Directory.CreateTempSubdirectory("class-inspector-").FullName;

    [Fact]
    public async Task DescribeAsync_RunsJavapPublicAgainstTheDependencyClasspath()
    {
        var javap = new FakeJavap(new MavenProcessResult(
            0,
            "Compiled from \"HttpClientBuilder.java\"\n" +
            "public class org.apache.hc.client5.http.impl.classic.HttpClientBuilder {\n" +
            "  public final org.apache.hc.client5.http.impl.classic.HttpClientBuilder evictIdleConnections(org.apache.hc.core5.util.TimeValue);\n" +
            "}\n",
            string.Empty));

        var text = await new JavapClassInspector(javap, Classpath, _repository).DescribeAsync(ClassName);

        Assert.Equal(["-public", "-classpath", Classpath, ClassName], javap.Arguments);
        Assert.Equal(_repository, javap.WorkingDirectory);
        Assert.Contains("evictIdleConnections(org.apache.hc.core5.util.TimeValue)", text);
        Assert.DoesNotContain("Compiled from", text);
    }

    [Theory]
    [InlineData("-J-Dfile.encoding=x")]
    [InlineData("HttpClientBuilder")]
    [InlineData("org..Foo")]
    [InlineData("org.example.Foo;rm")]
    [InlineData("org.example.Foo bar")]
    [InlineData("")]
    public async Task DescribeAsync_NamesThatAreNotFullyQualifiedJavaNames_AreRefusedWithoutRunningJavap(
        string className)
    {
        var javap = new FakeJavap(new MavenProcessResult(0, "x", ""));

        var text = await new JavapClassInspector(javap, Classpath, _repository).DescribeAsync(className);

        Assert.StartsWith("Error:", text);
        Assert.Null(javap.Arguments);
    }

    [Fact]
    public async Task DescribeAsync_NestedClassNames_AreAllowed()
    {
        var javap = new FakeJavap(new MavenProcessResult(0, "public class A$B {}", ""));

        await new JavapClassInspector(javap, Classpath, _repository)
            .DescribeAsync("org.example.Outer$Inner");

        Assert.Equal("org.example.Outer$Inner", javap.Arguments![^1]);
    }

    [Fact]
    public async Task DescribeAsync_ClassNotFound_ReportsAnErrorWithoutRevealingTheClasspath()
    {
        var javap = new FakeJavap(new MavenProcessResult(
            1,
            string.Empty,
            $"Error: class not found: org.example.Missing (classpath {Classpath})"));

        var text = await new JavapClassInspector(javap, Classpath, _repository)
            .DescribeAsync("org.example.Missing");

        Assert.StartsWith("Error:", text);
        Assert.Contains("org.example.Missing", text);
        Assert.DoesNotContain(".m2", text);
    }

    [Fact]
    public async Task DescribeAsync_LargeOutput_IsBounded()
    {
        var output = string.Join("\n", Enumerable.Range(0, 5000).Select(index => $"  public void method{index}();"));
        var javap = new FakeJavap(new MavenProcessResult(0, output, ""));

        var text = await new JavapClassInspector(javap, Classpath, _repository).DescribeAsync(ClassName);

        Assert.True(text.Length <= JavapClassInspector.MaxOutputChars + 100);
        Assert.Contains("truncated", text);
    }

    [Fact]
    public async Task Factory_BuildsTheClasspathWithMavenIntoTarget()
    {
        var maven = new FakeMaven(arguments =>
        {
            var output = arguments.Single(argument => argument.StartsWith("-Dmdep.outputFile=")).Split('=', 2)[1];
            File.WriteAllText(Path.Combine(_repository, output), Classpath);
            return new MavenProcessResult(0, "", "");
        });

        var inspector = await new MavenClassInspectorFactory(maven, new FakeJavap(new MavenProcessResult(0, "", "")))
            .CreateAsync(_repository);

        Assert.NotNull(inspector);
        Assert.Contains("dependency:build-classpath", maven.Arguments!);
        Assert.Contains("-Dmdep.outputFile=target/remediation-classpath.txt", maven.Arguments!);
        Assert.Contains("--batch-mode", maven.Arguments!);
    }

    [Fact]
    public async Task Factory_MavenFails_ReturnsNoInspector()
    {
        var maven = new FakeMaven(_ => new MavenProcessResult(1, "[ERROR] could not resolve", ""));

        var inspector = await new MavenClassInspectorFactory(maven, new FakeJavap(new MavenProcessResult(0, "", "")))
            .CreateAsync(_repository);

        Assert.Null(inspector);
    }

    public void Dispose() => Directory.Delete(_repository, recursive: true);

    private sealed class FakeJavap(MavenProcessResult result) : IJavapRunner
    {
        public IReadOnlyList<string>? Arguments { get; private set; }

        public string? WorkingDirectory { get; private set; }

        public Task<MavenProcessResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        {
            WorkingDirectory = workingDirectory;
            Arguments = arguments.ToList();
            return Task.FromResult(result);
        }
    }

    private sealed class FakeMaven(Func<IReadOnlyList<string>, MavenProcessResult> respond) : IMavenProcessRunner
    {
        public IReadOnlyList<string>? Arguments { get; private set; }

        public Task<MavenProcessResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        {
            Arguments = arguments.ToList();
            return Task.FromResult(respond(arguments));
        }
    }
}
