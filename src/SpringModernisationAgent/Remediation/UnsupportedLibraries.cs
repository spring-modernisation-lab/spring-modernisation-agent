namespace SpringModernisationAgent.Remediation;

/// <summary>A library the target platform no longer supports, and what a person does about it.</summary>
/// <param name="Name">For people, for example "Ehcache 2".</param>
/// <param name="Package">Java package prefix of its API, for example <c>net.sf.ehcache</c>.</param>
/// <param name="PomMarkers">Text in a <c>pom.xml</c> that shows the dependency is declared.</param>
/// <param name="Replacements">The supported replacements in one line, for the agent and rejection messages.</param>
/// <param name="Recommended">The replacement the manual steps follow.</param>
/// <param name="ManualSteps">What a person changes, in order. Fixed text, not AI-written.</param>
/// <param name="Alternatives">Other replacements and what choosing them would change.</param>
public sealed record UnsupportedLibrary(
    string Name,
    string Package,
    IReadOnlyList<string> PomMarkers,
    string Replacements,
    string Recommended,
    IReadOnlyList<string> ManualSteps,
    IReadOnlyList<string> Alternatives);

/// <summary>
/// Libraries Spring Boot 3 dropped. Replacing one is a design decision that
/// also touches <c>pom.xml</c> and often test resources, so the Remediation
/// Agent may not write code against them (the applier rejects it), is told
/// which ones a repository uses, and the pull request lists the manual steps.
/// </summary>
public static class UnsupportedLibraries
{
    public static IReadOnlyList<UnsupportedLibrary> All { get; } =
    [
        new(
            Name: "Ehcache 2",
            Package: "net.sf.ehcache",
            PomMarkers: ["<groupId>net.sf.ehcache</groupId>"],
            Replacements: "Ehcache 3 through JCache (org.ehcache, javax.cache), Caffeine, or Redis",
            Recommended: "Ehcache 3 through JCache: keeps XML-configured caches and their limits",
            ManualSteps:
            [
                "`pom.xml`: replace `net.sf.ehcache:ehcache` with `org.ehcache:ehcache` (classifier `jakarta`) " +
                "and add `javax.cache:cache-api`; Spring Boot manages both versions.",
                "Convert every Ehcache 2 XML file, in main **and test** resources, to the Ehcache 3 format. " +
                "Carry over each cache's name, time-to-live or time-to-idle, and size limit.",
                "Settings: change `spring.cache.type: ehcache` to `jcache`, and replace `spring.cache.ehcache.config` " +
                "with `spring.cache.jcache.config` pointing at the converted file, in every profile and test configuration.",
                "Keep the Spring caching annotations (`@Cacheable` and others) as they are; remove any code that " +
                "uses the Ehcache 2 API directly.",
                "Run the caching tests and check expiry and size limits behave as before."
            ],
            Alternatives:
            [
                "Caffeine: in-process like Ehcache, configured with a spec string per cache instead of XML " +
                "(`spring.cache.type: caffeine`); no per-cache size limits without a custom bean.",
                "Redis: one cache shared by every instance; needs a Redis server, network calls and serialisable values."
            ]),
        new(
            Name: "Keycloak Spring adapter",
            Package: "org.keycloak.adapters",
            PomMarkers: ["keycloak-spring-boot", "keycloak-spring-security-adapter"],
            Replacements: "Spring Security's OAuth2 resource server or OAuth2 login",
            Recommended: "Spring Security OAuth2 resource server (APIs) or OAuth2 login (browser applications)",
            ManualSteps:
            [
                "`pom.xml`: remove the Keycloak adapter dependencies and BOM; add `spring-boot-starter-oauth2-resource-server` " +
                "(or `spring-boot-starter-oauth2-client` for login).",
                "Settings: replace the `keycloak.*` properties with `spring.security.oauth2.resourceserver.jwt.issuer-uri` " +
                "set to the realm URL (or a client registration for login).",
                "Replace the Keycloak security configuration class with a `SecurityFilterChain` bean, and map realm and " +
                "client roles to authorities with a `JwtAuthenticationConverter`, keeping every role check.",
                "Replace `KeycloakPrincipal` and `KeycloakSecurityContext` with `Jwt` or `OAuth2User`.",
                "Update tests to use spring-security-test's `jwt()` or `oidcLogin()`."
            ],
            Alternatives:
            [
                "None in practice: Keycloak itself has deprecated the adapters."
            ]),
        new(
            Name: "Springfox",
            Package: "springfox.documentation",
            PomMarkers: ["<groupId>io.springfox</groupId>"],
            Replacements: "springdoc-openapi",
            Recommended: "springdoc-openapi 2.x",
            ManualSteps:
            [
                "`pom.xml`: remove the `io.springfox` dependencies and add `org.springdoc:springdoc-openapi-starter-webmvc-ui` (2.x).",
                "Remove `@EnableSwagger2` and `Docket` beans; express grouping and path filters with `GroupedOpenApi` " +
                "beans and `springdoc.*` settings.",
                "Replace Swagger 2 annotations (`@Api`, `@ApiOperation`, `@ApiParam`, `@ApiModel`) with OpenAPI 3 ones " +
                "(`@Tag`, `@Operation`, `@Parameter`, `@Schema`).",
                "The documentation URLs change (`/swagger-ui.html` to `/swagger-ui/index.html`, `/v2/api-docs` to " +
                "`/v3/api-docs`): update security rules, gateways and links."
            ],
            Alternatives: [])
    ];

    public static UnsupportedLibrary? Find(
        string name) =>
        All.FirstOrDefault(library => library.Name == name);

    /// <summary>The unsupported libraries a repository declares or imports, in list order.</summary>
    public static IReadOnlyList<UnsupportedLibrary> FindIn(
        string repositoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        if (!Directory.Exists(repositoryPath))
        {
            return [];
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = true
        };

        var found = new HashSet<UnsupportedLibrary>();

        foreach (var file in Directory.EnumerateFiles(repositoryPath, "*", options))
        {
            var relative = Path.GetRelativePath(repositoryPath, file).Replace('\\', '/');

            if (relative.Split('/').Any(segment => segment is ".git" or "target" or "node_modules"))
            {
                continue;
            }

            var isPom = Path.GetFileName(file) == "pom.xml";
            var isSource = relative.Contains("src/main/", StringComparison.Ordinal)
                           && Path.GetExtension(file) is ".java" or ".kt";

            if (!isPom && !isSource)
            {
                continue;
            }

            var text = File.ReadAllText(file);

            foreach (var library in All)
            {
                if (isPom
                        ? library.PomMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal))
                        : text.Contains($"import {library.Package}.", StringComparison.Ordinal))
                {
                    found.Add(library);
                }
            }
        }

        return All.Where(found.Contains).ToList();
    }
}
