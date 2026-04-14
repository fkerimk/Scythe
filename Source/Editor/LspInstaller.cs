using System.IO.Compression;
using System.Runtime.InteropServices;

internal static class LspInstaller {

    public static string Status = "Initializing...";
    public static float Progress;
    public static bool IsDone;

    private static readonly HttpClient Client = new();

    public static async void Start() {

        try {

            if (CheckLspFiles()) {

                Status = "Ready";
                Progress = 1.0f;
                IsDone = true;

                return;
            }

            try {
                await InstallAsync();
            } catch (Exception e) {

                Status = $"Error: {e.Message}";
                Console.WriteLine(e);
                await Task.Delay(3000);
                IsDone = true;
            }
        } catch {
            /**/
        }
    }

    public static bool CheckLspFiles() {

        var bin = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "lua-language-server.exe" : "lua-language-server";
        var lspRoot = Path.Combine(AppContext.BaseDirectory, "External", GetPlatformName(), "LuaLSP");

        if (!Directory.Exists(lspRoot)) return false;

        var hasBin = File.Exists(Path.Combine(lspRoot, "bin", bin));
        var hasMain = File.Exists(Path.Combine(lspRoot, "main.lua"));

        return hasBin && hasMain;
    }

    private static async Task InstallAsync() {

        var platform = GetPlatformName();
        var externalDir = Path.Combine(AppContext.BaseDirectory, "External");
        var platformDir = Path.Combine(externalDir, platform);
        var lspDir = Path.Combine(platformDir, "LuaLSP");

        if (!Directory.Exists(lspDir)) Directory.CreateDirectory(lspDir);

        var url = GetDownloadUrl();

        if (string.IsNullOrEmpty(url)) throw new Exception("Unsupported Platform");

        Status = "Downloading Lua Language Server...";

        if (!Directory.Exists(lspDir)) Directory.CreateDirectory(lspDir);

        var archiveName = "lsp_archive" + (url.EndsWith(".zip") ? ".zip" : ".tar.gz");
        var archivePath = Path.Combine(lspDir, archiveName);

        using (var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead)) {

            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? 1024 * 1024 * 50; // Estimate 50MB if unknown

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long totalRead = 0;
            int read;

            while ((read = await contentStream.ReadAsync(buffer)) > 0) {

                await fileStream.WriteAsync(buffer.AsMemory(0, read));
                totalRead += read;
                Progress = (float)totalRead / totalBytes;
                Status = $"Downloading... {totalRead / 1024 / 1024}MB";
            }
        }

        // Extract
        Status = "Extracting...";
        Progress = 1.0f;

        if (archivePath.EndsWith(".zip"))
            await ZipFile.ExtractToDirectoryAsync(archivePath, lspDir, true);
        else {

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                ExtractTarGz(archivePath);
            else
                await System.Diagnostics.Process.Start("tar", $"-xzf \"{archivePath}\" -C \"{lspDir}\"").WaitForExitAsync();
        }

        File.Delete(archivePath);

        Status = "Ready";
        IsDone = true;
    }

    private static void ExtractTarGz(string archivePath) {

        using var fs = File.OpenRead(archivePath);
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
    }

    private static string GetDownloadUrl() {

        const string version = "3.17.1";
        const string baseUrl = $"https://github.com/LuaLS/lua-language-server/releases/download/{version}/lua-language-server-{version}";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {

            if (RuntimeInformation.ProcessArchitecture == Architecture.X64) return $"{baseUrl}-win32-x64.zip";
            if (RuntimeInformation.ProcessArchitecture == Architecture.X86) return $"{baseUrl}-win32-ia32.zip";
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {

            if (RuntimeInformation.ProcessArchitecture == Architecture.X64) return $"{baseUrl}-linux-x64.tar.gz";
            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64) return $"{baseUrl}-linux-arm64.tar.gz";
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {

            if (RuntimeInformation.ProcessArchitecture == Architecture.X64) return $"{baseUrl}-darwin-x64.tar.gz";
            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64) return $"{baseUrl}-darwin-arm64.tar.gz";
        }

        return "";
    }

    private static string GetPlatformName() {

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "Windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "Linux";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "OSX";

        return "Unknown";
    }
}