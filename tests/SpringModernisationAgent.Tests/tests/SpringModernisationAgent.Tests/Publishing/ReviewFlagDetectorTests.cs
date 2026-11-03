using SpringModernisationAgent.Publishing;

namespace SpringModernisationAgent.Tests.Publishing;

public class ReviewFlagDetectorTests
{
    // Shapes taken from run 14's real remediation patches.
    private const string MappingPatch =
        """
        diff --git a/src/main/java/com/example/web/OrderController.java b/src/main/java/com/example/web/OrderController.java
        --- a/src/main/java/com/example/web/OrderController.java
        +++ b/src/main/java/com/example/web/OrderController.java
        @@ -27,7 +27,7 @@ public class OrderController {
        -    @GetMapping
        +    @GetMapping({"", "/"})
             @Transactional(readOnly = true)
        @@ -45,7 +45,7 @@ public class OrderController {
        -    @PostMapping
        +    @PostMapping({"", "/"})
        """;

    private const string HttpClientPatch =
        """
        diff --git a/src/main/java/com/example/config/InventoryClientConfiguration.java b/src/main/java/com/example/config/InventoryClientConfiguration.java
        --- a/src/main/java/com/example/config/InventoryClientConfiguration.java
        +++ b/src/main/java/com/example/config/InventoryClientConfiguration.java
        @@ -32,7 +32,6 @@
                         .setConnectionManager(connectionManager)
        -                .setConnectionTimeToLive(Duration.ofSeconds(30))
                         .evictIdleConnections(TimeValue.ofSeconds(60))
        """;

    private const string SecurityPatch =
        """
        diff --git a/src/main/java/com/example/config/SecurityConfiguration.java b/src/main/java/com/example/config/SecurityConfiguration.java
        --- a/src/main/java/com/example/config/SecurityConfiguration.java
        +++ b/src/main/java/com/example/config/SecurityConfiguration.java
        @@ -20,6 +20,7 @@
        -public class SecurityConfiguration extends WebSecurityConfigurerAdapter {
        +public class SecurityConfiguration {
        +    @Bean
        +    public SecurityFilterChain securityFilterChain(HttpSecurity http) throws Exception {
        """;

    private const string WorkflowPatch =
        """
        diff --git a/.github/workflows/baseline-build.yml b/.github/workflows/baseline-build.yml
        --- a/.github/workflows/baseline-build.yml
        +++ b/.github/workflows/baseline-build.yml
        @@ -9,1 +9,1 @@
        -          java-version: '11'
        +          java-version: '21'
        """;

    [Fact]
    public void Detect_RequestMappingChanges_AreFlagged()
    {
        var flags = ReviewFlagDetector.Detect([new ReviewPatch("02-remediation-attempt-1", AiWritten: true, MappingPatch)]);

        var flag = Assert.Single(flags, flag => flag.Kind == ReviewFlagKind.RequestMappingChanged);
        Assert.Equal("src/main/java/com/example/web/OrderController.java", flag.Path);
        Assert.Contains("@PostMapping({\"\", \"/\"})", flag.Detail);
        Assert.Contains("security rules", flag.Detail);
    }

    [Fact]
    public void Detect_CallsTheAiRemovedAndNeverReAdded_AreFlagged()
    {
        var flags = ReviewFlagDetector.Detect([new ReviewPatch("04-remediation-attempt-3", AiWritten: true, HttpClientPatch)]);

        var flag = Assert.Single(flags, flag => flag.Kind == ReviewFlagKind.CallRemoved);
        Assert.Contains("setConnectionTimeToLive", flag.Detail);
        Assert.Equal("04-remediation-attempt-3", flag.Stage);
    }

    [Fact]
    public void Detect_SeveralRemovedCallsInAFile_AreOneFlagListingThemAndIgnoreShortBuilderNames()
    {
        const string patch =
            """
            diff --git a/src/main/java/com/example/config/InventoryClientConfiguration.java b/src/main/java/com/example/config/InventoryClientConfiguration.java
            --- a/src/main/java/com/example/config/InventoryClientConfiguration.java
            +++ b/src/main/java/com/example/config/InventoryClientConfiguration.java
            @@ -1,4 +1,1 @@
            -                .setMaxConnTotal(50)
            -                .setConnectionTimeToLive(30, TimeUnit.SECONDS)
            -                .and()
            +                .build();
            """;

        var flags = ReviewFlagDetector.Detect([new ReviewPatch("02-remediation-attempt-1", AiWritten: true, patch)]);

        var flag = Assert.Single(flags);
        Assert.Contains("setMaxConnTotal, setConnectionTimeToLive", flag.Detail);
        Assert.DoesNotContain("'and'", flag.Detail);
        Assert.DoesNotContain(" and,", flag.Detail);
    }

    [Fact]
    public void Detect_CallRemovedInOneAttemptButAddedBackInALaterOne_IsNotFlagged()
    {
        const string restore =
            """
            diff --git a/src/main/java/com/example/config/InventoryClientConfiguration.java b/src/main/java/com/example/config/InventoryClientConfiguration.java
            --- a/src/main/java/com/example/config/InventoryClientConfiguration.java
            +++ b/src/main/java/com/example/config/InventoryClientConfiguration.java
            @@ -1,1 +1,2 @@
            +        connectionConfig.setConnectionTimeToLive(TimeValue.ofSeconds(30));
            """;

        var flags = ReviewFlagDetector.Detect(
        [
            new ReviewPatch("02-remediation-attempt-1", AiWritten: true, HttpClientPatch),
            new ReviewPatch("03-remediation-attempt-2", AiWritten: true, restore)
        ]);

        Assert.DoesNotContain(flags, flag => flag.Kind == ReviewFlagKind.CallRemoved);
    }

    [Fact]
    public void Detect_SecurityConfigurationChangedByTheAi_IsFlagged()
    {
        var flags = ReviewFlagDetector.Detect([new ReviewPatch("02-remediation-attempt-1", AiWritten: true, SecurityPatch)]);

        var flag = Assert.Single(flags, flag => flag.Kind == ReviewFlagKind.SecurityChanged);
        Assert.Equal("src/main/java/com/example/config/SecurityConfiguration.java", flag.Path);
    }

    [Fact]
    public void Detect_CiFileChanges_AreFlaggedEvenWhenOpenRewriteMadeThem()
    {
        var flags = ReviewFlagDetector.Detect([new ReviewPatch("01-openrewrite", AiWritten: false, WorkflowPatch)]);

        var flag = Assert.Single(flags);
        Assert.Equal(ReviewFlagKind.CiConfigurationChanged, flag.Kind);
        Assert.Equal(".github/workflows/baseline-build.yml", flag.Path);
    }

    [Fact]
    public void Detect_OpenRewriteChanges_AreNotFlaggedForRemovedCallsOrSecurity()
    {
        // Recipes are deterministic and reviewed upstream; flags target AI edits.
        var flags = ReviewFlagDetector.Detect(
        [
            new ReviewPatch("01-openrewrite", AiWritten: false, HttpClientPatch),
            new ReviewPatch("01-openrewrite", AiWritten: false, SecurityPatch)
        ]);

        Assert.Empty(flags);
    }

    [Fact]
    public void Detect_LargeAiAdditionsToAFile_AreFlagged()
    {
        var added = string.Join("\n", Enumerable.Range(0, ReviewFlagDetector.LargeAdditionLines)
            .Select(index => $"+        store.put(key{index}, value);"));

        var patch =
            "diff --git a/src/main/java/a/CacheConfiguration.java b/src/main/java/a/CacheConfiguration.java\n" +
            "--- a/src/main/java/a/CacheConfiguration.java\n" +
            "+++ b/src/main/java/a/CacheConfiguration.java\n" +
            "@@ -1,1 +1,40 @@\n" + added;

        var flag = Assert.Single(
            ReviewFlagDetector.Detect([new ReviewPatch("03-remediation-attempt-2", AiWritten: true, patch)]),
            flag => flag.Kind == ReviewFlagKind.LargeAddition);

        Assert.Contains($"{ReviewFlagDetector.LargeAdditionLines} lines", flag.Detail);
        Assert.Contains("re-implementing", flag.Detail);
    }

    [Fact]
    public void UnifiedDiff_ParsesFilesAndChangedLines()
    {
        var files = UnifiedDiff.Parse(MappingPatch + "\n" + HttpClientPatch);

        Assert.Equal(2, files.Count);
        Assert.Equal(["    @GetMapping", "    @PostMapping"], files[0].RemovedLines);
        Assert.Equal(["    @GetMapping({\"\", \"/\"})", "    @PostMapping({\"\", \"/\"})"], files[0].AddedLines);
        Assert.Equal("src/main/java/com/example/config/InventoryClientConfiguration.java", files[1].Path);
    }
}
