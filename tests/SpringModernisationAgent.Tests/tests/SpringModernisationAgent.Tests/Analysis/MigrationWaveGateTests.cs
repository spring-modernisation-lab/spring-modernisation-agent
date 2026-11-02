using SpringModernisationAgent.Analysis;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Tests.Analysis;

public class MigrationWaveGateTests
{
    [Fact]
    public void Apply_AllRepositoriesHavePlans_ReturnsAllPlansAndNoBlockedWave()
    {
        var waves = Waves(
            new MigrationWave(1, ["platform-parent"]),
            new MigrationWave(2, ["shared-security"]),
            new MigrationWave(3, ["account-service", "customer-service"]));

        var plans = new[]
        {
            CreatePlan("platform-parent", 1),
            CreatePlan("shared-security", 2),
            CreatePlan("account-service", 3),
            CreatePlan("customer-service", 3)
        };

        var result = new MigrationWaveGate().Apply(waves, plans);

        Assert.Equal(4, result.ExecutablePlans.Count);
        Assert.Null(result.EarliestBlockedWave);
        Assert.Empty(result.BlockedRepositories);
        Assert.Empty(result.WithheldPlans);
    }

    [Fact]
    public void Apply_FirstWaveFullyBlocked_WithholdsEveryLaterWave()
    {
        // The defect this gate exists for: without it, wave 2 would become
        // the "earliest eligible" wave because wave 1 has no plans at all.
        var waves = Waves(
            new MigrationWave(1, ["platform-parent"]),
            new MigrationWave(2, ["shared-security"]),
            new MigrationWave(3, ["account-service"]));

        var plans = new[]
        {
            CreatePlan("shared-security", 2),
            CreatePlan("account-service", 3)
        };

        var result = new MigrationWaveGate().Apply(waves, plans);

        Assert.Empty(result.ExecutablePlans);
        Assert.Equal(1, result.EarliestBlockedWave);
        Assert.Equal(["platform-parent"], result.BlockedRepositories);

        Assert.Equal(
            ["shared-security", "account-service"],
            result.WithheldPlans.Select(plan => plan.RepositoryName));
    }

    [Fact]
    public void Apply_WavePartiallyBlocked_KeepsReadyRepositoriesInThatWave()
    {
        // Repositories in the same wave do not depend on each other, so the
        // ready members of the blocked wave may still run.
        var waves = Waves(
            new MigrationWave(1, ["orders-service", "platform-parent"]),
            new MigrationWave(2, ["shared-security"]));

        var plans = new[]
        {
            CreatePlan("orders-service", 1),
            CreatePlan("shared-security", 2)
        };

        var result = new MigrationWaveGate().Apply(waves, plans);

        var executable = Assert.Single(result.ExecutablePlans);
        Assert.Equal("orders-service", executable.RepositoryName);

        Assert.Equal(1, result.EarliestBlockedWave);
        Assert.Equal(["platform-parent"], result.BlockedRepositories);

        var withheld = Assert.Single(result.WithheldPlans);
        Assert.Equal("shared-security", withheld.RepositoryName);
    }

    [Fact]
    public void Apply_LaterWaveBlocked_KeepsEarlierWavesAndBlockedWaveReadyMembers()
    {
        var waves = Waves(
            new MigrationWave(1, ["platform-parent"]),
            new MigrationWave(2, ["shared-security", "shared-logging"]),
            new MigrationWave(3, ["account-service"]));

        var plans = new[]
        {
            CreatePlan("platform-parent", 1),
            CreatePlan("shared-logging", 2),
            CreatePlan("account-service", 3)
        };

        var result = new MigrationWaveGate().Apply(waves, plans);

        Assert.Equal(
            ["platform-parent", "shared-logging"],
            result.ExecutablePlans.Select(plan => plan.RepositoryName));

        Assert.Equal(2, result.EarliestBlockedWave);
        Assert.Equal(["shared-security"], result.BlockedRepositories);
    }

    [Fact]
    public void Apply_ReportsEveryBlockedRepositoryInEarliestBlockedWaveInOrder()
    {
        var waves = Waves(
            new MigrationWave(1, ["zeta-service", "alpha-service", "beta-service"]));

        var plans = new[]
        {
            CreatePlan("beta-service", 1)
        };

        var result = new MigrationWaveGate().Apply(waves, plans);

        Assert.Equal(
            ["alpha-service", "zeta-service"],
            result.BlockedRepositories);
    }

    [Fact]
    public void Apply_RepositoryNameMatchingIsCaseInsensitive()
    {
        var waves = Waves(
            new MigrationWave(1, ["Platform-Parent"]),
            new MigrationWave(2, ["shared-security"]));

        var plans = new[]
        {
            CreatePlan("platform-parent", 1),
            CreatePlan("shared-security", 2)
        };

        var result = new MigrationWaveGate().Apply(waves, plans);

        Assert.Null(result.EarliestBlockedWave);
        Assert.Equal(2, result.ExecutablePlans.Count);
    }

    [Fact]
    public void Apply_PlanForRepositoryOutsideWaveTopology_Throws()
    {
        var waves = Waves(
            new MigrationWave(1, ["platform-parent"]));

        var plans = new[]
        {
            CreatePlan("platform-parent", 1),
            CreatePlan("unknown-service", 1)
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => new MigrationWaveGate().Apply(waves, plans));

        Assert.Contains("unknown-service", exception.Message);
    }

    [Fact]
    public void Apply_PlanWaveDisagreesWithTopology_Throws()
    {
        var waves = Waves(
            new MigrationWave(1, ["platform-parent"]),
            new MigrationWave(2, ["shared-security"]));

        var plans = new[]
        {
            CreatePlan("platform-parent", 1),
            CreatePlan("shared-security", 1)
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => new MigrationWaveGate().Apply(waves, plans));

        Assert.Contains("shared-security", exception.Message);
    }

    [Fact]
    public void Apply_NullArguments_Throw()
    {
        var gate = new MigrationWaveGate();

        Assert.Throws<ArgumentNullException>(
            () => gate.Apply(null!, []));

        Assert.Throws<ArgumentNullException>(
            () => gate.Apply(Waves(), null!));
    }

    [Fact]
    public void Apply_PreservesInputPlanOrder()
    {
        var waves = Waves(
            new MigrationWave(1, ["b-service", "a-service"]));

        var plans = new[]
        {
            CreatePlan("b-service", 1),
            CreatePlan("a-service", 1)
        };

        var result = new MigrationWaveGate().Apply(waves, plans);

        Assert.Equal(
            ["b-service", "a-service"],
            result.ExecutablePlans.Select(plan => plan.RepositoryName));
    }

    private static MigrationWavePlan Waves(
        params MigrationWave[] waves)
    {
        return new MigrationWavePlan(waves);
    }

    private static TransformationPlan CreatePlan(
        string repositoryName,
        int migrationWave)
    {
        return new TransformationPlan(
            RepositoryName: repositoryName,
            SourceCommitSha:
                "0123456789abcdef0123456789abcdef01234567",
            MigrationWave: migrationWave,
            RepositoryKind: RepositoryKind.SpringBootApplication,
            MigrationRouteId: "test-migration-route",
            Target: new TransformationTarget(
                SpringBootVersion: "3.5.x",
                JavaVersion: "21"),
            OpenRewrite:
            [
                new OpenRewriteTransformation(
                    Recipe:
                        "org.openrewrite.java.spring.boot3.UpgradeSpringBoot_3_5",
                    Artifact: "org.openrewrite.recipe:rewrite-spring",
                    Version: "6.37.1")
            ],
            Validation:
            [
                "compile"
            ]);
    }
}
