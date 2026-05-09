using ImageMagick;

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

        var args = new List<string> {
            "-y",
            "-hide_banner",
            "-loglevel",
            "error",
            "-i",
            sourceFile
        };

        var filter = BuildScaleFilter(settings);
        if (!string.IsNullOrWhiteSpace(filter)) {
            args.Add("-vf");
            args.Add(filter);
        }

        AddEncodingArgs(args, sourceFile, settings);
        args.Add("-frames:v");
        args.Add("1");
        args.Add(importedFile);

        var result = CommandRunner.Run("ffmpeg", args);
        if (result.ExitCode == 0 && File.Exists(importedFile)) return true;

        return TryImportWithMagick(sourceFile, importedFile, settings);
    }

    private static bool TryImportWithMagick(string sourceFile, string importedFile, AssetSidecarData.TextureImportSettings settings) {
        try {
            using var image = new MagickImage(sourceFile);
            ApplyResize(image, settings);
            ApplyEncoding(image, sourceFile, settings);
            image.Write(importedFile, GetMagickFormat(sourceFile, settings));
            return File.Exists(importedFile);
        } catch {
            return false;
        }
    }

    private static string BuildScaleFilter(AssetSidecarData.TextureImportSettings settings) {

        if (settings.MaxSize <= 0) return "";

        return $"scale={settings.MaxSize}:{settings.MaxSize}:force_original_aspect_ratio=decrease:flags={GetScaleFlag(settings.ResizeFilter)}";
    }

    private static void ApplyResize(MagickImage image, AssetSidecarData.TextureImportSettings settings) {
        if (settings.MaxSize <= 0) return;

        image.FilterType = GetMagickFilter(settings.ResizeFilter);
        image.Resize(new MagickGeometry((uint)settings.MaxSize, (uint)settings.MaxSize) {
            IgnoreAspectRatio = false,
            Greater = true
        });
    }

    private static void ApplyEncoding(MagickImage image, string sourceFile, AssetSidecarData.TextureImportSettings settings) {
        image.Format = GetMagickFormat(sourceFile, settings);

        if (UsesQuality(GetEffectiveFormat(sourceFile, settings)))
            image.Quality = (uint)Math.Clamp(settings.Quality, 1, 100);
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

    private static MagickFormat GetMagickFormat(string sourceFile, AssetSidecarData.TextureImportSettings settings) => GetEffectiveFormat(sourceFile, settings) switch {
        "Jpeg" => MagickFormat.Jpeg,
        "WebP" => MagickFormat.WebP,
        "Avif" => MagickFormat.Avif,
        _ => MagickFormat.Png
    };

    private static string GetScaleFlag(string filter) => filter switch {
        "Nearest" => "neighbor",
        "Bicubic" => "bicubic",
        "Lanczos" => "lanczos",
        _ => "bilinear"
    };

    private static FilterType GetMagickFilter(string filter) => filter switch {
        "Nearest" => FilterType.Point,
        "Bicubic" => FilterType.Cubic,
        "Lanczos" => FilterType.Lanczos,
        _ => FilterType.Triangle
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
