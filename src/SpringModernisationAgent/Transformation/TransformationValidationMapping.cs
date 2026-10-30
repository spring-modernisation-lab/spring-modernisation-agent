namespace SpringModernisationAgent.Transformation;

public static class TransformationValidationMapping
{
    public static IReadOnlyList<string> GetMavenArguments(string check)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(check);

        return check switch
        {
            "compile" =>
            [
                "--batch-mode",
                "compile"
            ],

            "unit-tests" =>
            [
                "--batch-mode",
                "test"
            ],

            "application-context-test" =>
            [
                "--batch-mode",
                "test"
            ],

            "effective-pom" =>
            [
                "--batch-mode",
                "help:effective-pom"
            ],

            "dependency-resolution" =>
            [
                "--batch-mode",
                "dependency:resolve"
            ],

            _ => throw new InvalidOperationException(
                $"Unsupported transformation validation check '{check}'.")
        };
    }
}