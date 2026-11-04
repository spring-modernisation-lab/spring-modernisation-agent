using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace SpringModernisationAgent.Recipes;

/// <summary>One recipe, as published in a module's JAR.</summary>
/// <param name="Children">For a declarative recipe, the recipes it runs, in order.</param>
/// <param name="Mentions">
/// Type names, packages and other identifiers the recipe names: from its
/// declarative options (for example <c>oldPackageName: javax.servlet</c>)
/// and from code spans in its description (for example <c>`UserType`</c>).
/// </param>
public sealed record RecipeEntry(
    string Name,
    string DisplayName,
    string Description,
    RecipeModule Module,
    IReadOnlyList<string> Children,
    IReadOnlyList<string> Mentions);

/// <summary>
/// Every recipe in the indexed modules, read deterministically from the JARs:
/// <c>META-INF/rewrite/recipes.csv</c> lists every recipe (declarative or
/// written in Java) and the declarative YAML says what each one runs.
/// </summary>
public sealed partial class RecipeCatalog
{
    private const string RecipeType = "specs.openrewrite.org/v1beta/recipe";

    private readonly Dictionary<string, RecipeEntry> _recipes;

    private RecipeCatalog(
        Dictionary<string, RecipeEntry> recipes,
        IReadOnlyList<string> warnings)
    {
        _recipes = recipes;
        Warnings = warnings;
    }

    public IReadOnlyCollection<RecipeEntry> Recipes => _recipes.Values;

    /// <summary>Problems reading a JAR that did not stop the index (for example, a YAML file that does not parse).</summary>
    public IReadOnlyList<string> Warnings { get; }

    public RecipeEntry? Find(
        string name) =>
        _recipes.GetValueOrDefault(name);

    public static RecipeCatalog FromJars(
        IEnumerable<(RecipeModule Module, string JarPath)> jars)
    {
        ArgumentNullException.ThrowIfNull(jars);

        var recipes = new Dictionary<string, RecipeEntry>(StringComparer.Ordinal);
        var warnings = new List<string>();

        foreach (var (module, jarPath) in jars)
        {
            using var archive = ZipFile.OpenRead(jarPath);

            foreach (var recipe in ReadModule(archive, module, warnings))
            {
                // A recipe published by two modules keeps its first (more central) source.
                recipes.TryAdd(recipe.Name, recipe);
            }
        }

        return new RecipeCatalog(recipes, warnings);
    }

    /// <summary>
    /// Every recipe the given roots run, directly or through composites,
    /// for example the 690 recipes under <c>UpgradeSpringBoot_3_5</c>.
    /// The roots themselves are included.
    /// </summary>
    public IReadOnlySet<string> ReachableFrom(
        IEnumerable<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(roots);

        while (pending.TryPop(out var name))
        {
            if (!seen.Add(name))
            {
                continue;
            }

            foreach (var child in _recipes.GetValueOrDefault(name)?.Children ?? [])
            {
                pending.Push(child);
            }
        }

        return seen;
    }

    internal static IReadOnlyList<RecipeEntry> ReadModule(
        ZipArchive archive,
        RecipeModule module,
        List<string> warnings)
    {
        var declarative = new Dictionary<string, (List<string> Children, List<string> Mentions)>(StringComparer.Ordinal);

        foreach (var entry in archive.Entries.Where(entry =>
                     entry.FullName.StartsWith("META-INF/rewrite/", StringComparison.Ordinal)
                     && entry.FullName.EndsWith(".yml", StringComparison.Ordinal)))
        {
            var text = ReadText(entry);

            try
            {
                ReadDeclarative(text, declarative);
            }
            catch (YamlException)
            {
                // rewrite-spring 6.37.1's spring-boot-20-properties.yml wraps
                // quoted strings onto lines in column 0, which strict YAML
                // rejects and OpenRewrite's own parser accepts. Such a line
                // can only be a continuation: indent it and try again.
                var repaired = ColumnZeroContinuation().Replace(text, "        $1");

                try
                {
                    ReadDeclarative(repaired, declarative);
                    continue;
                }
                catch (YamlException)
                {
                    // Still bad: read document by document below.
                }

                // Skip only the documents that still don't parse.
                var documents = DocumentSeparator().Split(repaired);

                for (var index = 0; index < documents.Length; index++)
                {
                    try
                    {
                        ReadDeclarative(documents[index], declarative);
                    }
                    catch (YamlException exception)
                    {
                        warnings.Add(
                            $"{module.Coordinates}: {entry.FullName} document {index + 1} was skipped ({exception.Message}).");
                    }
                }
            }
        }

        var listing = archive.GetEntry("META-INF/rewrite/recipes.csv");

        if (listing is null)
        {
            warnings.Add($"{module.Coordinates}: no META-INF/rewrite/recipes.csv; only declarative recipes were read.");
        }

        var rows = listing is null ? [] : Csv.Parse(ReadText(listing));
        var header = rows.Count == 0 ? [] : rows[0];
        int Column(string name) => Array.IndexOf(header, name);

        var (nameColumn, displayColumn, descriptionColumn) = (Column("name"), Column("displayName"), Column("description"));
        var result = new List<RecipeEntry>();
        var listed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows.Skip(1))
        {
            if (nameColumn < 0 || row.Length <= nameColumn || row[nameColumn].Length == 0)
            {
                continue;
            }

            var name = row[nameColumn];
            var description = descriptionColumn >= 0 && row.Length > descriptionColumn ? row[descriptionColumn] : string.Empty;
            var display = displayColumn >= 0 && row.Length > displayColumn ? row[displayColumn] : name;

            listed.Add(name);
            result.Add(Build(name, display, description, module, declarative.GetValueOrDefault(name)));
        }

        // Declarative recipes missing from the listing are still indexed.
        foreach (var (name, parts) in declarative.Where(entry => !listed.Contains(entry.Key)))
        {
            result.Add(Build(name, name, string.Empty, module, parts));
        }

        return result;
    }

    private static RecipeEntry Build(
        string name,
        string displayName,
        string description,
        RecipeModule module,
        (List<string> Children, List<string> Mentions)? declarative)
    {
        var mentions = new SortedSet<string>(StringComparer.Ordinal);

        foreach (Match span in CodeSpan().Matches(displayName + " " + description))
        {
            foreach (Match identifier in Identifier().Matches(span.Groups[1].Value))
            {
                mentions.Add(identifier.Value);
            }
        }

        foreach (var mention in declarative?.Mentions ?? [])
        {
            mentions.Add(mention);
        }

        return new RecipeEntry(
            name,
            displayName,
            description,
            module,
            declarative?.Children ?? [],
            mentions.ToList());
    }

    private static void ReadDeclarative(
        string yaml,
        Dictionary<string, (List<string> Children, List<string> Mentions)> declarative)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));

        foreach (var document in stream.Documents)
        {
            if (document.RootNode is not YamlMappingNode root
                || Scalar(root, "type") != RecipeType
                || Scalar(root, "name") is not { Length: > 0 } name)
            {
                continue;
            }

            var children = new List<string>();
            var mentions = new List<string>();

            if (root.Children.TryGetValue(new YamlScalarNode("recipeList"), out var list) && list is YamlSequenceNode items)
            {
                foreach (var item in items)
                {
                    switch (item)
                    {
                        case YamlScalarNode { Value: { Length: > 0 } child }:
                            children.Add(child);
                            break;

                        case YamlMappingNode { Children.Count: > 0 } configured:
                            var (key, options) = configured.Children.First();

                            if (key is YamlScalarNode { Value: { Length: > 0 } configuredName })
                            {
                                children.Add(configuredName);
                                CollectMentions(options, mentions);
                            }

                            break;
                    }
                }
            }

            declarative[name] = (children, mentions);
        }
    }

    /// <summary>Dotted names in option values: types, packages, and the type part of method patterns.</summary>
    private static void CollectMentions(
        YamlNode node,
        List<string> mentions)
    {
        switch (node)
        {
            case YamlScalarNode { Value: { } value }:
                mentions.AddRange(DottedName().Matches(value).Select(match => match.Value));
                break;

            case YamlMappingNode mapping:
                foreach (var (_, child) in mapping.Children)
                {
                    CollectMentions(child, mentions);
                }

                break;

            case YamlSequenceNode sequence:
                foreach (var child in sequence)
                {
                    CollectMentions(child, mentions);
                }

                break;
        }
    }

    private static string? Scalar(
        YamlMappingNode node,
        string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? (value as YamlScalarNode)?.Value : null;

    private static string ReadText(
        ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"(?m)^---[ \t]*\r?$")]
    private static partial Regex DocumentSeparator();

    // A line in column 0 that is not "---", a comment, or a top-level "key:".
    [GeneratedRegex(@"(?m)^(?!---|#|[A-Za-z_][\w-]*:)(\S.*)$")]
    private static partial Regex ColumnZeroContinuation();

    // `UserType`, `org.apache.http.client.HttpClient`, `setMaxConnTotal(int)`
    [GeneratedRegex(@"`([^`\n]{1,200})`")]
    private static partial Regex CodeSpan();

    [GeneratedRegex(@"[A-Za-z_$][\w$]*(?:\.[A-Za-z_$][\w$]*)*")]
    private static partial Regex Identifier();

    // At least one dot: org.hibernate.usertype.UserType, javax.servlet
    [GeneratedRegex(@"[A-Za-z_$][\w$]*(?:\.[A-Za-z_$][\w$]*)+")]
    private static partial Regex DottedName();
}

/// <summary>RFC 4180 CSV: quoted fields may contain commas, quotes ("") and line breaks.</summary>
internal static class Csv
{
    public static List<string[]> Parse(
        string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];

            if (quoted)
            {
                if (character == '"' && index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else if (character == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(character);
                }

                continue;
            }

            switch (character)
            {
                case '"' when field.Length == 0:
                    quoted = true;
                    break;

                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;

                case '\r':
                    break;

                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add([.. row]);
                    row.Clear();
                    break;

                default:
                    field.Append(character);
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add([.. row]);
        }

        return rows;
    }
}
