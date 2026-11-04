using SpringModernisationAgent.Behaviour;
using SpringModernisationAgent.Models;
using SpringModernisationAgent.Reporting;

namespace SpringModernisationAgent.Tests.Reporting;

public class BehaviourTextTests
{
    [Fact]
    public void APlatformChange_IsNamedInTheTableAndTheSummary_ButTheContractStillDoesNotHold()
    {
        // Run 27: the trailing-slash 404s are Spring 6's doing, a decision for a person.
        var baseline = Record(("B-SEC-default-GET /api/orders/-VIEWER", "200"), ("B-CTX-default", "starts"));
        var after = Record(("B-SEC-default-GET /api/orders/-VIEWER", "404"), ("B-CTX-default", "starts"));
        var report = new BehaviourReport(baseline, after, BehaviourContract.Compare(baseline, after), null)
        {
            Explained = new Dictionary<string, string> { ["B-SEC-default-GET /api/orders/-VIEWER"] = "BS-TRAILING-SLASH" }
        };

        Assert.False(report.Holds);
        Assert.Contains("1 of the differences are known platform changes: decisions for a person", BehaviourText.Summary(report));
        Assert.Contains(
            "| `B-SEC-default-GET /api/orders/-VIEWER` | 200 | 404 | Changed: a platform change, a decision (BS-TRAILING-SLASH) |",
            BehaviourText.Differences(report.Comparison!, report.Explained));
    }

    private static BehaviourRecord Record(params (string Item, string Value)[] items) =>
        new(BehaviourRecord.CurrentSchema, "orders-service", "abc", items.ToDictionary(item => item.Item, item => item.Value));
}
