using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

internal static class BundleRuntime {

    private const string BundleResourceName = "Scythe.Bundle.zip";
    private static string _bundleRoot = "";

    public static bool IsActive { get; private set; }
    public static string ProjectRoot => Path.Combine(_bundleRoot, "Project");
    public static string ResourcesRoot => Path.Combine(_bundleRoot, "Resources");

    public static bool TryActivate() {

        if (IsActive) return true;

        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(BundleResourceName);
        if (stream == null) return false;

        var identity = GetBundleIdentity(stream);
        stream.Position = 0;

        _bundleRoot = Path.Combine(Path.GetTempPath(), "ScytheBundles", identity);
        Directory.CreateDirectory(_bundleRoot);

        var marker = Path.Combine(_bundleRoot, ".ready");
        if (!File.Exists(marker)) {

            if (Directory.Exists(_bundleRoot)) {
                foreach (var entry in Directory.EnumerateFileSystemEntries(_bundleRoot).ToList()) {
                    if (Directory.Exists(entry)) Directory.Delete(entry, true);
                    else File.Delete(entry);
                }
            }

            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            archive.ExtractToDirectory(_bundleRoot, overwriteFiles: true);
            File.WriteAllText(marker, identity);
        }

        IsActive = true;
        return true;
    }

    public static string GetBaseRoot() => IsActive ? _bundleRoot : AppContext.BaseDirectory;

    public static string GetResourcesRoot() =>
        IsActive && Directory.Exists(ResourcesRoot)
            ? ResourcesRoot
            : Path.Combine(AppContext.BaseDirectory, "Resources");

    private static string GetBundleIdentity(Stream stream) {

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        var name = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "Scythe");
        return $"{Sanitize(name)}_{Convert.ToHexString(hash)[..16]}";
    }

    private static string Sanitize(string value) {

        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder(value.Length);

        foreach (var ch in value)
            builder.Append(invalid.Contains(ch) ? '_' : ch);

        return builder.ToString();
    }
}
