using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace SpringModernisationAgent.Maven;

public sealed class MavenPomParser
{
    private static readonly Regex PropertyReferenceRegex =
        new(@"^\$\{(?<name>[^}]+)\}$", RegexOptions.Compiled);

    public MavenProject Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            throw new ArgumentException(
                "POM XML cannot be empty.",
                nameof(xml));
        }

        var document = XDocument.Parse(xml);

        var project = document.Root
            ?? throw new InvalidOperationException(
                "POM does not contain a project element.");

        XNamespace ns = project.Name.Namespace;

        var parent = ParseCoordinate(
            project.Element(ns + "parent"),
            ns);

        var properties = ParseProperties(
            project.Element(ns + "properties"));

        var groupId =
            Value(project.Element(ns + "groupId"))
            ?? parent?.GroupId
            ?? throw new InvalidOperationException(
                "Unable to determine project groupId.");

        var artifactId =
            Value(project.Element(ns + "artifactId"))
            ?? throw new InvalidOperationException(
                "Unable to determine project artifactId.");

        var version =
            Value(project.Element(ns + "version"))
            ?? parent?.Version;

        var coordinate = new MavenCoordinate(
            groupId,
            artifactId,
            version);

        var packaging =
            Value(project.Element(ns + "packaging"))
            ?? "jar";

        var name =
            Value(project.Element(ns + "name"));

        var dependencies = ParseDependencies(
            project.Element(ns + "dependencies"),
            ns,
            properties);

        return new MavenProject(
            Coordinate: coordinate,
            Parent: parent,
            Packaging: packaging,
            Name: name,
            Properties: properties,
            Dependencies: dependencies);
    }

    private static MavenCoordinate? ParseCoordinate(
        XElement? element,
        XNamespace ns)
    {
        if (element is null)
        {
            return null;
        }

        var groupId = Value(
            element.Element(ns + "groupId"));

        var artifactId = Value(
            element.Element(ns + "artifactId"));

        if (groupId is null || artifactId is null)
        {
            return null;
        }

        return new MavenCoordinate(
            groupId,
            artifactId,
            Value(element.Element(ns + "version")));
    }

    private static IReadOnlyDictionary<string, string>
        ParseProperties(XElement? propertiesElement)
    {
        if (propertiesElement is null)
        {
            return new Dictionary<string, string>();
        }

        return propertiesElement
            .Elements()
            .Where(element =>
                !string.IsNullOrWhiteSpace(element.Value))
            .ToDictionary(
                element => element.Name.LocalName,
                element => element.Value.Trim(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<MavenDependency> ParseDependencies(
        XElement? dependenciesElement,
        XNamespace ns,
        IReadOnlyDictionary<string, string> properties)
    {
        if (dependenciesElement is null)
        {
            return [];
        }

        var dependencies = new List<MavenDependency>();

        foreach (var element in
                 dependenciesElement.Elements(ns + "dependency"))
        {
            var groupId =
                Resolve(
                    Value(element.Element(ns + "groupId")),
                    properties);

            var artifactId =
                Resolve(
                    Value(element.Element(ns + "artifactId")),
                    properties);

            if (groupId is null || artifactId is null)
            {
                continue;
            }

            var version =
                Resolve(
                    Value(element.Element(ns + "version")),
                    properties);

            var scope =
                Value(element.Element(ns + "scope"))
                ?? "compile";

            var optional =
                bool.TryParse(
                    Value(element.Element(ns + "optional")),
                    out var optionalValue)
                && optionalValue;

            dependencies.Add(
                new MavenDependency(
                    new MavenCoordinate(
                        groupId,
                        artifactId,
                        version),
                    scope,
                    optional));
        }

        return dependencies;
    }

    private static string? Resolve(
        string? value,
        IReadOnlyDictionary<string, string> properties)
    {
        if (value is null)
        {
            return null;
        }

        var match = PropertyReferenceRegex.Match(value);

        if (!match.Success)
        {
            return value;
        }

        var propertyName =
            match.Groups["name"].Value;

        return properties.TryGetValue(
            propertyName,
            out var resolved)
            ? resolved
            : value;
    }

    private static string? Value(XElement? element)
    {
        var value = element?.Value.Trim();

        return string.IsNullOrWhiteSpace(value)
            ? null
            : value;
    }
}