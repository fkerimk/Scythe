using System.Runtime.InteropServices;

internal static class NativeResolver {

#if SCYTHE_RUNTIME_BUILD
    public static void Init() { }
#else
    public static void Init() { NativeLibrary.SetDllImportResolver(typeof(Assimp.AssimpContext).Assembly, Resolver); }

    private static IntPtr Resolver(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath) {

        if (libraryName == "libdl.so" && RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {

            try {

                return NativeLibrary.Load("libdl.so.2");

            } catch {

                // ignored
            }
        }

        if (libraryName != "assimp") return IntPtr.Zero;

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;

        string rid;
        string ext;

        var prefix = "";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {

            rid = "win-" + RuntimeInformation.ProcessArchitecture.ToString().ToLower();
            ext = ".dll";

        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {

            rid = "linux-" + RuntimeInformation.ProcessArchitecture.ToString().ToLower();
            ext = ".so";
            prefix = "lib";

        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {

            rid = "osx-" + RuntimeInformation.ProcessArchitecture.ToString().ToLower();
            ext = ".dylib";
            prefix = "lib";

        } else
            return IntPtr.Zero;

        var fileName = libraryName.StartsWith(prefix) ? $"{libraryName}{ext}" : $"{prefix}{libraryName}{ext}";

        try {
            return NativeLibrary.Load(fileName, assembly, searchPath);
        } catch {
            // Fall back to explicit probing below.
        }

        string[] searchPaths = [Path.Combine(baseDir, "runtimes", rid, "native", fileName), RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? Path.Combine(baseDir, "runtimes", "osx", "native", fileName) : "", Path.Combine(baseDir, fileName)];

        foreach (var path in searchPaths) {

            if (string.IsNullOrEmpty(path)) continue;

            if (File.Exists(path)) return NativeLibrary.Load(path);
        }

        return IntPtr.Zero;
    }
#endif
}
