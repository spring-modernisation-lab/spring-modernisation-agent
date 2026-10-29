using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SpringModernisationAgent.Config;

public sealed class SpringMigrationPolicyLoader
{
    private readonly IDeserializer _deserializer;

    public SpringMigrationPolicyLoader()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(
                CamelCaseNamingConvention.Instance)
            .Build();
    }

    public SpringMigrationPolicy Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Spring migration policy was not found.",
                path);
        }

        var yaml = File.ReadAllText(path);

        var policy =
            _deserializer.Deserialize<SpringMigrationPolicy>(yaml);

        return policy
            ?? throw new InvalidOperationException(
                "Spring migration policy could not be loaded.");
    }
}
