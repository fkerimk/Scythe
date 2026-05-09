using Newtonsoft.Json.Linq;

internal class LevelAsset : Asset {

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

        if (changed)
            JsonFile.WriteIndented(File, json, ensureDirectory: false);

        IsLoaded = true;
        return true;
    }

    public override void Unload() {

        IsLoaded = false;
    }

    public override IEnumerable<string> GetWatchedFiles() {

        yield return File;
    }
}
