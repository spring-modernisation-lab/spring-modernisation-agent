using SpringModernisationAgent.Models;
using SpringModernisationAgent.Remediation;
using SpringModernisationAgent.Reporting;
using SpringModernisationAgent.Triage;

namespace SpringModernisationAgent.Tests.Triage;

public class ProblemClassifierTests
{
    private static readonly ProblemKnowledge Knowledge =
        ProblemKnowledge.LoadDefault(UnsupportedLibraries.All.Select(library => library.Name).ToList());

    private static readonly string[] Ehcache = ["Ehcache 2"];

    // Probe #5: what OpenRewrite leaves in orders-service (shapes of the real errors).
    private static readonly string[] AfterOpenRewrite =
    [
        "[ERROR] /w/r/src/main/java/a/config/SecurityConfiguration.java:[12,72] cannot find symbol",
        "  symbol:   class WebSecurityConfigurerAdapter",
        "[ERROR] /w/r/src/main/java/a/domain/CustomerOrder.java:[25,24] incompatible types: cannot be converted to java.lang.Class<? extends org.hibernate.usertype.UserType<?>>",
        "[ERROR] /w/r/src/main/java/a/config/InventoryClientConfiguration.java:[28,17] cannot find symbol",
        "  symbol:   method setMaxConnTotal(int)",
        "  location: class org.apache.hc.client5.http.impl.classic.HttpClientBuilder",
        "[ERROR] /w/r/src/main/java/a/config/SecurityConfiguration.java:[23,5] method does not override or implement a method from a supertype",
        "[ERROR] /w/r/src/main/java/a/persistence/MoneyUserType.java:[17,8] a.MoneyUserType is not abstract and does not override abstract method nullSafeGet(java.sql.ResultSet,int) in org.hibernate.usertype.UserType"
    ];

    // Run 24 attempt 1: the tests once the code compiles.
    private static readonly string[] TestFailures =
    [
        "FAILED TEST a.inventory.InventoryCachingTests.availabilityIsCachedAndRefreshedAfterItsTimeToLive (error): IllegalStateException: Failed to load ApplicationContext",
        "  root cause: org.springframework.beans.factory.NoSuchBeanDefinitionException: No bean named 'cacheManager' available",
        "FAILED TEST a.web.OrderControllerTests.anonymousRequestsAreRejected (error): IllegalStateException: ApplicationContext failure threshold (1) exceeded",
        "FAILED TEST a.web.OrderControllerTests.operatorCanCreateOrderAndViewerCanReadItBack (error): IllegalStateException: Failed to load ApplicationContext",
        "  root cause: java.lang.ClassNotFoundException: javax.servlet.http.HttpServletRequest",
        "FAILED TEST a.web.OrderControllerTests.legacyClientsCanListOrdersUsingTrailingSlash (failure): AssertionError: Status expected:<200> but was:<404>"
    ];

    [Fact]
    public void Classify_WhatOpenRewriteLeaves_IsThreeApiMovesWithTheirMappings()
    {
        var problems = ProblemClassifier.Classify(Knowledge, AfterOpenRewrite, Ehcache);

        // A file's errors form one problem, so "does not override" follows its file.
        Assert.Equal(
            [
                ("src/main/java/a/config/SecurityConfiguration.java", "M-SECURITY-6-FILTERCHAIN"),
                ("src/main/java/a/domain/CustomerOrder.java", "M-HIBERNATE-6-USERTYPE"),
                ("src/main/java/a/config/InventoryClientConfiguration.java", "M-HTTPCLIENT-5-POOL"),
                ("src/main/java/a/persistence/MoneyUserType.java", "M-HIBERNATE-6-USERTYPE")
            ],
            problems.Select(problem => (problem.Subject, problem.Mapping!)));

        Assert.All(problems, problem => Assert.Equal(ProblemKind.ApiMove, problem.Kind));
        Assert.Equal(["P1", "P2", "P3", "P4"], problems.Select(problem => problem.Id));
        Assert.Equal(2, problems[0].Evidence.Count(line => line.StartsWith("[ERROR]", StringComparison.Ordinal)));
    }

    [Fact]
    public void Classify_TestFailures_GetTheirKindsAndKnockOnsAreSeparated()
    {
        var problems = ProblemClassifier.Classify(Knowledge, TestFailures, Ehcache);

        Assert.Equal(
            [
                (ProblemKind.PlatformDropped, "a.inventory.InventoryCachingTests.availabilityIsCachedAndRefreshedAfterItsTimeToLive"),
                (ProblemKind.KnockOn, "a.web.OrderControllerTests.anonymousRequestsAreRejected"),
                (ProblemKind.Mechanical, "a.web.OrderControllerTests.operatorCanCreateOrderAndViewerCanReadItBack"),
                (ProblemKind.Contract, "a.web.OrderControllerTests.legacyClientsCanListOrdersUsingTrailingSlash")
            ],
            problems.Select(problem => (problem.Kind, problem.Subject)));

        Assert.Equal("Ehcache 2", problems[0].Library);
        Assert.Equal("lab.jakarta.ServletStringLiterals", problems[2].Recipe);
    }

    [Fact]
    public void Classify_APlatformDroppedSignatureAppliesOnlyWhenTheRepositoryUsesThatLibrary()
    {
        var problems = ProblemClassifier.Classify(Knowledge, TestFailures.Take(2).ToArray(), unsupportedLibraries: []);

        Assert.Equal(ProblemKind.Unknown, problems[0].Kind);
    }

    [Fact]
    public void Classify_APortsFollowUpErrorsInheritItsKindWhenThePreviousClassificationIsGiven()
    {
        var initial = ProblemClassifier.Classify(Knowledge, AfterOpenRewrite, Ehcache);

        // Run 14 attempt 1: the Hibernate port left an unused import.
        string[] followUp = ["[ERROR] /w/r/src/main/java/a/persistence/MoneyUserType.java:[14,32] package org.apache.commons.lang3 does not exist"];

        Assert.Equal(ProblemKind.Unknown, ProblemClassifier.Classify(Knowledge, followUp, Ehcache).Single().Kind);

        var problem = ProblemClassifier.Classify(Knowledge, followUp, Ehcache, initial).Single();

        Assert.Equal(ProblemKind.ApiMove, problem.Kind);
        Assert.Equal("M-HIBERNATE-6-USERTYPE", problem.Mapping);
        Assert.Equal("S-HIBERNATE-USERTYPE (follow-up)", problem.SignatureId);
        Assert.Equal(followUp, problem.Evidence);
    }

    [Fact]
    public void Classify_AFailureWithNoFileOrTest_IsStillOneUnknownProblem()
    {
        var problem = ProblemClassifier.Classify(Knowledge, ["[ERROR] Non-resolvable parent POM for lab:orders-service"], Ehcache).Single();

        Assert.Equal(ProblemKind.Unknown, problem.Kind);
        Assert.Equal("build", problem.Subject);
    }

    [Fact]
    public void ShippedKnowledge_EveryApiMoveHasNotesAndWhatItMustKeep()
    {
        Assert.Equal(["M-HIBERNATE-6-USERTYPE", "M-HTTPCLIENT-5-POOL", "M-SECURITY-6-FILTERCHAIN"], Knowledge.Mappings.Keys.Order());
        Assert.All(Knowledge.Mappings.Values, mapping => Assert.NotEmpty(mapping.MustKeep));
        Assert.Contains(Knowledge.Mappings["M-HTTPCLIENT-5-POOL"].Notes, note => note.Contains("ConnectionConfig", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("kind: api-move\n    mapping: M-NOPE\n    contains: [\"x\"]", "needs a mapping defined in api-mappings.yaml")]
    [InlineData("kind: mechanical\n    recipe: lab.Nope\n    contains: [\"x\"]", "needs a recipe the pipeline has")]
    [InlineData("kind: platform-dropped\n    library: \"Nope\"\n    contains: [\"x\"]", "needs a library from unsupported-libraries.yaml")]
    [InlineData("kind: guess\n    contains: [\"x\"]", "needs a kind")]
    [InlineData("kind: contract\n    pattern: [\"(unclosed\"]", "has an invalid pattern")]
    [InlineData("kind: contract", "needs contains or pattern")]
    public void Parse_AMistakeInASignatureStopsTheRunNamingIt(
        string body,
        string expected)
    {
        const string mappings = "mappings:\n  - id: M-X\n    name: x\n    notes: [\"n\"]\n";

        var exception = Assert.Throws<InvalidOperationException>(() => ProblemKnowledge.Parse(
            $"signatures:\n  - id: S-TEST\n    {body}\n",
            mappings,
            knownLibraries: ["Ehcache 2"],
            knownRecipes: ["lab.jakarta.ServletStringLiterals"]));

        Assert.Contains("signature 1 ('S-TEST')", exception.Message);
        Assert.Contains(expected, exception.Message);
    }

    [Fact]
    public void JobSummary_ShowsWhatTheRecipesLeftByKindAndWhoHandlesIt()
    {
        var problems = ProblemClassifier.Classify(Knowledge, [.. AfterOpenRewrite, .. TestFailures], Ehcache);

        var markdown = TransformationSummaryWriter.Render(
        [
            new TransformationExecutionResult(
                new TransformationResult("orders-service", "abc", TransformationStatus.Succeeded, [], ["pom.xml"], null, null),
                new TransformationValidationResult("orders-service", "abc", TransformationValidationStatus.Failed,
                    [new TransformationValidationCheckResult("compile", false, null, null)]))
            {
                Problems = problems
            }
        ]);

        Assert.Contains("### orders-service: problems left by the recipes", markdown);
        Assert.Contains("| ApiMove (4) | AI port, scoped and checked | SecurityConfiguration.java (M-SECURITY-6-FILTERCHAIN); CustomerOrder.java, MoneyUserType.java (M-HIBERNATE-6-USERTYPE); InventoryClientConfiguration.java (M-HTTPCLIENT-5-POOL) |", markdown);
        Assert.Contains("| PlatformDropped (1) | a person (decision) | InventoryCachingTests.availabilityIsCachedAndRefreshedAfterItsTimeToLive (Ehcache 2) |", markdown);
        Assert.Contains("| KnockOn (1) | re-checked after the others |", markdown);
    }
}
