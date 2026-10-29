using SpringModernisationAgent.Analysis;
using SpringModernisationAgent.Config;
using SpringModernisationAgent.GitHub;
using SpringModernisationAgent.Maven;
using SpringModernisationAgent.Models;

var token =
    Environment.GetEnvironmentVariable("GITHUB_TOKEN")
    ?? throw new InvalidOperationException(
        "GITHUB_TOKEN environment variable isn't set.");

var organisation =
    Environment.GetEnvironmentVariable("GITHUB_ORGANISATION")
    ?? throw new InvalidOperationException(
        "GITHUB_ORGANISATION environment variable isn't set.");

var provider =
    new GitHubRepositoryProvider(token);

var parser =
    new MavenPomParser();

var repositories =
    await provider.GetRepositoriesAsync(organisation);

var inventories =
    new List<RepositoryInventory>();

foreach (var repository in repositories.OrderBy(r => r.Name))
{
    var pom = await provider.GetFileContentAsync(
        organisation,
        repository.Name,
        "pom.xml");

    if (pom is null)
    {
        Console.WriteLine(
            $"{repository.Name}: not a Maven project");

        continue;
    }

    try
    {
        var project = parser.Parse(pom);

        inventories.Add(
            new RepositoryInventory(
                repository.Name,
                project));
    }
    catch (Exception exception)
    {
        Console.WriteLine(
            $"Unable to analyse {repository.Name}: " +
            exception.Message);
    }
}

var graphBuilder =
    new DependencyGraphBuilder();

var graph =
    graphBuilder.Build(inventories);

Console.WriteLine();
Console.WriteLine("Repository dependency graph");
Console.WriteLine("===========================");

foreach (var edge in graph.Edges)
{
    Console.WriteLine(
        $"{edge.FromRepository} " +
        $"--{edge.Type}--> " +
        $"{edge.ToRepository} " +
        $"[{edge.Version ?? "unknown"}]");
}

var repositoriesByArtifact =
    inventories.ToDictionary(
        inventory =>
            inventory.MavenProject.Coordinate.Key,
        inventory => inventory,
        StringComparer.OrdinalIgnoreCase);

var springAnalyzer =
    new SpringPlatformAnalyzer();

var assessments =
    new List<RepositoryAssessment>();

var repositoryClassifier = new RepositoryClassifier();

foreach (var inventory in inventories)
{
    var kind = repositoryClassifier.Classify(inventory.MavenProject);

    var spring =
        springAnalyzer.Analyze(
            inventory,
            repositoriesByArtifact);

    var internalEdges =
        graph.Edges
            .Where(edge =>
                edge.FromRepository.Equals(
                    inventory.RepositoryName,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

    var internalArtifactKeys =
        internalEdges
            .Select(edge =>
                inventories.Single(
                    candidate =>
                        candidate.RepositoryName.Equals(
                            edge.ToRepository,
                            StringComparison.OrdinalIgnoreCase)))
            .Select(candidate =>
                candidate.MavenProject.Coordinate.Key)
            .ToHashSet(
                StringComparer.OrdinalIgnoreCase);

    var externalDependencies =
        inventory.MavenProject.Dependencies
            .Where(dependency =>
                !internalArtifactKeys.Contains(
                    dependency.Coordinate.Key))
            .ToList();

    assessments.Add(
        new RepositoryAssessment(
            RepositoryName: inventory.RepositoryName,
            Kind: kind,
            MavenProject: inventory.MavenProject,
            Spring: spring,
            InternalDependencies: internalEdges,
            ExternalDependencies: externalDependencies));
}

Console.WriteLine();
Console.WriteLine("Technology inventory");
Console.WriteLine("====================");

foreach (var assessment in
         assessments.OrderBy(a => a.RepositoryName))
{
    Console.WriteLine();
    Console.WriteLine(assessment.RepositoryName);

    Console.WriteLine(
        $"  Spring Boot: " +
        $"{assessment.Spring.SpringBootVersion ?? "unknown"}");

    Console.WriteLine(
        $"  Java: " +
        $"{assessment.Spring.JavaVersion ?? "unknown"}");

    Console.WriteLine("  Spring starters:");

    foreach (var starter in assessment.Spring.Starters)
    {
        Console.WriteLine(
            $"    - {starter}");
    }

    Console.WriteLine("  Internal dependencies:");

    foreach (var dependency in
             assessment.InternalDependencies)
    {
        Console.WriteLine(
            $"    - {dependency.ToRepository} " +
            $"[{dependency.Type}]");
    }

    Console.WriteLine("  External dependencies:");

    foreach (var dependency in
             assessment.ExternalDependencies)
    {
        Console.WriteLine(
            $"    - {dependency.Coordinate} " +
            $"[{dependency.Scope}]");
    }
}

var migrationWaveBuilder = new MigrationWaveBuilder();

var assessmentBuilder = new ProjectAssessmentBuilder(
    migrationWaveBuilder);

var projectAssessment = assessmentBuilder.Build(
    organisation,
    assessments,
    graph);

// Load the approved Spring migration policy.
var policyPath = Path.Combine(
    AppContext.BaseDirectory,
    "Config",
    "spring.yaml");

var policyLoader = new SpringMigrationPolicyLoader();
var policy = policyLoader.Load(policyPath);

// Apply approved migration routes to each repository.
var routeSelector = new MigrationRouteSelector();
var policyAssessment = new MigrationPolicyAssessment(routeSelector);

projectAssessment = policyAssessment.Apply(
    projectAssessment,
    policy);

// Write the finished assessment.
var assessmentWriter = new ProjectAssessmentWriter();

var outputPath = Path.Combine(
    Directory.GetCurrentDirectory(),
    "Artifacts",
    "project-assessment.json");

await assessmentWriter.WriteAsync(
    projectAssessment,
    outputPath);

Console.WriteLine();
Console.WriteLine($"Project assessment written to: {outputPath}");
