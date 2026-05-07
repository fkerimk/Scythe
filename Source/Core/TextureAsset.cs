using Raylib_cs;
using static Raylib_cs.Raylib;

internal class TextureAsset : Asset {

    public Texture2D Texture;
    public AssetSidecarData.TextureImportSettings ImportSettings { get; private set; } = new();
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

        var sourceImage = LoadImage(File);
        if (sourceImage.Data != null) {

            SourceWidth = sourceImage.Width;
            SourceHeight = sourceImage.Height;
            UnloadImage(sourceImage);
        }

        var jsonPath = File + ".json";
        if (System.IO.File.Exists(jsonPath)) {

            var meta = Newtonsoft.Json.JsonConvert.DeserializeObject<AssetSidecarData>(System.IO.File.ReadAllText(jsonPath)) ?? new AssetSidecarData();
            var changed = false;
            if (string.IsNullOrWhiteSpace(meta.GUID)) {

                meta.GUID = System.Guid.NewGuid().ToString("N");
                changed = true;
            }

            if (meta.TextureImport == null) {

                meta.TextureImport = new AssetSidecarData.TextureImportSettings();
                changed = true;
            }

            GUID = meta.GUID;
            ImportSettings = (AssetSidecarData.TextureImportSettings)meta.TextureImport.Clone();
            if (changed) System.IO.File.WriteAllText(jsonPath, Newtonsoft.Json.JsonConvert.SerializeObject(meta, Newtonsoft.Json.Formatting.Indented));

        } else {

            GUID = System.Guid.NewGuid().ToString("N");
            var meta = new AssetSidecarData { GUID = GUID };
            ImportSettings = (AssetSidecarData.TextureImportSettings)meta.TextureImport.Clone();
            System.IO.File.WriteAllText(jsonPath, Newtonsoft.Json.JsonConvert.SerializeObject(meta, Newtonsoft.Json.Formatting.Indented));
        }

        if (ImportSettings == null) ImportSettings = new AssetSidecarData.TextureImportSettings();

        ImportedFile = AssetManager.GetImportedTextureFile(File, GUID, ImportSettings);

        if (string.Equals(Path.GetExtension(ResolvedFile), ".stex", StringComparison.OrdinalIgnoreCase)) {

            if (!CompiledAssetCache.LoadTexture(ResolvedFile, out Texture)) {

                ImportedFile = "";
                var image = LoadImage(File);
                if (image.Data == null) return false;

                Texture = LoadTextureFromImage(image);
                SetTextureFilter(Texture, TextureFilter.Bilinear);
                UnloadImage(image);
            }

        } else {

            var image = LoadImage(ResolvedFile);
            if (image.Data == null) return false;

            Texture = LoadTextureFromImage(image);
            SetTextureFilter(Texture, TextureFilter.Bilinear);
            UnloadImage(image);
        }

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
        System.IO.File.WriteAllText(jsonPath, Newtonsoft.Json.JsonConvert.SerializeObject(meta, Newtonsoft.Json.Formatting.Indented));
    }
}
