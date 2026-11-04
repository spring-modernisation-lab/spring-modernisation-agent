using SpringModernisationAgent.Models;
using SpringModernisationAgent.Publishing;
using SpringModernisationAgent.Remediation;

namespace SpringModernisationAgent.Tests.Publishing;

public class PullRequestContentBuilderTests
{
    private const string Sha = "ce4225812345678901234567890123456789abcd";

    [Fact]
    public void Build_SucceededRepository_IsADraftWithFactsFlagsAndEvidence()
    {
        var content = PullRequestContentBuilder.Build(
            CreatePlan(),
            CreateResult(passed: true, RemediationOutcome.Fixed),
            [new ReviewFlag(ReviewFlagKind.RequestMappingChanged, "05-remediation-attempt-4", "src/main/java/a/OrderController.java", "@PostMapping({\"\", \"/\"}). Check the security rules still cover every new path.")],
            CreateAnalysis());

        Assert.NotNull(content);
        Assert.Equal("migration/spring-boot-3.5-java-21", content.Branch);
        Assert.Equal("Migrate orders-service to Spring Boot 3.5 and Java 21", content.Title);
        Assert.False(content.NeedsDecision);

        Assert.Contains("Maven validation: **Passed**", content.Body);
        Assert.Contains("AI ports: **1 accepted (1 attempt)**", content.Body);
        Assert.Contains("## Review flags (1)", content.Body);
        Assert.Contains("**Request mapping** `src/main/java/a/OrderController.java` (05-remediation-attempt-4)", content.Body);
        Assert.Contains("| 1 | M-SECURITY-6-FILTERCHAIN | compile | OrderController.java (search/replace, 3 lines) | Passed | kept |", content.Body);
        Assert.Contains("| `M-SECURITY-6-FILTERCHAIN` Spring Security 6: WebSecurityConfigurerAdapter removed | OrderController.java | 1 | ✅ Accepted | - |", content.Body);
        Assert.Contains("## AI explanation (AI-generated, not authoritative)", content.Body);
        Assert.Contains("Compilation now succeeds.", content.Body);
        Assert.Contains($"Source commit: `{Sha}`", content.Body);
        Assert.Contains("`patches/orders-service/cumulative.patch` (sha256 `cafe`)", content.Body);
        Assert.Contains("never merged automatically", content.Body);
    }

    [Fact]
    public void Build_NeedsHumanDecision_PutsTheDecisionAndOptionsFirst()
    {
        var content = PullRequestContentBuilder.Build(
            CreatePlan(),
            CreateResult(passed: false, RemediationOutcome.NeedsHumanDecision),
            [],
            analysis: null);

        Assert.NotNull(content);
        Assert.True(content.NeedsDecision);
        Assert.Equal("[Needs decision] Migrate orders-service to Spring Boot 3.5 and Java 21", content.Title);

        var decision = content.Body.IndexOf("## Decision needed", StringComparison.Ordinal);
        var outcome = content.Body.IndexOf("## Outcome", StringComparison.Ordinal);

        Assert.True(decision >= 0 && decision < outcome);
        Assert.Contains("- `M-SECURITY-6-FILTERCHAIN` (OrderController.java): ✋ Cannot fix: the AI could not port it", content.Body);
        Assert.Contains("**Why `M-SECURITY-6-FILTERCHAIN` could not be ported (AI-generated):**", content.Body);
        Assert.Contains("- Use Caffeine with an explicit time-to-live.", content.Body);
        Assert.Contains("Maven validation: **Failed** (unit-tests)", content.Body);
        Assert.DoesNotContain("## AI explanation", content.Body);
    }

    [Fact]
    public void Build_UnsupportedLibraries_ListFixedManualStepsBetweenTheDecisionAndTheOutcome()
    {
        var result = CreateResult(passed: false, RemediationOutcome.NeedsHumanDecision) with
        {
            UnsupportedLibraries = ["Ehcache 2", "Not a known library"]
        };

        var body = PullRequestContentBuilder.Build(CreatePlan(), result, [], null)!.Body;

        var decision = body.IndexOf("## Decision needed", StringComparison.Ordinal);
        var steps = body.IndexOf("## Unsupported libraries: manual steps", StringComparison.Ordinal);
        var outcome = body.IndexOf("## Outcome", StringComparison.Ordinal);

        Assert.True(decision < steps && steps < outcome);
        Assert.Contains("not AI-generated", body);
        Assert.Contains("which the pipeline never lets the AI edit", body);
        Assert.Contains("### Ehcache 2 (`net.sf.ehcache`)", body);
        Assert.Contains("**Recommended:** Ehcache 3 through JCache", body);
        Assert.Contains("1. `pom.xml`: replace `net.sf.ehcache:ehcache` with `org.ehcache:ehcache`", body);
        Assert.Contains("- Caffeine:", body);
        // The verdict names it; the manual steps never invent guidance for it.
        Assert.DoesNotContain("### Not a known library", body);
    }

    [Fact]
    public void Build_MavenPassedButAnUnsupportedLibraryRemains_IsADecisionNotASuccess()
    {
        // Run 23: "Fixed" only because the AI hand-wrote a cache around Ehcache 2.
        var result = CreateResult(passed: true, RemediationOutcome.Fixed) with
        {
            UnsupportedLibraries = ["Ehcache 2"]
        };

        var content = PullRequestContentBuilder.Build(CreatePlan(), result, [], null)!;

        Assert.True(content.NeedsDecision);
        Assert.StartsWith("[Needs decision] ", content.Title);
        Assert.Contains("This repository still declares Ehcache 2", content.Body);
        Assert.Contains("**Maven passed anyway.**", content.Body);
        Assert.DoesNotContain("The Remediation Agent stopped", content.Body);
        Assert.DoesNotContain("Why (AI-generated)", content.Body);
        Assert.Contains("## Unsupported libraries: manual steps", content.Body);
    }

    [Fact]
    public void Build_AttemptsExhaustedButAnUnsupportedLibraryRemains_StillGetsADecisionPullRequest()
    {
        var result = CreateResult(passed: false, RemediationOutcome.AttemptsExhausted) with
        {
            UnsupportedLibraries = ["Ehcache 2"]
        };

        var content = PullRequestContentBuilder.Build(CreatePlan(), result, [], null)!;

        Assert.True(content.NeedsDecision);
        Assert.DoesNotContain("Maven passed anyway", content.Body);
    }

    [Fact]
    public void Build_NoUnsupportedLibraries_HasNoManualStepsSection()
    {
        var body = PullRequestContentBuilder.Build(CreatePlan(), CreateResult(passed: true, RemediationOutcome.Fixed), [], null)!.Body;

        Assert.DoesNotContain("Unsupported libraries", body);
    }

    [Fact]
    public void UnsupportedLibraries_EveryEntryHasARecommendationAndManualSteps()
    {
        Assert.All(UnsupportedLibraries.All, library =>
        {
            Assert.False(string.IsNullOrWhiteSpace(library.Recommended));
            Assert.NotEmpty(library.ManualSteps);
            Assert.Contains(library.ManualSteps, step => step.Contains("pom.xml", StringComparison.Ordinal));
        });
    }

    [Theory]
    [InlineData(false, RemediationOutcome.AttemptsExhausted)]
    [InlineData(false, RemediationOutcome.CannotFix)]
    public void Build_FailedRepository_GetsNoPullRequest(
        bool passed,
        RemediationOutcome outcome)
    {
        Assert.Null(PullRequestContentBuilder.Build(CreatePlan(), CreateResult(passed, outcome), [], null));
    }

    [Fact]
    public void Build_WithoutACumulativePatch_GetsNoPullRequest()
    {
        var result = CreateResult(passed: true, RemediationOutcome.Fixed) with
        {
            Patches = [],
            PatchRecordingError = "Git could not snapshot."
        };

        Assert.Null(PullRequestContentBuilder.Build(CreatePlan(), result, [], null));
    }

    private static TransformationPlan CreatePlan() =>
        new(
            RepositoryName: "orders-service",
            SourceCommitSha: Sha,
            MigrationWave: 1,
            RepositoryKind: RepositoryKind.SpringBootApplication,
            MigrationRouteId: "spring-boot-application-2.7-to-3",
            Target: new TransformationTarget("3.5.x", "21"),
            OpenRewrite: [],
            Validation: ["compile", "unit-tests"]);

    private static TransformationExecutionResult CreateResult(
        bool passed,
        RemediationOutcome outcome)
    {
        var validation = new TransformationValidationResult(
            "orders-service",
            Sha,
            passed ? TransformationValidationStatus.Passed : TransformationValidationStatus.Failed,
            [
                new TransformationValidationCheckResult("compile", true, null, null),
                new TransformationValidationCheckResult("unit-tests", passed, null, null)
            ]);

        // A scoped port: accepted, or one the AI said it cannot do.
        var decision = outcome == RemediationOutcome.NeedsHumanDecision
            ? RemediationDecision.CannotFix
            : RemediationDecision.Fix;

        var attempt = new RemediationAttemptRecord(
            Number: 1,
            FailedCheck: "compile",
            Proposal: new RemediationProposal(
                decision,
                "Spring Boot 3 removed Ehcache 2 support.",
                "Ehcache 2 integration was removed.",
                [],
                decision == RemediationDecision.CannotFix
                    ? ["Use Caffeine with an explicit time-to-live.", "Use Ehcache 3 through JCache."]
                    : []),
            Applied: decision == RemediationDecision.Fix,
            RejectionReason: null,
            Error: null,
            Patch: null,
            Validation: decision == RemediationDecision.Fix ? validation : null,
            ToolCalls: [],
            InputTokens: 1000,
            OutputTokens: 100)
        {
            AppliedEdits = decision == RemediationDecision.Fix
                ? [new AppliedEdit("src/main/java/a/OrderController.java", RemediationEditKind.SearchReplace, 3)]
                : [],
            Task = "M-SECURITY-6-FILTERCHAIN"
        };

        var task = new PortTaskRecord(
            "M-SECURITY-6-FILTERCHAIN",
            "Spring Security 6: WebSecurityConfigurerAdapter removed",
            ["src/main/java/a/OrderController.java"],
            ["P1"],
            decision == RemediationDecision.Fix ? PortTaskStatus.Accepted : PortTaskStatus.CannotFix,
            decision == RemediationDecision.Fix ? null : "the AI could not port it: Spring Boot 3 removed Ehcache 2 support.",
            [1]);

        return new TransformationExecutionResult(
            new TransformationResult(
                "orders-service",
                Sha,
                TransformationStatus.Succeeded,
                ["org.openrewrite.java.spring.boot3.UpgradeSpringBoot_3_5"],
                ["pom.xml", "src/main/java/a/OrderController.java"],
                null,
                null),
            validation)
        {
            Patches =
            [
                new PatchArtifact("01-openrewrite", "patches/orders-service/01-openrewrite.patch", "beef", ["pom.xml"]),
                new PatchArtifact("cumulative", "patches/orders-service/cumulative.patch", "cafe", ["pom.xml"])
            ],
            Remediation = new RemediationReport(outcome, [attempt]) { Tasks = [task] }
        };
    }

    [Fact]
    public void Build_AFlagOnAPortTheContractChecked_SaysWhatWasProvenAndStillAsksForReview()
    {
        // Run 27: "Removed code" on the HttpClient port, whose six client values the contract proved unchanged.
        var result = CreateResult(passed: true, RemediationOutcome.Fixed);
        var patch = new PatchArtifact("04-port-m-security-6-filterchain", "patches/orders-service/04.patch", "a", ["x"]);
        result = result with
        {
            Remediation = result.Remediation! with
            {
                Attempts = [result.Remediation.Attempts[0] with { Patch = patch }],
                Tasks = [result.Remediation.Tasks[0] with { ObligationsKept = ["a", "b", "c", "d", "e", "f"] }]
            }
        };

        var body = PullRequestContentBuilder.Build(
            CreatePlan(),
            result,
            [new ReviewFlag(ReviewFlagKind.CallRemoved, "04-port-m-security-6-filterchain", "src/main/java/a/A.java", "The AI removed calls.")],
            analysis: null)!.Body;

        Assert.Contains("The AI removed calls. The behaviour contract checked this port: 6 recorded behaviours are unchanged; anything the probe does not record still needs your review.", body);
    }

    private static MigrationAnalysis CreateAnalysis() =>
        new(
            RepositoryName: "orders-service",
            Summary: "Compilation now succeeds.",
            Findings: ["The security configuration was rewritten as a SecurityFilterChain."],
            RecommendedActions: ["Add a test for POST /api/orders/ as a viewer."]);
}
