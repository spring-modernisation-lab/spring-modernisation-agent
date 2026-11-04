using System.Text;
using System.Text.Json;
using SpringModernisationAgent.Behaviour;
using SpringModernisationAgent.Build;
using SpringModernisationAgent.Reporting;

namespace SpringModernisationAgent.Commands;

/// <summary>
/// The behaviour contract from the command line (docs/system-design.md §3):
/// <list type="bullet">
/// <item><c>behaviour record &lt;repository-path&gt; &lt;name&gt; &lt;commit&gt; &lt;out.json&gt;</c>: run the probe and save the record;</item>
/// <item><c>behaviour compare &lt;baseline.json&gt; &lt;after.json&gt;</c>: print the differences as Markdown (also to the job summary); exit 1 if the contract does not hold.</item>
/// </list>
/// </summary>
public sealed class BehaviourCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<int> ExecuteAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        switch (arguments.FirstOrDefault())
        {
            case "record" when arguments.Count == 5:
            {
                var probe = new BehaviourProbe(new MavenProcessRunner(), ProbeConfiguration.LoadDefault());
                var record = await probe.RecordAsync(arguments[1], arguments[2], arguments[3], cancellationToken);

                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(arguments[4]))!);
                await File.WriteAllTextAsync(arguments[4], JsonSerializer.Serialize(record, JsonOptions), cancellationToken);

                Console.WriteLine($"Behaviour record for {arguments[2]}: {record.Items.Count} items -> {arguments[4]}");

                foreach (var (item, value) in record.Items.Where(item => item.Key.StartsWith("B-CTX-", StringComparison.Ordinal)))
                {
                    Console.WriteLine($"  {item}: {value}");
                }

                return 0;
            }

            case "compare" when arguments.Count == 3:
            {
                var baseline = await ReadAsync(arguments[1], cancellationToken);
                var after = await ReadAsync(arguments[2], cancellationToken);
                var comparison = BehaviourContract.Compare(baseline, after);
                var markdown = Render(baseline, comparison);

                Console.WriteLine(markdown);

                var summary = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");

                if (!string.IsNullOrWhiteSpace(summary))
                {
                    await File.AppendAllTextAsync(summary, markdown, cancellationToken);
                }

                return comparison.Matches ? 0 : 1;
            }

            default:
                Console.Error.WriteLine(
                    "Usage: behaviour record <repository-path> <name> <commit> <out.json> | behaviour compare <baseline.json> <after.json>");
                return 2;
        }
    }

    public static string Render(
        BehaviourRecord baseline,
        ContractComparison comparison)
    {
        var markdown = new StringBuilder();

        markdown.AppendLine($"## Behaviour contract: {baseline.Repository}");
        markdown.AppendLine();
        markdown.AppendLine(
            comparison.Matches
                ? $"**Holds.** {comparison.Same} recorded behaviours are unchanged."
                : $"**Does not hold.** {comparison.Differences.Count} differ from the baseline; {comparison.Same} are unchanged." +
                  BehaviourText.UncheckedSentence(comparison));

        if (comparison.Differences.Count > 0)
        {
            markdown.AppendLine();
            markdown.AppendLine("| Behaviour | Baseline | After | Status |");
            markdown.AppendLine("|---|---|---|---|");

            foreach (var difference in comparison.Differences)
            {
                markdown.AppendLine(
                    $"| `{Cell(difference.Item)}` | {Cell(difference.Before)} | {Cell(difference.After ?? "(not recorded)")} | " +
                    $"{Cell(BehaviourText.Status(difference))} |");
            }
        }

        if (comparison.New.Count > 0)
        {
            markdown.AppendLine();
            markdown.AppendLine($"{comparison.New.Count} behaviours were recorded only after the change (reported, not compared).");
        }

        return markdown.ToString();
    }

    private static async Task<BehaviourRecord> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);

        return await JsonSerializer.DeserializeAsync<BehaviourRecord>(stream, cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException($"{path} is not a behaviour record.");
    }

    private static string Cell(
        string text) =>
        text.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");
}
