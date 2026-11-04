using System.Security.Cryptography;

namespace SpringModernisationAgent.Recipes;

/// <summary>
/// Downloads a pinned module's JAR from Maven Central once, checks it against
/// the SHA-1 Maven Central publishes for it, and keeps it in a cache folder.
/// A JAR that fails the check is never kept or used.
/// </summary>
public sealed class MavenCentralJarCache
{
    public const string DefaultRepository = "https://repo1.maven.org/maven2";

    private readonly string _cacheDirectory;
    private readonly HttpClient _http;
    private readonly string _repository;

    public MavenCentralJarCache(
        string cacheDirectory,
        HttpClient http,
        string repository = DefaultRepository)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);

        _cacheDirectory = cacheDirectory;
        _http = http;
        _repository = repository.TrimEnd('/');
    }

    public async Task<string> GetAsync(
        RecipeModule module,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(module);

        var fileName = $"{module.ArtifactId}-{module.Version}.jar";
        var path = Path.Combine(_cacheDirectory, module.GroupId, fileName);

        if (File.Exists(path))
        {
            return path;
        }

        var url = $"{_repository}/{module.GroupId.Replace('.', '/')}/{module.ArtifactId}/{module.Version}/{fileName}";

        var expected = (await _http.GetStringAsync(url + ".sha1", cancellationToken))
            .Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?
            .ToLowerInvariant();

        if (expected is null || expected.Length != 40)
        {
            throw new InvalidOperationException($"Maven Central published no SHA-1 for {module.Coordinates}.");
        }

        var bytes = await _http.GetByteArrayAsync(url, cancellationToken);
        var actual = Convert.ToHexStringLower(SHA1.HashData(bytes));

        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"{module.Coordinates}: downloaded JAR has SHA-1 {actual}, but Maven Central publishes {expected}; it was not used.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Written next to its final name, then moved, so a partial file is never used.
        var partial = path + $".{Guid.NewGuid():N}.partial";
        await File.WriteAllBytesAsync(partial, bytes, cancellationToken);
        File.Move(partial, path, overwrite: true);

        return path;
    }
}
