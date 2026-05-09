internal static class CommandLine {

    public static bool NoSplash { get; set; }
    public static bool Runtime { get; set; }

    public static void Init() {
        var args = Environment.GetCommandLineArgs()
            .Skip(1)
            .ToHashSet(StringComparer.InvariantCultureIgnoreCase);

        NoSplash = args.Contains("nosplash");
        Runtime = args.Contains("runtime");
    }
}
