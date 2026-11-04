using System.Text;
using System.Text.RegularExpressions;
using SpringModernisationAgent.Build;
using SpringModernisationAgent.Transformation;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SpringModernisationAgent.Behaviour;

/// <summary>What the probe reads from beans and caches (<c>Config/knowledge/behaviour-probes.yaml</c>).</summary>
public sealed record ProbeBean(
    string Id,
    IReadOnlyList<string> StartTypes,
    IReadOnlyList<string> Fields);

public sealed partial record ProbeConfiguration(
    IReadOnlyList<ProbeBean> Beans,
    IReadOnlyList<string> CacheTimeToLiveFields,
    IReadOnlyList<string> CacheMaxEntriesFields)
{
    public const string DefaultRelativePath = "Config/knowledge/behaviour-probes.yaml";

    public static ProbeConfiguration LoadDefault() =>
        Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, DefaultRelativePath)));

    public static ProbeConfiguration Parse(
        string yaml)
    {
        ProbeFile? file;

        try
        {
            file = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build()
                .Deserialize<ProbeFile>(yaml);
        }
        catch (YamlException exception)
        {
            throw new InvalidOperationException($"behaviour-probes.yaml: {exception.Message} (line {exception.Start.Line}).", exception);
        }

        var beans = new List<ProbeBean>();

        foreach (var (entry, index) in (file?.Beans ?? []).Select((entry, index) => (entry, index + 1)))
        {
            var label = $"behaviour-probes.yaml: bean {index} ('{entry.Id ?? "?"}')";

            if (string.IsNullOrWhiteSpace(entry.Id) || !SafeName().IsMatch(entry.Id)
                || entry.StartTypes is not { Count: > 0 } || !entry.StartTypes.All(SafeName().IsMatch)
                || entry.Fields is not { Count: > 0 } || !entry.Fields.All(SafeName().IsMatch))
            {
                throw new InvalidOperationException(
                    $"{label} needs an id, startTypes and fields made of letters, digits, '.', '_' and ':'.");
            }

            if (beans.Any(bean => bean.Id == entry.Id))
            {
                throw new InvalidOperationException($"{label} duplicates another bean's id.");
            }

            beans.Add(new ProbeBean(entry.Id, entry.StartTypes, entry.Fields));
        }

        var ttl = file?.Caches?.TimeToLiveFields ?? [];
        var size = file?.Caches?.MaxEntriesFields ?? [];

        if (!ttl.Concat(size).All(SafeName().IsMatch))
        {
            throw new InvalidOperationException("behaviour-probes.yaml: cache fields must be made of letters, digits, '_' and ':'.");
        }

        return new ProbeConfiguration(beans, ttl, size);
    }

    // Everything here is written into Java source, so only plain names are allowed.
    [GeneratedRegex(@"^[A-Za-z_][\w.:]*$")]
    private static partial Regex SafeName();

    private sealed class ProbeFile
    {
        public List<BeanEntry>? Beans { get; set; }

        public CacheEntry? Caches { get; set; }
    }

    private sealed class BeanEntry
    {
        public string? Id { get; set; }

        public List<string>? StartTypes { get; set; }

        public List<string>? Fields { get; set; }
    }

    private sealed class CacheEntry
    {
        public List<string>? TimeToLiveFields { get; set; }

        public List<string>? MaxEntriesFields { get; set; }
    }
}

/// <summary>What the probe needs to know about a repository, discovered from its files.</summary>
/// <param name="BasePackage">The package of the <c>@SpringBootApplication</c> class; the probe lives there so Spring finds the application.</param>
/// <param name="Profiles"><c>default</c>, then each <c>application-&lt;profile&gt;</c> file's profile.</param>
/// <param name="Settings">Every key in the application's own configuration files.</param>
/// <param name="Roles">Role names used in the security rules.</param>
/// <param name="CanAuthenticate">True when spring-security-test is available to act as a role.</param>
public sealed record ProbeTarget(
    string BasePackage,
    IReadOnlyList<string> Profiles,
    IReadOnlyList<string> Settings,
    IReadOnlyList<string> Roles,
    bool CanAuthenticate);

/// <summary>
/// The behaviour probe (docs/system-design.md §3): a generated test, one per
/// profile, written into the repository just before it runs and always
/// deleted afterwards, so it never reaches a patch. It records what the
/// running application does as <see cref="BehaviourRecord"/> items.
/// </summary>
public sealed partial class BehaviourProbe
{
    public const string ClassPrefix = "MigrationBehaviourProbe";

    /// <summary>Setting keys whose values are never recorded, only whether they are set.</summary>
    private static readonly string[] SecretWords = ["password", "secret", "token", "credential", "key", "private"];

    private readonly IMavenProcessRunner _maven;
    private readonly ProbeConfiguration _configuration;
    private readonly string _template;

    public BehaviourProbe(
        IMavenProcessRunner maven,
        ProbeConfiguration configuration,
        string? template = null)
    {
        ArgumentNullException.ThrowIfNull(maven);
        ArgumentNullException.ThrowIfNull(configuration);

        _maven = maven;
        _configuration = configuration;
        _template = template ?? File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Config", "probe", "BehaviourProbe.java.template"));
    }

    /// <summary>Finds what the probe needs, from the repository's own files. Null if it has no Spring Boot application.</summary>
    public static ProbeTarget? Discover(
        string repositoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        var main = Path.Combine(repositoryPath, "src", "main", "java");
        var resources = Path.Combine(repositoryPath, "src", "main", "resources");

        if (!Directory.Exists(main))
        {
            return null;
        }

        var sources = Directory.EnumerateFiles(main, "*.java", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToList();

        var application = sources.FirstOrDefault(source => source.Contains("@SpringBootApplication", StringComparison.Ordinal));
        var package = application is null ? null : PackageDeclaration().Match(application);

        if (package is not { Success: true })
        {
            return null;
        }

        var configurationFiles = Directory.Exists(resources)
            ? Directory.EnumerateFiles(resources, "application*.*", SearchOption.TopDirectoryOnly)
                .Where(file => Path.GetExtension(file) is ".yml" or ".yaml" or ".properties")
                .Order(StringComparer.Ordinal)
                .ToList()
            : [];

        var profiles = new List<string> { "default" };
        profiles.AddRange(configurationFiles
            .Select(file => ProfileFile().Match(Path.GetFileName(file)))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal));

        var settings = configurationFiles
            .SelectMany(SettingKeys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var roles = sources
            .SelectMany(source => RoleExpression().Matches(source))
            .SelectMany(match => QuotedName().Matches(match.Value).Select(name => name.Groups[1].Value))
            .Select(role => role.StartsWith("ROLE_", StringComparison.Ordinal) ? role["ROLE_".Length..] : role)
            .Where(role => SafeRole().IsMatch(role))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var pom = Path.Combine(repositoryPath, "pom.xml");
        var canAuthenticate = File.Exists(pom) && File.ReadAllText(pom).Contains("spring-security-test", StringComparison.Ordinal);

        return new ProbeTarget(package.Groups[1].Value, profiles, settings, roles, canAuthenticate);
    }

    /// <summary>The probe test for one profile.</summary>
    public (string ClassName, string Source) Generate(
        ProbeTarget target,
        string profile)
    {
        ArgumentNullException.ThrowIfNull(target);

        var className = ClassName(profile);
        var principals = new List<string> { "anonymous" };

        if (target.CanAuthenticate)
        {
            principals.AddRange(target.Roles);
        }

        static string Literal(string text) =>
            "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

        var source = _template
            .Replace("{{PACKAGE}}", target.BasePackage, StringComparison.Ordinal)
            .Replace("{{CLASS}}", className, StringComparison.Ordinal)
            .Replace("{{PROFILE}}", profile, StringComparison.Ordinal)
            .Replace("{{ACTIVE_PROFILES}}", profile == "default" ? string.Empty : $"@ActiveProfiles({Literal(profile)})", StringComparison.Ordinal)
            .Replace("{{SETTINGS}}", string.Join(", ", target.Settings.Select(Literal)), StringComparison.Ordinal)
            .Replace("{{PRINCIPALS}}", string.Join(", ", principals.Select(Literal)), StringComparison.Ordinal)
            .Replace("{{BEANS}}", string.Join(", ", _configuration.Beans.Select(bean =>
                $"{{ {Literal(bean.Id)}, {Literal(string.Join(",", bean.StartTypes))}, {Literal(string.Join(",", bean.Fields))} }}")), StringComparison.Ordinal)
            .Replace("{{CACHE_TTL}}", string.Join(", ", _configuration.CacheTimeToLiveFields.Select(Literal)), StringComparison.Ordinal)
            .Replace("{{CACHE_SIZE}}", string.Join(", ", _configuration.CacheMaxEntriesFields.Select(Literal)), StringComparison.Ordinal)
            .Replace("{{SECURITY_IMPORT}}", target.CanAuthenticate
                ? "import static org.springframework.security.test.web.servlet.request.SecurityMockMvcRequestPostProcessors.user;"
                : string.Empty, StringComparison.Ordinal)
            .Replace("{{AUTHENTICATE}}", target.CanAuthenticate
                ? "request.with(user(\"probe\").roles(principal));"
                : string.Empty, StringComparison.Ordinal);

        return (className, source);
    }

    /// <summary>
    /// Records the repository's behaviour: writes one probe per profile, runs
    /// only those tests, reads what each wrote, and deletes the probes again.
    /// A profile whose context does not start records why.
    /// </summary>
    public async Task<BehaviourRecord> RecordAsync(
        string repositoryPath,
        string repository,
        string commit,
        CancellationToken cancellationToken = default)
    {
        var target = Discover(repositoryPath)
                     ?? throw new InvalidOperationException($"{repository}: no @SpringBootApplication class to probe.");

        var packageDirectory = Path.Combine(repositoryPath, "src", "test", "java", Path.Combine(target.BasePackage.Split('.')));
        var output = Path.Combine(Path.GetTempPath(), $"behaviour-{Guid.NewGuid():N}");
        var written = new List<string>();

        try
        {
            Directory.CreateDirectory(packageDirectory);

            foreach (var profile in target.Profiles)
            {
                var (className, source) = Generate(target, profile);
                var file = Path.Combine(packageDirectory, className + ".java");
                await File.WriteAllTextAsync(file, source, cancellationToken);
                written.Add(file);
            }

            var started = DateTime.UtcNow;
            var classes = string.Join(",", target.Profiles.Select(ClassName));

            var run = await _maven.RunAsync(
                repositoryPath,
                [
                    "--batch-mode",
                    "test",
                    $"-Dtest={classes}",
                    "-Dsurefire.failIfNoSpecifiedTests=false",
                    "-DfailIfNoTests=false",
                    $"-Dmigration.probe.out={output}"
                ],
                cancellationToken);

            var failures = SurefireReportReader.Read(repositoryPath, started);
            var items = new SortedDictionary<string, string>(StringComparer.Ordinal);

            foreach (var profile in target.Profiles)
            {
                var file = Path.Combine(output, profile + ".tsv");

                if (File.Exists(file))
                {
                    foreach (var line in await File.ReadAllLinesAsync(file, cancellationToken))
                    {
                        var tab = line.IndexOf('\t');

                        if (tab > 0)
                        {
                            items[line[..tab]] = line[(tab + 1)..];
                        }
                    }

                    continue;
                }

                // The context did not start (or the probe never ran): say why.
                var failure = failures.FirstOrDefault(test => test.TestClass.EndsWith("." + ClassName(profile), StringComparison.Ordinal));
                var reason = failure?.RootCause ?? failure?.Message
                             ?? (run.ExitCode == 0 ? "the probe did not run" : FirstError(run) ?? $"Maven exit code {run.ExitCode}");

                items[$"B-CTX-{profile}"] = "fails: " + reason;
            }

            foreach (var key in items.Keys.Where(IsSecretSetting).ToList())
            {
                items[key] = items[key] == "(unset)" ? "(unset)" : "(set)";
            }

            return new BehaviourRecord(BehaviourRecord.CurrentSchema, repository, commit, items);
        }
        finally
        {
            foreach (var file in written)
            {
                File.Delete(file);
            }

            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    public static string ClassName(
        string profile) =>
        ClassPrefix + string.Concat(profile
            .Split(['-', '_', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..])) + "Test";

    private static bool IsSecretSetting(
        string item) =>
        item.StartsWith("B-CFG-", StringComparison.Ordinal)
        && SecretWords.Any(word => item[(item.IndexOf('-', "B-CFG-".Length) + 1)..]
            .Split('.', '-', '_')
            .Any(part => part.Equals(word, StringComparison.OrdinalIgnoreCase)));

    private static string? FirstError(
        MavenProcessResult run) =>
        ValidationDiagnosticsExtractor.Extract(run.StandardOutput, run.StandardError).FirstOrDefault();

    /// <summary>Every key in a YAML or properties file, flattened (<c>spring.cache.type</c>).</summary>
    private static IEnumerable<string> SettingKeys(
        string file)
    {
        var text = File.ReadAllText(file);

        if (Path.GetExtension(file) == ".properties")
        {
            return text.ReplaceLineEndings("\n").Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && line[0] is not '#' and not '!' && line.IndexOfAny(['=', ':']) > 0)
                .Select(line => line[..line.IndexOfAny(['=', ':'])].Trim());
        }

        var keys = new List<string>();
        var stream = new YamlStream();

        try
        {
            stream.Load(new StringReader(text));
        }
        catch (Exception exception) when (exception is YamlException or ArgumentException)
        {
            return keys;
        }

        void Walk(YamlNode node, string path)
        {
            switch (node)
            {
                case YamlMappingNode mapping:
                    foreach (var (key, child) in mapping.Children)
                    {
                        var name = (key as YamlScalarNode)?.Value ?? string.Empty;
                        Walk(child, path.Length == 0 ? name : $"{path}.{name}");
                    }

                    break;

                case YamlScalarNode:
                case YamlSequenceNode:
                    if (path.Length > 0)
                    {
                        keys.Add(path);
                    }

                    break;
            }
        }

        foreach (var document in stream.Documents)
        {
            Walk(document.RootNode, string.Empty);
        }

        return keys;
    }

    [GeneratedRegex(@"^\s*package\s+([\w.]+)\s*;", RegexOptions.Multiline)]
    private static partial Regex PackageDeclaration();

    [GeneratedRegex(@"^application-([\w-]+)\.(?:ya?ml|properties)$")]
    private static partial Regex ProfileFile();

    // hasRole("X"), hasAnyRole("X", "Y"), hasAuthority('X'), @Secured({"ROLE_X"}), @RolesAllowed("X")
    [GeneratedRegex(@"(?:hasRole|hasAnyRole|hasAuthority|hasAnyAuthority|@Secured|@RolesAllowed)\s*\(\s*\{?\s*[^)]*\)")]
    private static partial Regex RoleExpression();

    [GeneratedRegex(@"[""']([A-Za-z_][\w]*)[""']")]
    private static partial Regex QuotedName();

    [GeneratedRegex(@"^[A-Za-z_]\w*$")]
    private static partial Regex SafeRole();
}
