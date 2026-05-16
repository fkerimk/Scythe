using Newtonsoft.Json.Linq;
using Raylib_cs;

internal class LevelAsset : Asset {

    [RecordHistory] public string Skybox { get; set; } = "";
    [RecordHistory] public string SkyboxPath { get; set; } = "";
    [RecordHistory] public Color SkyboxTint { get; set; } = Color.White;
    [RecordHistory] public Color BackgroundColor { get; set; } = new Color(25, 25, 25, 255);
    [RecordHistory] public Color AmbientColor { get; set; } = Color.White;
    [RecordHistory] public bool SkyboxAmbientEnabled { get; set; }
    [RecordHistory] public float SkyboxAmbientIntensity { get; set; } = 1.0f;

    public override bool Load() {

        if (!System.IO.File.Exists(File)) return false;

        var changed = false;
        var json = JObject.Parse(System.IO.File.ReadAllText(File));
        var guid = json["GUID"]?.Value<string>();

        if (string.IsNullOrWhiteSpace(guid)) {
            guid = System.Guid.NewGuid().ToString("N");
            json["GUID"] = guid;
            changed = true;
        }

        GUID = guid;
        Skybox = json["Skybox"]?.Value<string>() ?? "";
        SkyboxPath = json["SkyboxPath"]?.Value<string>() ?? "";
        SkyboxTint = json["SkyboxTint"]?.ToObject<Color?>() ?? Color.White;
        BackgroundColor = json["BackgroundColor"]?.ToObject<Color?>() ?? new Color(25, 25, 25, 255);
        AmbientColor = json["AmbientColor"]?.ToObject<Color?>() ?? Color.White;
        SkyboxAmbientEnabled = json["SkyboxAmbientEnabled"]?.Value<bool>() ?? false;
        SkyboxAmbientIntensity = Math.Max(json["SkyboxAmbientIntensity"]?.Value<float>() ?? 1.0f, 0.0f);

        if (changed)
            JsonFile.WriteIndented(File, json, ensureDirectory: false);

        IsLoaded = true;
        ThumbnailDirty = true;
        if (!AssetManager.IsInitializing) Preview.UpdateThumbnail(this);
        return true;
    }

    public override void Unload() {

        if (Thumbnail.HasValue) {
            Raylib.UnloadTexture(Thumbnail.Value);
            Thumbnail = null;
        }

        ThumbnailDirty = true;
        IsLoaded = false;
    }

    public override IEnumerable<string> GetWatchedFiles() {

        yield return File;
    }

    public void SaveSettings() {

        var json = System.IO.File.Exists(File) ? JObject.Parse(System.IO.File.ReadAllText(File)) : [];
        json["GUID"] = GUID;
        json["Skybox"] = Skybox;
        json["SkyboxPath"] = SkyboxPath;
        json["SkyboxTint"] = JToken.FromObject(SkyboxTint);
        json["BackgroundColor"] = JToken.FromObject(BackgroundColor);
        json["AmbientColor"] = JToken.FromObject(AmbientColor);
        json["SkyboxAmbientEnabled"] = SkyboxAmbientEnabled;
        json["SkyboxAmbientIntensity"] = SkyboxAmbientIntensity;

        AssetManager.RegisterInternalWrite(File);
        JsonFile.WriteIndented(File, json, ensureDirectory: false);
    }

    public void ApplyToActiveLevelIfOpen() {

        if (Core.ActiveLevel == null) return;
        if (!Path.GetFullPath(Core.ActiveLevel.JsonPath).Equals(Path.GetFullPath(File), StringComparison.OrdinalIgnoreCase)) return;

        Core.ActiveLevel.Skybox = Skybox;
        Core.ActiveLevel.SkyboxPath = SkyboxPath;
        Core.ActiveLevel.SkyboxTint = SkyboxTint;
        Core.ActiveLevel.BackgroundColor = BackgroundColor;
        Core.ActiveLevel.AmbientColor = AmbientColor;
        Core.ActiveLevel.SkyboxAmbientEnabled = SkyboxAmbientEnabled;
        Core.ActiveLevel.SkyboxAmbientIntensity = SkyboxAmbientIntensity;
        Core.ActiveLevel.IsDirty = true;
        Core.ApplyLevelVisualSettings();
    }
}
