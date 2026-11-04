using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace SpringModernisationAgent.Remediation;

/// <summary>
/// Guardrails on what an edit may not take away or bring in, whatever the
/// code looks like (runs 20 and 21 found new shapes for the same
/// workaround each time):
/// <list type="bullet">
/// <item>no new code against a library the target platform dropped;</item>
/// <item>no removing Spring annotations that carry behaviour;</item>
/// <item>no changing or deleting existing configuration values.</item>
/// </list>
/// </summary>
public sealed partial class RemediationPatchApplier
{
    /// <summary>Annotations whose removal deletes behaviour a green build cannot vouch for.</summary>
    public static IReadOnlyList<string> BehaviourAnnotations { get; } =
    [
        "Cacheable", "CachePut", "CacheEvict", "Caching", "EnableCaching",
        "Transactional",
        "PreAuthorize", "PostAuthorize", "PreFilter", "PostFilter", "Secured", "RolesAllowed",
        "Async", "EnableAsync", "Scheduled", "EnableScheduling",
        "Validated", "Valid", "Retryable"
    ];

    private static string? CheckUnsupportedLibraries(
        string relativePath,
        string strippedBefore,
        string strippedAfter)
    {
        foreach (var library in UnsupportedLibraries.All)
        {
            var marker = library.Package + ".";

            if (Occurrences(strippedAfter, marker) > Occurrences(strippedBefore, marker))
            {
                return $"'{relativePath}': adds code that uses {library.Name} ({library.Package}), which the " +
                       "target platform no longer supports. Replacing it is a design decision: choose " +
                       $"NeedsHumanDecision and list the supported replacements ({library.Replacements}).";
            }
        }

        return null;
    }

    /// <summary>Counted over the whole proposal, so moving an annotation between files is allowed.</summary>
    private static string? CheckBehaviourAnnotations(
        IReadOnlyList<(string Before, string After)> strippedSources)
    {
        static Dictionary<string, int> Count(IEnumerable<string> sources) =>
            sources
                .SelectMany(source => AnnotationUse().Matches(source))
                .Select(match => match.Groups[1].Value)
                .Where(BehaviourAnnotationSet.Contains)
                .GroupBy(name => name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var before = Count(strippedSources.Select(source => source.Before));
        var after = Count(strippedSources.Select(source => source.After));

        var removed = before
            .Where(entry => after.GetValueOrDefault(entry.Key) < entry.Value)
            .Select(entry => "@" + entry.Key)
            .Order(StringComparer.Ordinal)
            .ToList();

        return removed.Count == 0
            ? null
            : $"the proposal removes {string.Join(", ", removed)}, which deletes behaviour (caching, transactions, " +
              "security, scheduling or validation). Keep the annotation and fix what it depends on, or choose " +
              "NeedsHumanDecision.";
    }

    /// <summary>
    /// Existing settings keep their values. New keys may be added, and a key
    /// may be renamed (its value moves to a key that did not exist before).
    /// </summary>
    private static string? CheckSettings(
        string relativePath,
        string before,
        string after)
    {
        var extension = Path.GetExtension(relativePath).ToLowerInvariant();

        if (before.Length == 0 || extension is not (".properties" or ".yml" or ".yaml"))
        {
            return null;
        }

        var parse = extension == ".properties"
            ? (Func<string, IReadOnlyDictionary<string, string>?>)ParseProperties
            : ParseYaml;

        var original = parse(before);

        if (original is null)
        {
            return null;
        }

        var updated = parse(after);

        if (updated is null)
        {
            return $"'{relativePath}': the edited file is not valid YAML.";
        }

        // Keys the edit added: a removed key may only have been renamed to one of these.
        var added = updated
            .Where(entry => !original.ContainsKey(entry.Key))
            .ToList();

        foreach (var (key, value) in original)
        {
            if (updated.TryGetValue(key, out var now))
            {
                if (!string.Equals(now, value, StringComparison.Ordinal))
                {
                    return $"'{relativePath}': changes the setting {Display(key)} from '{value}' to '{now}'. " +
                           "Existing settings keep their values (you may rename a key or add new ones); " +
                           "changing what a setting does is a decision for a person.";
                }

                continue;
            }

            var rename = added.FindIndex(entry => IsRename(key, value, entry.Key, entry.Value));

            if (rename < 0)
            {
                return $"'{relativePath}': removes the setting {Display(key)} ('{value}'). " +
                       "Existing settings keep their values (you may rename a key, keeping its last part " +
                       "and value, or add new ones).";
            }

            added.RemoveAt(rename);
        }

        return null;
    }

    /// <summary>
    /// A rename keeps the value, the document and the key's last part, as
    /// Spring's own renames do (<c>spring.redis.host</c> to
    /// <c>spring.data.redis.host</c>). Moving a value to an unrelated key
    /// would switch the setting off, so it is not a rename.
    /// </summary>
    private static bool IsRename(
        string oldKey,
        string value,
        string newKey,
        string newValue)
    {
        static (string Document, string Last) Parts(string key)
        {
            var separator = key.IndexOf('|');
            var path = key[(separator + 1)..];
            var last = path[(path.LastIndexOf('.') + 1)..];
            return (key[..(separator + 1)], last);
        }

        return string.Equals(value, newValue, StringComparison.Ordinal)
               && Parts(oldKey) == Parts(newKey);
    }

    private static string Display(
        string key)
    {
        // Documents after the first are shown with their number.
        var separator = key.IndexOf('|');
        return separator < 0 ? key
            : key[..separator] == "0" ? key[(separator + 1)..]
            : $"{key[(separator + 1)..]} (document {int.Parse(key[..separator]) + 1})";
    }

    private static IReadOnlyDictionary<string, string> ParseProperties(
        string text)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.Trim();

            if (line.Length == 0 || line[0] is '#' or '!')
            {
                continue;
            }

            var separator = line.IndexOfAny(['=', ':']);

            if (separator > 0)
            {
                values["0|" + line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
        }

        return values;
    }

    /// <summary>Flattened <c>document|a.b[0].c</c> keys to scalar values; null if it is not valid YAML.</summary>
    private static IReadOnlyDictionary<string, string>? ParseYaml(
        string text)
    {
        var stream = new YamlStream();

        try
        {
            stream.Load(new StringReader(text));
        }
        catch (Exception exception)
            when (exception is YamlException or ArgumentException or InvalidOperationException)
        {
            // Duplicate keys surface as ArgumentException from the node dictionary.
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        void Walk(YamlNode node, string prefix, string path)
        {
            switch (node)
            {
                case YamlMappingNode mapping:
                    foreach (var (key, child) in mapping.Children)
                    {
                        var name = (key as YamlScalarNode)?.Value ?? key.ToString();
                        Walk(child, prefix, path.Length == 0 ? name : $"{path}.{name}");
                    }

                    break;

                case YamlSequenceNode sequence:
                    for (var index = 0; index < sequence.Children.Count; index++)
                    {
                        Walk(sequence.Children[index], prefix, $"{path}[{index}]");
                    }

                    break;

                case YamlScalarNode scalar:
                    values[prefix + path] = scalar.Value ?? string.Empty;
                    break;
            }
        }

        for (var index = 0; index < stream.Documents.Count; index++)
        {
            Walk(stream.Documents[index].RootNode, $"{index}|", string.Empty);
        }

        return values;
    }

    private static int Occurrences(
        string text,
        string value)
    {
        var count = 0;

        for (var index = text.IndexOf(value, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static readonly HashSet<string> BehaviourAnnotationSet = new(BehaviourAnnotations, StringComparer.Ordinal);

    // @Cacheable, @Cacheable("x"), @org.springframework.cache.annotation.Cacheable
    [GeneratedRegex(@"@(?:[A-Za-z_]\w*\.)*([A-Z]\w*)\b")]
    private static partial Regex AnnotationUse();
}
