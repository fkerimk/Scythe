using CliWrap;
using CliWrap.Buffered;

internal readonly record struct CommandRunResult(int ExitCode, string StandardOutput, string StandardError) {

    public string GetPreferredError(string fallback) =>
        !string.IsNullOrWhiteSpace(StandardError) ? StandardError :
        !string.IsNullOrWhiteSpace(StandardOutput) ? StandardOutput :
        fallback;
}

internal static class CommandRunner {

    public static CommandRunResult Run(string fileName, IEnumerable<string> arguments) {

        var result = Cli.Wrap(fileName)
            .WithArguments(arguments)
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync()
            .GetAwaiter()
            .GetResult();

        return new CommandRunResult(result.ExitCode, result.StandardOutput, result.StandardError);
    }
}
