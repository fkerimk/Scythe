using System.Diagnostics;

internal static class TextureImportProcessor {

    public static string GetEffectiveFormat(string sourceFile, AssetSidecarData.TextureImportSettings settings) => settings.Format switch {
        "Png" => "Png",
        "Jpeg" => "Jpeg",
        "WebP" => "WebP",
        "Avif" => "Avif",
        _ => GetFormatFromExtension(Path.GetExtension(sourceFile).ToLowerInvariant())
    };

    public static bool UsesCompression(string effectiveFormat) => effectiveFormat is "Png" or "WebP" or "Avif";

    public static bool UsesQuality(string effectiveFormat) => effectiveFormat is "Jpeg" or "WebP" or "Avif";

    public static string BuildImportedPath(string importsFolder, string guid, string sourceFile, AssetSidecarData.TextureImportSettings settings) {

        var ext = GetOutputExtension(sourceFile, settings);
        return Path.Combine(importsFolder, guid + ext);
    }

    public static bool IsCurrent(string sourceFile, string importedFile, AssetSidecarData.TextureImportSettings settings) {

        if (!File.Exists(sourceFile) || !File.Exists(importedFile)) return false;

        var importedTime = new FileInfo(importedFile).LastWriteTimeUtc;
        var sourceTime = new FileInfo(sourceFile).LastWriteTimeUtc;
        var sidecarPath = sourceFile + ".json";
        var sidecarTime = File.Exists(sidecarPath) ? new FileInfo(sidecarPath).LastWriteTimeUtc : DateTime.MinValue;

        return importedTime >= sourceTime && importedTime >= sidecarTime;
    }

    public static bool Import(string sourceFile, string importedFile, AssetSidecarData.TextureImportSettings settings) {

        Directory.CreateDirectory(Path.GetDirectoryName(importedFile)!);

        var psi = new ProcessStartInfo("ffmpeg") {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-loglevel");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(sourceFile);

        var filter = BuildScaleFilter(settings);
        if (!string.IsNullOrWhiteSpace(filter)) {

            psi.ArgumentList.Add("-vf");
            psi.ArgumentList.Add(filter);
        }

        AddEncodingArgs(psi.ArgumentList, sourceFile, settings);

        psi.ArgumentList.Add("-frames:v");
        psi.ArgumentList.Add("1");
        psi.ArgumentList.Add(importedFile);

        using var process = Process.Start(psi);
        if (process == null) return false;
        process.WaitForExit();

        return process.ExitCode == 0 && File.Exists(importedFile);
    }

    private static string BuildScaleFilter(AssetSidecarData.TextureImportSettings settings) {

        if (settings.MaxSize <= 0) return "";

        return $"scale={settings.MaxSize}:{settings.MaxSize}:force_original_aspect_ratio=decrease:flags={GetScaleFlag(settings.ResizeFilter)}";
    }

    private static void AddEncodingArgs(ICollection<string> args, string sourceFile, AssetSidecarData.TextureImportSettings settings) {

        switch (GetOutputExtension(sourceFile, settings)) {

            case ".png":
                args.Add("-compression_level");
                args.Add(settings.Compression switch {
                    "Fast" => "1",
                    "Best" => "9",
                    _ => "6"
                });
                break;

            case ".jpg":
                args.Add("-q:v");
                args.Add(MapJpegQuality(settings.Quality));
                args.Add("-pix_fmt");
                args.Add("yuv420p");
                break;

            case ".webp":
                args.Add("-c:v");
                args.Add("libwebp");
                args.Add("-compression_level");
                args.Add(settings.Compression switch {
                    "Fast" => "1",
                    "Best" => "6",
                    _ => "4"
                });
                args.Add("-q:v");
                args.Add(settings.Quality.ToString());
                break;

            case ".avif":
                args.Add("-c:v");
                args.Add("libaom-av1");
                args.Add("-still-picture");
                args.Add("1");
                args.Add("-cpu-used");
                args.Add(settings.Compression switch {
                    "Fast" => "8",
                    "Best" => "2",
                    _ => "5"
                });
                args.Add("-crf");
                args.Add(MapAvifCrf(settings.Quality));
                args.Add("-pix_fmt");
                args.Add("yuv420p");
                break;
        }
    }

    private static string GetOutputExtension(string sourceFile, AssetSidecarData.TextureImportSettings settings) => GetEffectiveFormat(sourceFile, settings) switch {
        "Jpeg" => ".jpg",
        "WebP" => ".webp",
        "Avif" => ".avif",
        _ => ".png"
    };

    private static string GetFormatFromExtension(string ext) => ext switch {
        ".jpg" => "Jpeg",
        ".jpeg" => "Jpeg",
        ".png" => "Png",
        ".webp" => "WebP",
        ".avif" => "Avif",
        _ => "Png"
    };

    private static string GetScaleFlag(string filter) => filter switch {
        "Nearest" => "neighbor",
        "Bicubic" => "bicubic",
        "Lanczos" => "lanczos",
        _ => "bilinear"
    };

    private static string MapJpegQuality(int quality) {

        quality = Math.Clamp(quality, 1, 100);
        var q = 31 - (int)MathF.Round((quality - 1) / 99f * 29f);
        return Math.Clamp(q, 2, 31).ToString();
    }

    private static string MapAvifCrf(int quality) {

        quality = Math.Clamp(quality, 1, 100);
        var crf = 63 - (int)MathF.Round((quality - 1) / 99f * 51f);
        return Math.Clamp(crf, 12, 63).ToString();
    }
}
