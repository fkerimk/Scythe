using System.Reflection;

internal class ScriptAsset : Asset {

    public Assembly? Assembly;
    public Type? ScriptType;

    public override bool Load() {
        
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

        ScriptCompiler.CompileProject(); 
        AssignFromAssembly();
        return true;
    }

    public void AssignFromAssembly() {
        
        if (ScriptCompiler.ProjectAssembly == null) return;
        
        Assembly = ScriptCompiler.ProjectAssembly;
        var typeName = Path.GetFileNameWithoutExtension(File);
        ScriptType = Assembly.GetTypes().FirstOrDefault(t => t.Name == typeName && typeof(ScytheScript).IsAssignableFrom(t) && !t.IsAbstract);
        IsLoaded = true;
    }

    public override void Unload() {
        
        Assembly = null;
        ScriptType = null;
        IsLoaded = false;
    }

    public override IEnumerable<string> GetWatchedFiles() {

        yield return File;
        yield return File + ".json";
    }
}
