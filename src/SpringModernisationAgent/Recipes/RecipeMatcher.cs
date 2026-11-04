using System.Text.RegularExpressions;

namespace SpringModernisationAgent.Recipes;

/// <summary>One compile error (or failing test) and the names it involves.</summary>
/// <param name="File">Repository-relative source file, or the test class for a failing test.</param>
/// <param name="Lines">The diagnostic lines, as extracted.</param>
/// <param name="Terms">
/// Fully qualified types, simple type names, members and packages from the
/// error, excluding the repository's own code and the JDK.
/// </param>
public sealed record CompileProblem(
    string File,
    IReadOnlyList<string> Lines,
    IReadOnlyList<string> Terms);

public enum RecipeCandidateStatus
{
    /// <summary>The route does not run it: a real suggestion.</summary>
    NotRun,

    /// <summary>The route already runs it and the problem remains: reported, never suggested.</summary>
    AlreadyRun
}

/// <param name="Score">Strongest match: 3 exact type, 2 simple name or member, 1 package.</param>
/// <param name="MatchedOn">The error terms the recipe matched, for the reviewer.</param>
/// <param name="Files">The files whose errors it matched.</param>
public sealed record RecipeCandidate(
    RecipeEntry Recipe,
    RecipeCandidateStatus Status,
    int Score,
    IReadOnlyList<string> MatchedOn,
    IReadOnlyList<string> Files);

/// <summary>
/// Matches the errors the route's recipes leave behind against the recipe
/// index, deterministically (docs/system-design.md §5.1, step 2). It only
/// suggests recipes that exist, and says which ones already ran.
/// </summary>
public static partial class RecipeMatcher
{
    /// <summary>Names too generic to point at a migration.</summary>
    private static readonly HashSet<string> GenericNames = new(StringComparer.Ordinal)
    {
        "Object", "String", "Class", "List", "Map", "Set", "Collection", "Optional", "Override",
        "Integer", "Long", "Boolean", "Exception", "Throwable", "Void", "super", "this"
    };

    public static IReadOnlyList<CompileProblem> Problems(
        IReadOnlyList<string> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var problems = new List<(string File, List<string> Lines)>();

        foreach (var line in diagnostics)
        {
            var error = CompileErrorLine().Match(line);
            var test = FailingTestLine().Match(line);

            if (error.Success)
            {
                problems.Add((error.Groups["file"].Value, [line]));
            }
            else if (test.Success)
            {
                problems.Add((test.Groups["class"].Value, [line]));
            }
            else if (problems.Count > 0 && line.Length > 0 && char.IsWhiteSpace(line[0]))
            {
                problems[^1].Lines.Add(line);
            }
        }

        return problems
            .Select(problem => new CompileProblem(problem.File, problem.Lines, Terms(problem.File, problem.Lines)))
            .Where(problem => problem.Terms.Count > 0)
            .ToList();
    }

    /// <summary>
    /// Candidates for the problems, best first: not-run before already-run,
    /// then by score, then the more specific recipe (fewer mentions).
    /// </summary>
    public static IReadOnlyList<RecipeCandidate> Match(
        RecipeCatalog catalog,
        IReadOnlySet<string> alreadyRun,
        IReadOnlyList<CompileProblem> problems,
        int maxCandidates = 10)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(alreadyRun);
        ArgumentNullException.ThrowIfNull(problems);

        var found = new Dictionary<string, (RecipeEntry Recipe, int Score, SortedSet<string> On, SortedSet<string> Files)>(StringComparer.Ordinal);
        var reached = HighestVersionFamilies(alreadyRun);

        foreach (var recipe in catalog.Recipes)
        {
            // Umbrella upgrades and search recipes are never a candidate, run or not:
            // they don't point at a specific fix.
            if (!IsSuggestible(recipe.Name, reached))
            {
                continue;
            }

            var mentions = recipe.Mentions
                .Append(recipe.Name[(recipe.Name.LastIndexOf('.') + 1)..])
                .ToList();

            foreach (var problem in problems)
            {
                foreach (var term in problem.Terms)
                {
                    var score = mentions.Max(mention => Strength(mention, term));

                    if (score == 0)
                    {
                        continue;
                    }

                    if (!found.TryGetValue(recipe.Name, out var entry))
                    {
                        entry = (recipe, 0, new SortedSet<string>(StringComparer.Ordinal), new SortedSet<string>(StringComparer.Ordinal));
                    }

                    entry.On.Add(term);
                    entry.Files.Add(problem.File);
                    found[recipe.Name] = entry with { Score = Math.Max(entry.Score, score) };
                }
            }
        }

        return found.Values
            .Select(entry => new RecipeCandidate(
                entry.Recipe,
                alreadyRun.Contains(entry.Recipe.Name) ? RecipeCandidateStatus.AlreadyRun : RecipeCandidateStatus.NotRun,
                entry.Score,
                entry.On.ToList(),
                entry.Files.ToList()))
            .OrderBy(candidate => candidate.Status)
            .ThenByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Recipe.Mentions.Count)
            .ThenBy(candidate => candidate.Recipe.Name, StringComparer.Ordinal)
            .Take(maxCandidates)
            .ToList();
    }

    /// <summary>
    /// A recipe the route doesn't run may be suggested only if it can fix
    /// something without choosing a platform version:
    /// <list type="bullet">
    /// <item>not a search recipe (it only reports);</item>
    /// <item>not a version upgrade (<c>UpgradeSpringFramework_7_0</c>): versions are the route's decision;</item>
    /// <item>not in a version family beyond what the route reaches (<c>boot4</c> when the route reaches <c>boot3</c>).</item>
    /// </list>
    /// </summary>
    private static bool IsSuggestible(
        string name,
        IReadOnlyDictionary<string, int> reached)
    {
        var segments = name.Split('.');
        var simple = segments[^1];

        if (segments.Contains("search") || simple.StartsWith("Find", StringComparison.Ordinal))
        {
            return false;
        }

        if (VersionUpgrade().IsMatch(simple))
        {
            return false;
        }

        foreach (var segment in segments[..^1])
        {
            var family = VersionFamily().Match(segment);

            if (family.Success
                && reached.TryGetValue(family.Groups["family"].Value, out var highest)
                && int.Parse(family.Groups["major"].Value) > highest)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>For each version family in the route's recipes (<c>boot</c>, <c>security</c>, <c>framework</c>), the highest major it reaches.</summary>
    private static Dictionary<string, int> HighestVersionFamilies(
        IReadOnlySet<string> alreadyRun)
    {
        var highest = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var segment in alreadyRun.SelectMany(name => name.Split('.')[..^1]))
        {
            var family = VersionFamily().Match(segment);

            if (family.Success)
            {
                var major = int.Parse(family.Groups["major"].Value);
                highest[family.Groups["family"].Value] = Math.Max(highest.GetValueOrDefault(family.Groups["family"].Value), major);
            }
        }

        return highest;
    }

    /// <summary>3: the same fully qualified type; 2: the same simple name or member; 1: the term is in the mentioned package.</summary>
    private static int Strength(
        string mention,
        string term)
    {
        if (string.Equals(mention, term, StringComparison.Ordinal))
        {
            return term.Contains('.') ? 3 : 2;
        }

        var mentionSimple = mention[(mention.LastIndexOf('.') + 1)..];
        var termSimple = term[(term.LastIndexOf('.') + 1)..];

        if (mentionSimple.Length > 0 && !GenericNames.Contains(mentionSimple)
            && string.Equals(mentionSimple, termSimple, StringComparison.Ordinal)
            && (!mention.Contains('.') || !term.Contains('.')))
        {
            return 2;
        }

        // A specific package mention (lower-case last part, at least three
        // parts such as javax.servlet.http) covering a type in it.
        var isPackage = mention.Count(character => character == '.') >= 2
                        && mentionSimple.Length > 0
                        && char.IsLower(mentionSimple[0]);

        return isPackage && term.StartsWith(mention + ".", StringComparison.Ordinal) ? 1 : 0;
    }

    private static IReadOnlyList<string> Terms(
        string file,
        IReadOnlyList<string> lines)
    {
        var ownRoot = OwnPackageRoot(file);
        var terms = new SortedSet<string>(StringComparer.Ordinal);

        void Add(string term)
        {
            term = term.Trim();

            if (term.Length == 0 || IsExcluded(term, ownRoot))
            {
                return;
            }

            terms.Add(term);

            // A type also matches by its simple name (`UserType` in a description).
            if (term.Contains('.'))
            {
                var simple = term[(term.LastIndexOf('.') + 1)..];

                if (simple.Length > 0 && char.IsUpper(simple[0]) && !GenericNames.Contains(simple))
                {
                    terms.Add(simple);
                }
            }
        }

        string? missingType = null;

        foreach (var line in lines)
        {
            var symbol = SymbolLine().Match(line);

            if (symbol.Success)
            {
                var name = symbol.Groups["name"].Value;

                if (!GenericNames.Contains(name))
                {
                    Add(name);
                }

                missingType = symbol.Groups["kind"].Value is "class" or "interface" or "enum" ? name : null;
                continue;
            }

            var location = LocationLine().Match(line);

            if (location.Success)
            {
                var name = location.Groups["name"].Value;

                // "symbol: class X / location: package p" names the type p.X;
                // the bare package is too broad to search on.
                if (location.Groups["kind"].Value == "package")
                {
                    if (missingType is not null)
                    {
                        Add($"{name}.{missingType}");
                    }
                }
                else
                {
                    Add(name);
                }

                continue;
            }

            foreach (Match type in QualifiedType().Matches(line))
            {
                Add(type.Value);
            }
        }

        return terms.ToList();
    }

    private static bool IsExcluded(
        string term,
        string? ownRoot) =>
        GenericNames.Contains(term)
        || term.StartsWith("java.lang.", StringComparison.Ordinal)
        || term.StartsWith("java.util.", StringComparison.Ordinal)
        || term.StartsWith("java.sql.", StringComparison.Ordinal)
        || (ownRoot is not null && term.StartsWith(ownRoot, StringComparison.Ordinal));

    /// <summary>The first two package parts of the file itself (<c>com.springmodernisationlab.</c>), so the repository's own types are left out.</summary>
    private static string? OwnPackageRoot(
        string file)
    {
        var marker = file.IndexOf("/java/", StringComparison.Ordinal);

        if (marker < 0)
        {
            var parts = file.Split('.');
            return parts.Length > 2 ? $"{parts[0]}.{parts[1]}." : null;
        }

        var segments = file[(marker + "/java/".Length)..].Split('/');
        return segments.Length > 2 ? $"{segments[0]}.{segments[1]}." : null;
    }

    // [ERROR] src/main/java/a/B.java:[12,72] cannot find symbol
    [GeneratedRegex(@"^\[ERROR\]\s+(?<file>[^\s:]+\.(?:java|kt)):\[\d+,\d+\]")]
    private static partial Regex CompileErrorLine();

    // FAILED TEST a.b.CTests.name (failure): ...
    [GeneratedRegex(@"^FAILED TEST (?<class>[\w.$]+)\.\w+ ")]
    private static partial Regex FailingTestLine();

    //   symbol:   class WebSecurityConfigurerAdapter / method setMaxConnTotal(int) / variable super
    [GeneratedRegex(@"^\s*(?:\[ERROR\]\s*)?symbol:\s+(?<kind>class|interface|enum|method|variable|constructor)\s+(?<name>[\w$.]+)")]
    private static partial Regex SymbolLine();

    //   location: class org.apache.hc...HttpClientBuilder / package org.springframework...
    [GeneratedRegex(@"^\s*(?:\[ERROR\]\s*)?location:\s+(?<kind>class|interface|package|variable \w+ of type)\s+(?<name>[\w$.]+)")]
    private static partial Regex LocationLine();

    // UpgradeSpringFramework_7_0, MigrateToHibernate61, HibernateValidator_8_0, UpgradeToJava21
    [GeneratedRegex(@"^(?:Upgrade|MigrateTo)\w*?\d+$|_\d+(?:_\d+)*$")]
    private static partial Regex VersionUpgrade();

    // boot3, security6, framework7, hibernate6 (a package part naming a major version)
    [GeneratedRegex(@"^(?<family>[a-z]+)(?<major>\d+)$")]
    private static partial Regex VersionFamily();

    // org.hibernate.usertype.UserType (a package path ending in a capitalised type)
    [GeneratedRegex(@"\b(?:[a-z][\w$]*\.)+[A-Z][\w$]*")]
    private static partial Regex QualifiedType();
}
