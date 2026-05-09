using System.Reflection;

internal class ScriptAsset : Asset {

    public Assembly? Assembly;
    public Type? ScriptType;
    [RecordHistory] public Dictionary<string, string> ConfigValues { get; private set; } = [];

    public override bool Load() {
        
        if (!System.IO.File.Exists(File)) return false;

        var meta = ReadSidecarData();
        var changed = false;
        if (string.IsNullOrWhiteSpace(meta.GUID)) {

            meta.GUID = System.Guid.NewGuid().ToString("N");
            changed = true;
        }

        GUID = meta.GUID;
        ConfigValues = meta.ScriptConfig ?? [];
        if (changed) WriteSidecarData(meta);

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

    public object? GetConfigFieldValue(FieldInfo field) {

        var value = ScriptFieldUtility.GetCodeDefaultValue(ScriptType, field);
        if (ConfigValues.TryGetValue(field.Name, out var raw))
            value = ScriptFieldUtility.DeserializeStoredValue(raw, field.FieldType);

        return value;
    }

    public void SetConfigFieldValue(FieldInfo field, object? value) {

        var defaultValue = ScriptFieldUtility.GetCodeDefaultValue(ScriptType, field);

        if (ScriptFieldUtility.ValueEquals(value, defaultValue))
            ConfigValues.Remove(field.Name);
        else
            ConfigValues[field.Name] = ScriptFieldUtility.SerializeStoredValue(value);

        SaveMeta();
        ApplyConfigToScripts();
    }

    public void SaveMeta() {

        var meta = ReadSidecarData();
        meta.GUID = GUID;
        meta.ScriptConfig = new Dictionary<string, string>(ConfigValues);
        WriteSidecarData(meta);
    }

    public void ApplyConfigToScripts() {

        foreach (var level in Core.OpenLevels)
            ApplyConfigToScripts(level.Root);
    }

    private void ApplyConfigToScripts(Obj obj) {

        foreach (var component in obj.Components.Values)
            if (component is Script script && script.UsesAsset(this))
                script.ReapplyStoredFieldValues(this);

        foreach (var child in obj.Children.Values) ApplyConfigToScripts(child);
    }

    private AssetSidecarData ReadSidecarData() {

        var jsonPath = File + ".json";
        return JsonFile.ReadOrDefault(jsonPath, new AssetSidecarData());
    }

    private void WriteSidecarData(AssetSidecarData meta) {

        var jsonPath = File + ".json";
        AssetManager.RegisterInternalWrite(jsonPath);
        JsonFile.WriteIndented(jsonPath, meta);
    }
}
