using SpringModernisationAgent.Behaviour;

namespace SpringModernisationAgent.Tests.Behaviour;

public class BehaviourContractTests
{
    private static BehaviourRecord Record(params (string Item, string Value)[] items) =>
        new(BehaviourRecord.CurrentSchema, "orders-service", "ce42258", items.ToDictionary(item => item.Item, item => item.Value));

    [Fact]
    public void Compare_TheSameBehaviour_Matches()
    {
        var baseline = Record(("B-CTX-default", "starts"), ("B-SEC-default-GET /api/orders-VIEWER", "200"));

        var comparison = BehaviourContract.Compare(baseline, baseline);

        Assert.True(comparison.Matches);
        Assert.Equal(2, comparison.Same);
        Assert.Empty(comparison.Differences);
    }

    [Fact]
    public void Compare_AChangedValue_DoesNotMatch()
    {
        // R24: the connection time-to-live was dropped.
        var baseline = Record(("B-BEAN-default-HttpClient.connectionTimeToLive", "30000ms"));
        var after = Record(("B-BEAN-default-HttpClient.connectionTimeToLive", "-1ms"));

        var comparison = BehaviourContract.Compare(baseline, after);

        Assert.False(comparison.Matches);
        var difference = Assert.Single(comparison.Differences);
        Assert.Equal(ContractItemStatus.Changed, difference.Status);
        Assert.Equal(("30000ms", "-1ms"), (difference.Before, difference.After));
    }

    [Fact]
    public void Compare_AnItemTheProbeCannotReadAfterwards_IsUnavailableNotSame()
    {
        // R21: caching switched off, so the cache's expiry can no longer be read.
        var baseline = Record(("B-CACHE-production-inventoryAvailability.timeToLive", "30000ms"), ("B-CTX-production", "starts"));
        var after = Record(("B-CTX-production", "starts"), ("B-CACHE-production-names", ""));

        var comparison = BehaviourContract.Compare(baseline, after);

        var difference = Assert.Single(comparison.Differences);
        Assert.Equal(ContractItemStatus.Unavailable, difference.Status);
        Assert.Null(difference.After);
        Assert.Equal(["B-CACHE-production-names"], comparison.New);
        Assert.False(comparison.Matches);

        var unreadable = BehaviourContract.Compare(
            baseline,
            Record(("B-CTX-production", "starts"), ("B-CACHE-production-inventoryAvailability.timeToLive", BehaviourRecord.Unavailable)));

        Assert.Equal(ContractItemStatus.Unavailable, unreadable.Differences.Single().Status);
    }

    [Fact]
    public void Compare_ADifferenceADecisionAllows_StillShowsButMatches()
    {
        // D-orders-service-1: Ehcache 2 replaced; the person accepted the new expiry.
        var baseline = Record(("B-CACHE-production-inventoryAvailability.timeToLive", "30000ms"));
        var after = Record(("B-CACHE-production-inventoryAvailability.timeToLive", "60000ms"));

        var comparison = BehaviourContract.Compare(
            baseline,
            after,
            new Dictionary<string, string> { ["B-CACHE-production-inventoryAvailability.timeToLive"] = "D-orders-service-1" });

        Assert.True(comparison.Matches);
        var difference = Assert.Single(comparison.Differences);
        Assert.Equal(ContractItemStatus.Allowed, difference.Status);
        Assert.Equal("D-orders-service-1", difference.Decision);
    }
}
