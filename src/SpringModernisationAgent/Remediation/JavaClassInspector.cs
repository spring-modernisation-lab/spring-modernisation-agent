using System.Text.RegularExpressions;
using SpringModernisationAgent.Build;

namespace SpringModernisationAgent.Remediation;

/// <summary>
/// Describes the public API of a class on the project's real dependency
/// classpath, so the agent can check signatures instead of guessing them.
/// Returns text for the model; problems are returned as <c>Error:</c> text.
/// </summary>
public interface IJavaClassInspector
{
    Task<string> DescribeAsync(
        string className,
        CancellationToken cancellationToken = default);
}

/// <summary>Creates an inspector for one workspace, or null if unavailable.</summary>
public interface IJavaClassInspectorFactory
{
    Task<IJavaClassInspector?> CreateAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>javap -public</c> against the dependency classpath. The class name is
/// checked to be a plain Java name first, because javap treats arguments
/// starting with "-" as options (including JVM options via -J).
/// </summary>
public sealed partial class JavapClassInspector : IJavaClassInspector
{
    public const int MaxOutputChars = 24 * 1024;

    private readonly IJavapRunner _javap;
    private readonly string _classpath;
    private readonly string _workingDirectory;

    public JavapClassInspector(
        IJavapRunner javap,
        string classpath,
        string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(javap);
        ArgumentException.ThrowIfNullOrWhiteSpace(classpath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        _javap = javap;
        _classpath = classpath;
        _workingDirectory = workingDirectory;
    }

    public async Task<string> DescribeAsync(
        string className,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(className) || !QualifiedName().IsMatch(className))
        {
            return "Error: give a fully qualified class name, such as " +
                   "org.apache.hc.client5.http.impl.classic.HttpClientBuilder.";
        }

        var result = await _javap.RunAsync(
            _workingDirectory,
            ["-public", "-classpath", _classpath, className],
            cancellationToken);

        if (result.ExitCode != 0)
        {
            // javap's own message can include the classpath; never echo it.
            return $"Error: {className} was not found on the project's dependencies. " +
                   "Check the package name with search_source or the imports you read.";
        }

        var text = string.Join(
            '\n',
            result.StandardOutput
                .ReplaceLineEndings("\n")
                .Split('\n')
                .Where(line => !line.StartsWith("Compiled from", StringComparison.Ordinal)))
            .Trim();

        text += "\n(Inherited members are not listed; describe the class after 'extends' for those.)";

        return text.Length > MaxOutputChars
            ? text[..MaxOutputChars] + "\n... truncated."
            : text;
    }

    // a.b.C, a.b.Outer$Inner: at least one package segment, no options.
    [GeneratedRegex(@"^[A-Za-z_$][A-Za-z0-9_$]*(\.[A-Za-z_$][A-Za-z0-9_$]*)+$")]
    private static partial Regex QualifiedName();
}

/// <summary>
/// Resolves the workspace's dependency classpath once with Maven
/// (<c>dependency:build-classpath</c>) into <c>target/</c>, which is never
/// part of a patch and is hidden from the agent's file tools.
/// </summary>
public sealed class MavenClassInspectorFactory : IJavaClassInspectorFactory
{
    private const string ClasspathFile = "target/remediation-classpath.txt";

    private readonly IMavenProcessRunner _maven;
    private readonly IJavapRunner _javap;

    public MavenClassInspectorFactory(
        IMavenProcessRunner maven,
        IJavapRunner javap)
    {
        ArgumentNullException.ThrowIfNull(maven);
        ArgumentNullException.ThrowIfNull(javap);

        _maven = maven;
        _javap = javap;
    }

    public async Task<IJavaClassInspector?> CreateAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        Directory.CreateDirectory(Path.Combine(repositoryPath, "target"));

        var result = await _maven.RunAsync(
            repositoryPath,
            [
                "--batch-mode",
                "--quiet",
                "dependency:build-classpath",
                $"-Dmdep.outputFile={ClasspathFile}"
            ],
            cancellationToken);

        var path = Path.Combine(repositoryPath, ClasspathFile);

        if (result.ExitCode != 0 || !File.Exists(path))
        {
            return null;
        }

        var classpath = (await File.ReadAllTextAsync(path, cancellationToken)).Trim();

        return classpath.Length == 0
            ? null
            : new JavapClassInspector(_javap, classpath, repositoryPath);
    }
}
