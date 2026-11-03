using SpringModernisationAgent.Commands;

using var cancellationTokenSource =
    new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationTokenSource.Cancel();
};

var cancellationToken =
    cancellationTokenSource.Token;

var command = args
    .FirstOrDefault()?
    .Trim()
    .ToLowerInvariant();

try
{
    switch (command)
    {
        case null:
        case "":
        case "assess":
            {
                var assessCommand =
                    new AssessCommand();

                await assessCommand.ExecuteAsync(
                    cancellationToken);

                break;
            }

        case "transform":
            {
                var transformCommand =
                    new TransformCommand();

                await transformCommand.ExecuteAsync(
                    cancellationToken);

                break;
            }

        case "report":
            {
                var reportCommand =
                    new ReportCommand();

                await reportCommand.ExecuteAsync(
                    cancellationToken);

                break;
            }

        case "help":
        case "--help":
        case "-h":
            {
                WriteUsage();
                break;
            }

        default:
            {
                Console.Error.WriteLine(
                    $"Unknown command '{command}'.");

                Console.Error.WriteLine();

                WriteUsage();

                Environment.ExitCode = 1;

                break;
            }
    }
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine(
        "Operation cancelled.");

    Environment.ExitCode = 2;
}
catch (Exception exception)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine(
        "The command failed.");

    Console.Error.WriteLine(
        exception.Message);

    Environment.ExitCode = 1;
}

static void WriteUsage()
{
    Console.WriteLine(
        "Spring Modernisation Agent");

    Console.WriteLine();

    Console.WriteLine(
        "Usage:");

    Console.WriteLine(
        "  dotnet run --project " +
        @".\src\SpringModernisationAgent -- assess");

    Console.WriteLine(
        "      Analyse the repository estate, generate approved " +
        "transformation plans, produce the AI migration strategy " +
        "and generate the strategy document.");

    Console.WriteLine();

    Console.WriteLine(
        "  dotnet run --project " +
        @".\src\SpringModernisationAgent -- transform");

    Console.WriteLine(
        "      Explicitly execute one eligible migration wave.");

    Console.WriteLine();

    Console.WriteLine(
        "  dotnet run --project " +
        @".\src\SpringModernisationAgent -- report");

    Console.WriteLine(
        "      Read-only: explain the latest migration results with AI " +
        "and generate the migration results document.");

    Console.WriteLine();

    Console.WriteLine(
        "  dotnet run --project " +
        @".\src\SpringModernisationAgent -- help");

    Console.WriteLine(
        "      Display this help.");
}