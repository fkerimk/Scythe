internal static class CommandLine {

    public static bool NoSplash { get; set; }
    public static bool Runtime { get; set; }
    public static bool SplashHelper { get; set; }
    public static string SplashSignalPath { get; private set; } = "";
    public static string SplashReadyPath { get; private set; } = "";

    public static void Init() {
        var rawArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var args = rawArgs.ToHashSet(StringComparer.InvariantCultureIgnoreCase);

        NoSplash = args.Contains("nosplash");
        Runtime = args.Contains("runtime");
        SplashHelper = rawArgs.Contains("splashhelper", StringComparer.InvariantCultureIgnoreCase);

        for (var i = 0; i < rawArgs.Length - 1; i++) {
            if (!string.Equals(rawArgs[i], "splashsignal", StringComparison.InvariantCultureIgnoreCase)) continue;
            SplashSignalPath = rawArgs[i + 1] ?? "";
        }

        for (var i = 0; i < rawArgs.Length - 1; i++) {
            if (!string.Equals(rawArgs[i], "splashready", StringComparison.InvariantCultureIgnoreCase)) continue;
            SplashReadyPath = rawArgs[i + 1] ?? "";
        }
    }
}
