using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SpringModernisationAgent.Recipes;

public enum RecipeModuleRole
{
    /// <summary>Loaded by the pinned plugin itself.</summary>
    Core,

    /// <summary>A module a route runs.</summary>
    Route,

    /// <summary>Not run by any route; only ever suggested.</summary>
    Candidate
}

/// <summary>A pinned recipe module, for example <c>org.openrewrite.recipe:rewrite-hibernate:2.25.0</c>.</summary>
public sealed record RecipeModule(
    string GroupId,
    string ArtifactId,
    string Version,
    RecipeModuleRole Role,
    string CoreVersion)
{
    public string Coordinates => $"{GroupId}:{ArtifactId}:{Version}";
}

/// <summary>The modules recipe discovery indexes (<c>Config/knowledge/recipe-sources.yaml</c>).</summary>
public sealed record RecipeSources(
    string PluginCoreVersion,
    IReadOnlyList<RecipeModule> Modules)
{
    public const string DefaultRelativePath = "Config/knowledge/recipe-sources.yaml";

    public static RecipeSources Load(
        string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The recipe sources file was not found.", path);
        }

        return Parse(File.ReadAllText(path), path);
    }

    public static RecipeSources Parse(
        string yaml,
        string source = "recipe-sources.yaml")
    {
        ArgumentNullException.ThrowIfNull(yaml);

        SourcesFile? file;

        try
        {
            file = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build()
                .Deserialize<SourcesFile>(yaml);
        }
        catch (YamlException exception)
        {
            throw new InvalidOperationException($"{source}: {exception.Message} (line {exception.Start.Line}).", exception);
        }

        if (string.IsNullOrWhiteSpace(file?.PluginCoreVersion))
        {
            throw new InvalidOperationException($"{source}: needs pluginCoreVersion.");
        }

        var modules = new List<RecipeModule>();

        foreach (var (entry, index) in (file.Modules ?? []).Select((entry, index) => (entry, index + 1)))
        {
            var label = $"{source}: module {index} ('{entry.Coordinates ?? "?"}')";
            var parts = entry.Coordinates?.Split(':') ?? [];

            if (parts.Length != 3 || parts.Any(part => part.Length == 0 || part.Contains('/') || part.Contains("..")))
            {
                throw new InvalidOperationException($"{label} needs coordinates group:artifact:version.");
            }

            if (!Enum.TryParse<RecipeModuleRole>(entry.Role, ignoreCase: true, out var role))
            {
                throw new InvalidOperationException($"{label} needs a role: core, route or candidate.");
            }

            if (string.IsNullOrWhiteSpace(entry.CoreVersion))
            {
                throw new InvalidOperationException($"{label} needs coreVersion.");
            }

            if (modules.Any(module => module.GroupId == parts[0] && module.ArtifactId == parts[1]))
            {
                throw new InvalidOperationException($"{label} is listed twice.");
            }

            modules.Add(new RecipeModule(parts[0], parts[1], parts[2], role, entry.CoreVersion));
        }

        return new RecipeSources(file.PluginCoreVersion, modules);
    }

    private sealed class SourcesFile
    {
        public string? PluginCoreVersion { get; set; }

        public List<SourcesEntry>? Modules { get; set; }
    }

    private sealed class SourcesEntry
    {
        public string? Coordinates { get; set; }

        public string? Role { get; set; }

        public string? CoreVersion { get; set; }
    }
}
