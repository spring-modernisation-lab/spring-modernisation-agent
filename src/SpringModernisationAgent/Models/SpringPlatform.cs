namespace SpringModernisationAgent.Models;

public sealed record SpringPlatform(
    string? SpringBootVersion,
    string? JavaVersion,
    IReadOnlyList<string> Starters);
