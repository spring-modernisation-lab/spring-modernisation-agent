using SpringModernisationAgent.Behaviour;
using SpringModernisationAgent.Build;

namespace SpringModernisationAgent.Tests.Behaviour;

public class BehaviourProbeTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("probe-").FullName;

    public BehaviourProbeTests()
    {
        // The shape of orders-service.
        Write("pom.xml", "<project><dependency><artifactId>spring-security-test</artifactId></dependency></project>");
        Write("src/main/java/com/lab/orders/OrdersApplication.java", "package com.lab.orders;\n\n@SpringBootApplication\npublic class OrdersApplication {}\n");
        Write("src/main/java/com/lab/orders/config/SecurityConfiguration.java",
            ".antMatchers(HttpMethod.GET, \"/api/orders/**\").hasAnyRole(\"VIEWER\", \"OPERATOR\")\n" +
            ".antMatchers(HttpMethod.POST, \"/api/orders\").hasRole(\"OPERATOR\")\n" +
            "@Secured(\"ROLE_AUDITOR\")\n");
        Write("src/main/resources/application.yml", "spring:\n  cache:\n    type: none\n  datasource:\n    password: hunter2\ninventory:\n  base-url: http://localhost:8089\n");
        Write("src/main/resources/application-production.yml", "spring:\n  cache:\n    type: ehcache\n");
        Write("src/main/resources/ehcache.xml", "<ehcache/>");
    }

    [Fact]
    public void Discover_FindsTheApplicationPackageProfilesSettingsAndRolesFromTheRepository()
    {
        var target = BehaviourProbe.Discover(_root)!;

        Assert.Equal("com.lab.orders", target.BasePackage);
        Assert.Equal(["default", "production"], target.Profiles);
        Assert.Equal(["inventory.base-url", "spring.cache.type", "spring.datasource.password"], target.Settings);
        Assert.Equal(["AUDITOR", "OPERATOR", "VIEWER"], target.Roles);
        Assert.True(target.CanAuthenticate);
    }

    [Fact]
    public void Discover_ARepositoryWithoutABootApplication_IsNotProbed()
    {
        File.Delete(Path.Combine(_root, "src/main/java/com/lab/orders/OrdersApplication.java"));

        Assert.Null(BehaviourProbe.Discover(_root));
    }

    [Fact]
    public void Generate_FillsTheTemplateForAProfile()
    {
        var probe = Probe(new FakeMaven(_ => { }));

        var (className, source) = probe.Generate(BehaviourProbe.Discover(_root)!, "production");

        Assert.Equal("MigrationBehaviourProbeProductionTest", className);
        Assert.StartsWith("package com.lab.orders;", source);
        Assert.Contains("@ActiveProfiles(\"production\")", source);
        Assert.Contains("class MigrationBehaviourProbeProductionTest {", source);
        Assert.Contains("PRINCIPALS = { \"anonymous\", \"AUDITOR\", \"OPERATOR\", \"VIEWER\" }", source);
        Assert.Contains("request.with(user(\"probe\").roles(principal));", source);
        Assert.Contains("{ \"HttpClient.connectionTimeToLive\", \"CloseableHttpClient\", \"timeToLive\" }", source);
        Assert.DoesNotContain("{{", source);

        Assert.DoesNotContain("@ActiveProfiles", probe.Generate(BehaviourProbe.Discover(_root)!, "default").Source);
    }

    [Fact]
    public void Generate_WithoutSpringSecurityTest_ProbesAnonymousOnly()
    {
        Write("pom.xml", "<project/>");

        var source = Probe(new FakeMaven(_ => { })).Generate(BehaviourProbe.Discover(_root)!, "default").Source;

        Assert.Contains("PRINCIPALS = { \"anonymous\" }", source);
        Assert.DoesNotContain("SecurityMockMvcRequestPostProcessors", source);
    }

    [Fact]
    public async Task RecordAsync_ReadsWhatEachProfileWroteMasksSecretsAndAlwaysDeletesTheProbes()
    {
        var maven = new FakeMaven(arguments =>
        {
            var output = arguments.Single(argument => argument.StartsWith("-Dmigration.probe.out=", StringComparison.Ordinal))["-Dmigration.probe.out=".Length..];
            Directory.CreateDirectory(output);
            File.WriteAllLines(Path.Combine(output, "default.tsv"),
            [
                "B-CTX-default\tstarts",
                "B-CFG-default-spring.datasource.password\thunter2",
                "B-SEC-default-POST /api/orders-VIEWER\t403"
            ]);
            // production writes nothing: its context failed.
        });

        var record = await Probe(maven).RecordAsync(_root, "orders-service", "ce42258");

        Assert.Equal("starts", record.Items["B-CTX-default"]);
        Assert.Equal("(set)", record.Items["B-CFG-default-spring.datasource.password"]);
        Assert.Equal("403", record.Items["B-SEC-default-POST /api/orders-VIEWER"]);
        Assert.StartsWith("fails: ", record.Items["B-CTX-production"]);

        var call = Assert.Single(maven.Calls);
        Assert.Contains("-Dtest=MigrationBehaviourProbeDefaultTest,MigrationBehaviourProbeProductionTest", call);
        Assert.Contains("test", call);

        Assert.Empty(Directory.EnumerateFiles(_root, "MigrationBehaviourProbe*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task RecordAsync_ProbesAreDeletedEvenWhenMavenThrows()
    {
        var probe = Probe(new FakeMaven(_ => throw new IOException("mvn not found")));

        await Assert.ThrowsAsync<IOException>(() => probe.RecordAsync(_root, "orders-service", "ce42258"));

        Assert.Empty(Directory.EnumerateFiles(_root, "MigrationBehaviourProbe*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("beans:\n  - id: \"x\\\"; System.exit(1); //\"\n    startTypes: [A]\n    fields: [b]\n")]
    [InlineData("beans:\n  - id: ok\n    startTypes: [\"A,B\"]\n    fields: [b]\n")]
    public void ProbeConfiguration_RejectsAnythingThatIsNotAPlainName(
        string yaml)
    {
        // These are written into Java source.
        Assert.Throws<InvalidOperationException>(() => ProbeConfiguration.Parse(yaml));
    }

    [Fact]
    public void ProbeConfiguration_ShippedFileLoads()
    {
        var configuration = ProbeConfiguration.LoadDefault();

        Assert.Contains(configuration.Beans, bean => bean.Id == "HttpClient.connectionTimeToLive");
        Assert.Contains("timeToLiveSeconds", configuration.CacheTimeToLiveFields);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static BehaviourProbe Probe(FakeMaven maven) =>
        new(maven, ProbeConfiguration.LoadDefault());

    private void Write(
        string relativePath,
        string content)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private sealed class FakeMaven(Action<IReadOnlyList<string>> onRun) : IMavenProcessRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<MavenProcessResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
        {
            Calls.Add(arguments);

            // The probes exist while Maven runs.
            Assert.NotEmpty(Directory.EnumerateFiles(workingDirectory, "MigrationBehaviourProbe*Test.java", SearchOption.AllDirectories));

            onRun(arguments);
            return Task.FromResult(new MavenProcessResult(1, "[ERROR] Tests run: 2, Errors: 1", string.Empty));
        }
    }
}
