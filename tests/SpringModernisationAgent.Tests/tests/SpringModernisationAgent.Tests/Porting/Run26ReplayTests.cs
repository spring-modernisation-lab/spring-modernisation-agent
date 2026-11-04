using System.Text.Json;
using SpringModernisationAgent.Behaviour;
using SpringModernisationAgent.Porting;
using SpringModernisationAgent.Remediation;
using SpringModernisationAgent.Triage;

namespace SpringModernisationAgent.Tests.Porting;

/// <summary>
/// Run 26's real behaviour records (Boot 2.7 baseline, and the migrated
/// application after the open loop), judged by the 8.4 contract gate.
/// </summary>
public class Run26ReplayTests
{
    private static readonly ProblemKnowledge Knowledge =
        ProblemKnowledge.LoadDefault(UnsupportedLibraries.All.Select(library => library.Name).ToList());

    private const string Security = "src/main/java/com/springmodernisationlab/orders/config/SecurityConfiguration.java";
    private const string Http = "src/main/java/com/springmodernisationlab/orders/config/InventoryClientConfiguration.java";

    [Fact]
    public void ContractGate_Run26_RevertsTheHttpClientPortAndKeepsTheSecurityPort()
    {
        var baseline = Load("R26-behaviour-baseline.json");
        var after = Load("R26-behaviour-after.json");
        PortTask[] tasks = [Task("M-SECURITY-6-FILTERCHAIN", Security), Task("M-HTTPCLIENT-5-POOL", Http)];

        var results = PortObligations.Check(tasks, baseline, BehaviourContract.Compare(baseline, after), Knowledge);

        // Security: every trailing-slash 404 is Spring 6's doing, not the port's; nothing it must keep broke.
        var security = results[0];
        Assert.True(security.Holds);
        Assert.Equal(4, security.Explained.Count);
        Assert.All(security.Explained, explained => Assert.Equal("BS-TRAILING-SLASH", explained.Signature.Id));
        Assert.NotEmpty(security.Kept);
        Assert.NotEmpty(security.Unchecked); // production did not start (Ehcache 2)

        // HttpClient: the TTL the open loop dropped (R14, R24, R26) is broken; the rest of the default profile kept.
        var http = results[1];
        Assert.False(http.Holds);
        var broken = Assert.Single(http.Broken);
        Assert.Equal("B-BEAN-default-HttpClient.connectionTimeToLive", broken.Item);
        Assert.Equal("30000ms", broken.Before);
        Assert.Contains("B-BEAN-default-HttpClient.maxTotal", http.Kept);
        Assert.Contains("B-BEAN-production-HttpClient.connectionTimeToLive", http.Unchecked);
    }

    private static PortTask Task(
        string mapping,
        string file) =>
        new(Knowledge.Mappings[mapping], [file],
            [new ClassifiedProblem("P1", ProblemKind.ApiMove, file, "S-TEST", mapping, null, null, ["e"])], ["e"]);

    private static BehaviourRecord Load(
        string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "tests", "fixtures")))
        {
            directory = directory.Parent;
        }

        var path = Path.Combine(directory!.FullName, "tests", "fixtures", "orders-service", name);
        return JsonSerializer.Deserialize<BehaviourRecord>(File.ReadAllText(path))!;
    }
}
