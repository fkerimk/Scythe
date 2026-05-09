using Raylib_cs;
using static Raylib_cs.Raylib;

internal class TextureAsset : Asset {

    public Texture2D Texture;
    public AssetSidecarData.TextureImportSettings ImportSettings { get; private set; } = new();
    public bool HasTransparentPixels { get; private set; }
    public int SourceWidth { get; private set; }
    public int SourceHeight { get; private set; }
    public int ImportedWidth { get; private set; }
    public int ImportedHeight { get; private set; }
    public long SourceFileSize { get; private set; }
    public long ImportedFileSize { get; private set; }

    public override unsafe bool Load() {

        ImportedFile = "";
        if (!System.IO.File.Exists(File)) return false;
        SourceFileSize = new FileInfo(File).Length;
        SourceWidth = 0;
        SourceHeight = 0;
        ImportedWidth = 0;
        ImportedHeight = 0;
        ImportedFileSize = 0;
        HasTransparentPixels = false;

        var sourceImage = LoadImage(File);
        if (sourceImage.Data != null) {

            SourceWidth = sourceImage.Width;
            SourceHeight = sourceImage.Height;
            HasTransparentPixels = DetectTransparency(sourceImage);
            UnloadImage(sourceImage);
        }

        var jsonPath = File + ".json";
        if (System.IO.File.Exists(jsonPath)) {

            var meta = JsonFile.ReadOrDefault(jsonPath, new AssetSidecarData());
            var changed = false;
            if (string.IsNullOrWhiteSpace(meta.GUID)) {

                meta.GUID = System.Guid.NewGuid().ToString("N");
                changed = true;
            }

            if (meta.TextureImport == null) {

                meta.TextureImport = new AssetSidecarData.TextureImportSettings();
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(meta.TextureImport.Format)) {

                meta.TextureImport.Format = GetDefaultFormat();
                changed = true;
            }

            GUID = meta.GUID;
            ImportSettings = (AssetSidecarData.TextureImportSettings)meta.TextureImport.Clone();
            if (changed) JsonFile.WriteIndented(jsonPath, meta);

        } else {

            GUID = System.Guid.NewGuid().ToString("N");
            var meta = new AssetSidecarData { GUID = GUID };
            meta.TextureImport.Format = GetDefaultFormat();
            ImportSettings = (AssetSidecarData.TextureImportSettings)meta.TextureImport.Clone();
            JsonFile.WriteIndented(jsonPath, meta);
        }

        if (ImportSettings == null) ImportSettings = new AssetSidecarData.TextureImportSettings();
        if (string.IsNullOrWhiteSpace(ImportSettings.Format)) ImportSettings.Format = GetDefaultFormat();
        if (string.IsNullOrWhiteSpace(ImportSettings.TextureFilter)) ImportSettings.TextureFilter = "Bilinear";

        ImportedFile = AssetManager.GetImportedTextureFile(File, GUID, ImportSettings);

        var image = LoadImage(ResolvedFile);
        if (image.Data == null) {

            image = TryLoadImportedFallbackImage();
        }

        if (image.Data == null) {

            ImportedFile = "";
            image = LoadImage(File);
            if (image.Data == null) return false;
        }

        Texture = LoadTextureFromImage(image);
        ApplyTextureFilter();
        UnloadImage(image);

        if (SourceWidth <= 0 || SourceHeight <= 0) {

            SourceWidth = Texture.Width;
            SourceHeight = Texture.Height;
        }

        ImportedWidth = Texture.Width;
        ImportedHeight = Texture.Height;
        ImportedFileSize = System.IO.File.Exists(ResolvedFile) ? new FileInfo(ResolvedFile).Length : SourceFileSize;

        IsLoaded = true;
        ThumbnailDirty = true;
        if (!AssetManager.IsInitializing) Preview.UpdateThumbnail(this);

        return true;
    }

    public override void Unload() {

        if (IsLoaded) {

            UnloadTexture(Texture);
            Texture = new Texture2D();
            if (Thumbnail.HasValue) {

                UnloadTexture(Thumbnail.Value);
                Thumbnail = null;
            }
        }

        ThumbnailDirty = true;
        IsLoaded = false;
        SourceWidth = 0;
        SourceHeight = 0;
        ImportedWidth = 0;
        ImportedHeight = 0;
        SourceFileSize = 0;
        ImportedFileSize = 0;
        HasTransparentPixels = false;
    }

    public override IEnumerable<string> GetWatchedFiles() {

        yield return File;
        yield return File + ".json";
    }

    public void SaveMeta() {

        var jsonPath = File + ".json";
        var meta = new AssetSidecarData {
            GUID = GUID,
            TextureImport = (AssetSidecarData.TextureImportSettings)ImportSettings.Clone()
        };

        AssetManager.RegisterInternalWrite(jsonPath);
        JsonFile.WriteIndented(jsonPath, meta);
    }

    public void ApplyTextureFilter() {

        if (!IsLoaded && Texture.Id == 0) return;
        SetTextureFilter(Texture, GetTextureFilter(ImportSettings.TextureFilter));
    }

    private string GetDefaultFormat() => HasTransparentPixels ? "Png" : "Jpeg";

    private static TextureFilter GetTextureFilter(string filter) => filter switch {
        "Point" => TextureFilter.Point,
        "Trilinear" => TextureFilter.Trilinear,
        "Anisotropic 4x" => TextureFilter.Anisotropic4X,
        "Anisotropic 8x" => TextureFilter.Anisotropic8X,
        "Anisotropic 16x" => TextureFilter.Anisotropic16X,
        _ => TextureFilter.Bilinear
    };

    private static unsafe bool DetectTransparency(Image image) {

        var colors = LoadImageColors(image);
        if (colors == null) return false;

        try {
            var pixelCount = image.Width * image.Height;
            for (var i = 0; i < pixelCount; i++) {

                if (colors[i].A < 255) return true;
            }

            return false;

        } finally {
            UnloadImageColors(colors);
        }
    }

    private Image TryLoadImportedFallbackImage() {

        if (string.IsNullOrWhiteSpace(ImportedFile) || !System.IO.File.Exists(ImportedFile)) return default;

        var effectiveFormat = TextureImportProcessor.GetEffectiveFormat(File, ImportSettings);
        if (effectiveFormat is not "WebP" and not "Avif") return default;

        var tempFile = Path.Combine(Path.GetTempPath(), $"scythe-{GUID}-{Guid.NewGuid():N}.png");

        try {
            var result = CommandRunner.Run("ffmpeg", [
                "-y",
                "-hide_banner",
                "-loglevel",
                "error",
                "-i",
                ImportedFile,
                "-frames:v",
                "1",
                tempFile
            ]);

            if (result.ExitCode != 0 || !System.IO.File.Exists(tempFile)) return default;

            return LoadImage(tempFile);

        } finally {
            try {
                if (System.IO.File.Exists(tempFile)) System.IO.File.Delete(tempFile);
            } catch {
            }
        }
    }
}
