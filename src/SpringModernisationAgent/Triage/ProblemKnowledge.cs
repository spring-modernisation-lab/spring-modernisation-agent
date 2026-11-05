using System.Text.RegularExpressions;
using SpringModernisationAgent.Behaviour;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SpringModernisationAgent.Triage;

/// <summary>Which path a problem takes (docs/system-design.md §4).</summary>
public enum ProblemKind
{
    /// <summary>Fails only because another test's context failed; re-checked after other fixes.</summary>
    KnockOn,

    /// <summary>A deterministic recipe fixes it.</summary>
    Mechanical,

    /// <summary>A known API move: a scoped AI port.</summary>
    ApiMove,

    /// <summary>A library the platform dropped: a person decides.</summary>
    PlatformDropped,

    /// <summary>An HTTP status or URL changed: a person decides.</summary>
    Contract,

    /// <summary>Nothing matched: the AI may diagnose it, never edit.</summary>
    Unknown
}

/// <summary>One rule from <c>diagnostic-signatures.yaml</c>.</summary>
public sealed record DiagnosticSignature(
    string Id,
    ProblemKind Kind,
    string? Mapping,
    string? Recipe,
    string? Library,
    IReadOnlyList<string> Contains,
    IReadOnlyList<Regex> Patterns)
{
    public bool Matches(
        string evidence) =>
        Contains.Any(text => evidence.Contains(text, StringComparison.Ordinal))
        || Patterns.Any(pattern => pattern.IsMatch(evidence));
}

/// <summary>A known API move from <c>api-mappings.yaml</c>: notes for the AI and what the port must keep.</summary>
/// <param name="MustKeep">What the port must keep, in words, for the AI and the reviewer.</param>
/// <param name="Obligations">
/// The same as behaviour-contract item globs (<c>B-BEAN-*-HttpClient.*</c>):
/// a port is accepted only if these items are unchanged.
/// </param>
public sealed record ApiMapping(
    string Id,
    string Name,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> MustKeep,
    IReadOnlyList<string> Obligations)
{
    private readonly Regex[] _obligations = Obligations.Select(Glob).ToArray();

    /// <summary>Whether a port of this mapping must keep this contract item.</summary>
    public bool Obliges(
        string item) =>
        _obligations.Any(pattern => pattern.IsMatch(item));

    private static Regex Glob(
        string glob) =>
        new("^" + string.Join(".*", glob.Split('*').Select(Regex.Escape)) + "$", RegexOptions.CultureInvariant);
}

/// <summary>
/// One rule from <c>behaviour-signatures.yaml</c>: a behaviour difference the
/// platform causes, whatever a port does (for example Spring 6 no longer
/// matching a trailing slash). Such a difference is a decision, never held
/// against a port.
/// </summary>
public sealed record BehaviourSignature(
    string Id,
    ProblemKind Kind,
    Regex Item,
    Regex? Before,
    Regex? After,
    string Reason)
{
    public bool Matches(
        ContractDifference difference) =>
        Item.IsMatch(difference.Item)
        && (Before is null || Before.IsMatch(difference.Before))
        && (After is null || After.IsMatch(difference.After ?? string.Empty));
}

/// <summary>
/// The signatures and API mappings, validated together: every mapping,
/// recipe and library a signature names must exist. A mistake stops the run,
/// naming the file and entry, rather than silently misclassifying.
/// </summary>
public sealed partial class ProblemKnowledge
{
    public const string SignaturesRelativePath = "Config/knowledge/diagnostic-signatures.yaml";
    public const string MappingsRelativePath = "Config/knowledge/api-mappings.yaml";
    public const string BehaviourSignaturesRelativePath = "Config/knowledge/behaviour-signatures.yaml";
    public const string DecisionsRelativePath = "Config/knowledge/decisions.yaml";

    private static readonly Dictionary<string, ProblemKind> Kinds = new(StringComparer.Ordinal)
    {
        ["knock-on"] = ProblemKind.KnockOn,
        ["mechanical"] = ProblemKind.Mechanical,
        ["api-move"] = ProblemKind.ApiMove,
        ["platform-dropped"] = ProblemKind.PlatformDropped,
        ["contract"] = ProblemKind.Contract
    };

    public ProblemKnowledge(
        IReadOnlyList<DiagnosticSignature> signatures,
        IReadOnlyDictionary<string, ApiMapping> mappings,
        IReadOnlyList<BehaviourSignature>? behaviourSignatures = null,
        DecisionPlaybooks? decisions = null)
    {
        Signatures = signatures;
        Mappings = mappings;
        BehaviourSignatures = behaviourSignatures ?? [];
        Decisions = decisions ?? new DecisionPlaybooks([]);
    }

    /// <summary>What a person can choose, and what each choice does.</summary>
    public DecisionPlaybooks Decisions { get; }

    public IReadOnlyList<DiagnosticSignature> Signatures { get; }

    public IReadOnlyDictionary<string, ApiMapping> Mappings { get; }

    public IReadOnlyList<BehaviourSignature> BehaviourSignatures { get; }

    /// <summary>The first behaviour signature that explains a difference, or null.</summary>
    public BehaviourSignature? Explain(
        ContractDifference difference)
    {
        ArgumentNullException.ThrowIfNull(difference);

        return BehaviourSignatures.FirstOrDefault(signature => signature.Matches(difference));
    }

    /// <summary>
    /// The files shipped with the application. Mechanical signatures may name
    /// any recipe defined in <c>Config/recipes/*.yml</c>.
    /// </summary>
    public static ProblemKnowledge LoadDefault(
        IReadOnlyCollection<string> knownLibraries)
    {
        var root = AppContext.BaseDirectory;
        var recipesDirectory = Path.Combine(root, "Config", "recipes");

        var knownRecipes = Directory.Exists(recipesDirectory)
            ? Directory.EnumerateFiles(recipesDirectory, "*.yml")
                .SelectMany(file => RecipeName().Matches(File.ReadAllText(file)).Select(match => match.Groups[1].Value))
                .ToHashSet(StringComparer.Ordinal)
            : [];

        var behaviour = Path.Combine(root, BehaviourSignaturesRelativePath);
        var decisions = Path.Combine(root, DecisionsRelativePath);

        return Parse(
            File.ReadAllText(Path.Combine(root, SignaturesRelativePath)),
            File.ReadAllText(Path.Combine(root, MappingsRelativePath)),
            knownLibraries,
            knownRecipes,
            File.Exists(behaviour) ? File.ReadAllText(behaviour) : null,
            File.Exists(decisions) ? File.ReadAllText(decisions) : null);
    }

    [GeneratedRegex(@"(?m)^name:\s*(\S+)\s*$")]
    private static partial Regex RecipeName();

    /// <param name="knownLibraries">Library names in the unsupported-libraries catalogue.</param>
    /// <param name="knownRecipes">Recipe names a mechanical signature may name (the pipeline recipes).</param>
    /// <param name="behaviourYaml">The behaviour signatures, if any.</param>
    /// <param name="decisionsYaml">The decision playbooks, if any.</param>
    public static ProblemKnowledge Parse(
        string signaturesYaml,
        string mappingsYaml,
        IReadOnlyCollection<string> knownLibraries,
        IReadOnlyCollection<string> knownRecipes,
        string? behaviourYaml = null,
        string? decisionsYaml = null)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        var mappingsFile = Read<MappingsFile>(deserializer, mappingsYaml, "api-mappings.yaml");
        var mappings = new Dictionary<string, ApiMapping>(StringComparer.Ordinal);

        foreach (var (entry, index) in (mappingsFile?.Mappings ?? []).Select((entry, index) => (entry, index + 1)))
        {
            var label = $"api-mappings.yaml: mapping {index} ('{entry.Id ?? "?"}')";

            Require(entry.Id is { Length: > 0 } && entry.Id.StartsWith("M-", StringComparison.Ordinal), label, "needs an id starting with M-");
            Require(!string.IsNullOrWhiteSpace(entry.Name), label, "needs a name");
            Require(entry.Notes is { Count: > 0 }, label, "needs at least one note");
            Require(!mappings.ContainsKey(entry.Id!), label, "duplicates another mapping's id");
            Require((entry.Obligations ?? []).All(glob => glob.StartsWith("B-", StringComparison.Ordinal)), label,
                "obligations must be contract item patterns starting with B-");

            mappings[entry.Id!] = new ApiMapping(entry.Id!, entry.Name!, entry.Notes!, entry.MustKeep ?? [], entry.Obligations ?? []);
        }

        var signaturesFile = Read<SignaturesFile>(deserializer, signaturesYaml, "diagnostic-signatures.yaml");
        var signatures = new List<DiagnosticSignature>();

        foreach (var (entry, index) in (signaturesFile?.Signatures ?? []).Select((entry, index) => (entry, index + 1)))
        {
            var label = $"diagnostic-signatures.yaml: signature {index} ('{entry.Id ?? "?"}')";

            Require(entry.Id is { Length: > 0 } && entry.Id.StartsWith("S-", StringComparison.Ordinal), label, "needs an id starting with S-");
            Require(signatures.All(signature => signature.Id != entry.Id), label, "duplicates another signature's id");
            Require(entry.Kind is not null && Kinds.ContainsKey(entry.Kind), label,
                $"needs a kind: {string.Join(", ", Kinds.Keys)}");
            Require((entry.Contains?.Count ?? 0) + (entry.Pattern?.Count ?? 0) > 0, label, "needs contains or pattern");

            var kind = Kinds[entry.Kind!];

            Require(kind != ProblemKind.ApiMove || (entry.Mapping is not null && mappings.ContainsKey(entry.Mapping)), label,
                "is api-move, so it needs a mapping defined in api-mappings.yaml");
            Require(kind != ProblemKind.Mechanical || (entry.Recipe is not null && knownRecipes.Contains(entry.Recipe)), label,
                "is mechanical, so it needs a recipe the pipeline has");
            Require(kind != ProblemKind.PlatformDropped || (entry.Library is not null && knownLibraries.Contains(entry.Library)), label,
                "is platform-dropped, so it needs a library from unsupported-libraries.yaml");

            var patterns = (entry.Pattern ?? []).Select(pattern => Compile(pattern, label)).ToList();

            signatures.Add(new DiagnosticSignature(
                entry.Id!, kind, entry.Mapping, entry.Recipe, entry.Library, entry.Contains ?? [], patterns));
        }

        var behaviourSignatures = ParseBehaviour(deserializer, behaviourYaml);

        return new ProblemKnowledge(
            signatures,
            mappings,
            behaviourSignatures,
            ParseDecisions(deserializer, decisionsYaml, signatures.Select(signature => signature.Id).ToHashSet(), behaviourSignatures.Select(signature => signature.Id).ToHashSet()));
    }

    private static DecisionPlaybooks ParseDecisions(
        IDeserializer deserializer,
        string? yaml,
        IReadOnlySet<string> signatureIds,
        IReadOnlySet<string> behaviourSignatureIds)
    {
        var subjects = new List<DecisionSubject>();

        if (yaml is null)
        {
            return new DecisionPlaybooks(subjects);
        }

        var file = Read<DecisionsFile>(deserializer, yaml, "decisions.yaml");

        foreach (var (entry, index) in (file?.Subjects ?? []).Select((entry, index) => (entry, index + 1)))
        {
            var label = $"decisions.yaml: subject {index} ('{entry.Id ?? "?"}')";

            Require(!string.IsNullOrWhiteSpace(entry.Id), label, "needs an id");
            Require(subjects.All(subject => subject.Id != entry.Id), label, "duplicates another subject's id");
            Require(!string.IsNullOrWhiteSpace(entry.Title), label, "needs a title");
            Require(entry.Answers is not null && signatureIds.Contains(entry.Answers), label,
                "answers a signature that is not in diagnostic-signatures.yaml");
            Require(entry.Options is { Count: > 0 }, label, "needs at least one option");

            var options = new List<DecisionOption>();

            foreach (var option in entry.Options!)
            {
                var optionLabel = $"{label}, option '{option.Id ?? "?"}'";

                Require(!string.IsNullOrWhiteSpace(option.Id), label, "has an option with no id");
                Require(options.All(existing => existing.Id != option.Id), optionLabel, "duplicates another option's id");
                Require(!string.IsNullOrWhiteSpace(option.Label), optionLabel, "needs a label");
                Require(option.Port is not null || option.Allows is not null || option.Manual, optionLabel, "needs a port, allows or manual");
                Require(option.Allows is null || behaviourSignatureIds.Contains(option.Allows), optionLabel,
                    "allows a behaviour signature that is not in behaviour-signatures.yaml");

                DecisionPort? port = null;

                if (option.Port is { } entryPort)
                {
                    Require(!string.IsNullOrWhiteSpace(entryPort.Name), optionLabel, "port needs a name");
                    Require(entryPort.Scope is not null && DecisionPlaybooks.KnownScopes.Contains(entryPort.Scope), optionLabel,
                        $"has an unknown scope (known: {string.Join(", ", DecisionPlaybooks.KnownScopes)})");
                    Require(entryPort.Notes is { Count: > 0 }, optionLabel, "port needs at least one note");

                    port = new DecisionPort(entryPort.Name!, entryPort.Scope!, entryPort.Notes!, entryPort.MustKeep ?? []);
                }

                options.Add(new DecisionOption(option.Id!, option.Label!, option.Note, port, option.Allows, option.Manual));
            }

            subjects.Add(new DecisionSubject(entry.Id!, entry.Title!, entry.Answers!, options));
        }

        return new DecisionPlaybooks(subjects);
    }

    private static List<BehaviourSignature> ParseBehaviour(
        IDeserializer deserializer,
        string? yaml)
    {
        var signatures = new List<BehaviourSignature>();

        if (yaml is null)
        {
            return signatures;
        }

        var file = Read<BehaviourSignaturesFile>(deserializer, yaml, "behaviour-signatures.yaml");

        foreach (var (entry, index) in (file?.Signatures ?? []).Select((entry, index) => (entry, index + 1)))
        {
            var label = $"behaviour-signatures.yaml: signature {index} ('{entry.Id ?? "?"}')";

            Require(entry.Id is { Length: > 0 } && entry.Id.StartsWith("BS-", StringComparison.Ordinal), label, "needs an id starting with BS-");
            Require(signatures.All(signature => signature.Id != entry.Id), label, "duplicates another signature's id");
            Require(entry.Kind is "contract" or "platform-dropped", label, "needs a kind: contract, platform-dropped");
            Require(!string.IsNullOrWhiteSpace(entry.Item), label, "needs an item pattern");
            Require(!string.IsNullOrWhiteSpace(entry.Reason), label, "needs a reason");

            signatures.Add(new BehaviourSignature(
                entry.Id!,
                Kinds[entry.Kind!],
                Compile(entry.Item!, label),
                entry.Before is null ? null : Compile(entry.Before, label),
                entry.After is null ? null : Compile(entry.After, label),
                entry.Reason!));
        }

        return signatures;
    }

    private static Regex Compile(
        string pattern,
        string label)
    {
        try
        {
            return new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException($"{label} has an invalid pattern '{pattern}': {exception.Message}");
        }
    }

    private static T? Read<T>(
        IDeserializer deserializer,
        string yaml,
        string source)
    {
        try
        {
            return deserializer.Deserialize<T>(yaml);
        }
        catch (YamlException exception)
        {
            throw new InvalidOperationException($"{source}: {exception.Message} (line {exception.Start.Line}).", exception);
        }
    }

    private static void Require(
        bool valid,
        string label,
        string problem)
    {
        if (!valid)
        {
            throw new InvalidOperationException($"{label} {problem}.");
        }
    }

    private sealed class SignaturesFile
    {
        public List<SignatureEntry>? Signatures { get; set; }
    }

    private sealed class SignatureEntry
    {
        public string? Id { get; set; }

        public string? Kind { get; set; }

        public string? Mapping { get; set; }

        public string? Recipe { get; set; }

        public string? Library { get; set; }

        public List<string>? Contains { get; set; }

        public List<string>? Pattern { get; set; }

        /// <summary>The runs whose real failures the signature was written from (documentation).</summary>
        public List<string>? SeenIn { get; set; }
    }

    private sealed class MappingsFile
    {
        public List<MappingEntry>? Mappings { get; set; }
    }

    private sealed class MappingEntry
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public List<string>? Notes { get; set; }

        public List<string>? MustKeep { get; set; }

        public List<string>? Obligations { get; set; }
    }

    private sealed class DecisionsFile
    {
        public List<DecisionSubjectEntry>? Subjects { get; set; }
    }

    private sealed class DecisionSubjectEntry
    {
        public string? Id { get; set; }

        public string? Title { get; set; }

        public string? Answers { get; set; }

        public List<DecisionOptionEntry>? Options { get; set; }
    }

    private sealed class DecisionOptionEntry
    {
        public string? Id { get; set; }

        public string? Label { get; set; }

        public string? Note { get; set; }

        public DecisionPortEntry? Port { get; set; }

        public string? Allows { get; set; }

        public bool Manual { get; set; }
    }

    private sealed class DecisionPortEntry
    {
        public string? Name { get; set; }

        public string? Scope { get; set; }

        public List<string>? Notes { get; set; }

        public List<string>? MustKeep { get; set; }
    }

    private sealed class BehaviourSignaturesFile
    {
        public List<BehaviourSignatureEntry>? Signatures { get; set; }
    }

    private sealed class BehaviourSignatureEntry
    {
        public string? Id { get; set; }

        public string? Kind { get; set; }

        public string? Item { get; set; }

        public string? Before { get; set; }

        public string? After { get; set; }

        public string? Reason { get; set; }

        /// <summary>The runs it was written from (documentation).</summary>
        public List<string>? SeenIn { get; set; }
    }
}
