using System.Diagnostics;
using Raylib_cs;
using static Raylib_cs.Raylib;

internal static class TextureImportProcessor {

    public static string BuildImportedPath(string importsFolder, string guid, string sourceFile, AssetSidecarData.TextureImportSettings settings) =>
        Path.Combine(importsFolder, guid + ".dds");

    public static bool IsCurrent(string sourceFile, string importedFile) {

        if (!File.Exists(sourceFile) || !File.Exists(importedFile)) return false;

        var importedTime = new FileInfo(importedFile).LastWriteTimeUtc;
        var sourceTime = new FileInfo(sourceFile).LastWriteTimeUtc;
        var sidecarPath = sourceFile + ".json";
        var sidecarTime = File.Exists(sidecarPath) ? new FileInfo(sidecarPath).LastWriteTimeUtc : DateTime.MinValue;

        return importedTime >= sourceTime && importedTime >= sidecarTime;
    }

    public static bool Import(string sourceFile, string importedFile, AssetSidecarData.TextureImportSettings settings) {

        Directory.CreateDirectory(Path.GetDirectoryName(importedFile)!);

        var tempSource = PrepareSourceImage(sourceFile, settings);
        if (tempSource == null) return false;

        try {

            var psi = new ProcessStartInfo("compressonatorcli") {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            psi.ArgumentList.Add("-silent");
            psi.ArgumentList.Add("-noprogress");
            psi.ArgumentList.Add("-fd");
            psi.ArgumentList.Add(GetCodec(settings, sourceFile));

            AddCodecArgs(psi.ArgumentList, settings);

            psi.ArgumentList.Add(tempSource);
            psi.ArgumentList.Add(importedFile);

            using var process = Process.Start(psi);
            if (process == null) return false;
            process.WaitForExit();

            return process.ExitCode == 0 && File.Exists(importedFile);

        } finally {

            SafeDelete(tempSource);
        }
    }

    private static unsafe string? PrepareSourceImage(string sourceFile, AssetSidecarData.TextureImportSettings settings) {

        var image = LoadImage(sourceFile);
        if (image.Data == null) return null;

        try {

            if (settings.MaxSize > 0 && (image.Width > settings.MaxSize || image.Height > settings.MaxSize)) {

                var scale = Math.Min((float)settings.MaxSize / image.Width, (float)settings.MaxSize / image.Height);
                var targetWidth = Math.Max(1, (int)MathF.Round(image.Width * scale));
                var targetHeight = Math.Max(1, (int)MathF.Round(image.Height * scale));

                if (string.Equals(settings.ResizeFilter, "Nearest", StringComparison.OrdinalIgnoreCase))
                    ImageResizeNN(ref image, targetWidth, targetHeight);
                else
                    ImageResize(ref image, targetWidth, targetHeight);
            }

            var tempFile = Path.Combine(Path.GetTempPath(), $"scythe_teximport_{Guid.NewGuid():N}.png");
            ExportImage(image, tempFile);
            return tempFile;

        } finally {

            UnloadImage(image);
        }
    }

    private static string GetCodec(AssetSidecarData.TextureImportSettings settings, string sourceFile) {

        if (!string.Equals(settings.Format, "Auto", StringComparison.OrdinalIgnoreCase)) return settings.Format;

        return HasAlpha(sourceFile) ? "BC3" : "BC1";
    }

    private static bool HasAlpha(string sourceFile) {

        var ext = Path.GetExtension(sourceFile).ToLowerInvariant();
        return ext is ".png" or ".tga" or ".webp" or ".avif";
    }

    private static void AddCodecArgs(ICollection<string> args, AssetSidecarData.TextureImportSettings settings) {

        switch (settings.Compression) {
            case "Fast":
                args.Add("-CompressionSpeed");
                args.Add("0.2");
                break;
            case "Best":
                args.Add("-CompressionSpeed");
                args.Add("1");
                break;
            default:
                args.Add("-CompressionSpeed");
                args.Add("0.5");
                break;
        }

        if (string.Equals(settings.Format, "BC7", StringComparison.OrdinalIgnoreCase)) {

            args.Add("-Quality");
            args.Add((Math.Clamp(settings.Quality, 1, 100) / 100f).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private static void SafeDelete(string path) {

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        try {
            File.Delete(path);
        } catch {
        }
    }
}
