using Raylib_cs;
using static Raylib_cs.Raylib;

internal class TextureAsset : Asset {

    public Texture2D Texture;

    public override unsafe bool Load() {

        if (!System.IO.File.Exists(File)) return false;

        var jsonPath = File + ".json";
        if (System.IO.File.Exists(jsonPath)) {

            var meta = Newtonsoft.Json.JsonConvert.DeserializeObject<AssetSidecarData>(System.IO.File.ReadAllText(jsonPath)) ?? new AssetSidecarData();
            var changed = false;
            if (string.IsNullOrWhiteSpace(meta.GUID)) {

                meta.GUID = System.Guid.NewGuid().ToString("N");
                changed = true;
            }

            GUID = meta.GUID;
            if (changed) System.IO.File.WriteAllText(jsonPath, Newtonsoft.Json.JsonConvert.SerializeObject(meta, Newtonsoft.Json.Formatting.Indented));

        } else {

            GUID = System.Guid.NewGuid().ToString("N");
            System.IO.File.WriteAllText(jsonPath, Newtonsoft.Json.JsonConvert.SerializeObject(new AssetSidecarData { GUID = GUID }, Newtonsoft.Json.Formatting.Indented));
        }

        var image = LoadImage(File);

        if (image.Data == null) return false;

        // Main Texture
        Texture = LoadTextureFromImage(image);
        SetTextureFilter(Texture, TextureFilter.Bilinear);
        UnloadImage(image);

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
    }

    public override IEnumerable<string> GetWatchedFiles() {

        yield return File;
        yield return File + ".json";
    }
}
