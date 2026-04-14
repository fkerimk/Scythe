internal static class CommandLine {

    public static bool NoSplash;
    public static bool Runtime;

    public static void Init() {

        foreach (var arg in Environment.GetCommandLineArgs()) {

            if (arg.Equals("nosplash", StringComparison.InvariantCultureIgnoreCase)) NoSplash = true;
            if (arg.Equals("runtime", StringComparison.InvariantCultureIgnoreCase)) Runtime = true;
        }
    }
}