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

#if !SCYTHE_RUNTIME_BUILD
        ScriptCompiler.CompileProject(); 
#endif
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

        var ownerAsset = ResolveConfigOwnerAsset(field);
        if (ownerAsset != null && !ReferenceEquals(ownerAsset, this))
            return ownerAsset.GetConfigFieldValue(field);

        var value = ScriptFieldUtility.GetCodeDefaultValue(ScriptType, field);
        if (ConfigValues.TryGetValue(field.Name, out var raw))
            value = ScriptFieldUtility.DeserializeStoredValue(raw, field.FieldType);

        return value;
    }

    public object? GetInspectorConfigFieldValue(FieldInfo field) {

        if (!Core.IsPlaying)
            return GetConfigFieldValue(field);

        var runtimeValues = GetRuntimeConfigFieldValues(field).ToList();
        return runtimeValues.Count == 0
            ? GetConfigFieldValue(field)
            : runtimeValues.All(value => ScriptFieldUtility.ValueEquals(value, runtimeValues[0])) ? runtimeValues[0] : null;
    }

    public void SetConfigFieldValue(FieldInfo field, object? value) {

        var ownerAsset = ResolveConfigOwnerAsset(field);
        if (ownerAsset != null && !ReferenceEquals(ownerAsset, this)) {
            ownerAsset.SetConfigFieldValue(field, value);
            return;
        }

        var defaultValue = ScriptFieldUtility.GetCodeDefaultValue(ScriptType, field);

        if (ScriptFieldUtility.ValueEquals(value, defaultValue))
            ConfigValues.Remove(field.Name);
        else
            ConfigValues[field.Name] = ScriptFieldUtility.SerializeStoredValue(value);

        SaveMeta();
        ApplyConfigToScripts();
    }

    public void SetInspectorConfigFieldValue(FieldInfo field, object? value) {

        if (!Core.IsPlaying) {
            SetConfigFieldValue(field, value);
            return;
        }

        foreach (var level in Core.OpenLevels)
            ApplyRuntimeConfigToScripts(level.Root, field, value);

        BackgroundScripts.SetRuntimeConfigFieldValue(this, field, value);
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

        BackgroundScripts.ApplyConfigToScripts(this);
    }

    private void ApplyConfigToScripts(Obj obj) {

        foreach (var component in obj.ComponentEntries.Values)
            if (component is Script script && script.UsesAsset(this))
                script.ReapplyStoredFieldValues(this);

        foreach (var child in obj.ChildEntries.Values) ApplyConfigToScripts(child);
    }

    private IEnumerable<object?> GetRuntimeConfigFieldValues(FieldInfo field) {

        foreach (var level in Core.OpenLevels)
            foreach (var value in GetRuntimeConfigFieldValues(level.Root, field))
                yield return value;

        foreach (var value in BackgroundScripts.GetRuntimeConfigFieldValues(this, field))
            yield return value;
    }

    private IEnumerable<object?> GetRuntimeConfigFieldValues(Obj obj, FieldInfo field) {

        foreach (var component in obj.ComponentEntries.Values) {
            if (component is Script script
                && script.UsesAsset(this)
                && script.Instance != null
                && field.DeclaringType?.IsAssignableFrom(script.Instance.GetType()) == true)
                yield return field.GetValue(script.Instance);
        }

        foreach (var child in obj.ChildEntries.Values)
            foreach (var value in GetRuntimeConfigFieldValues(child, field))
                yield return value;
    }

    private void ApplyRuntimeConfigToScripts(Obj obj, FieldInfo field, object? value) {

        foreach (var component in obj.ComponentEntries.Values) {
            if (component is not Script script
                || !script.UsesAsset(this)
                || script.Instance == null
                || field.DeclaringType?.IsAssignableFrom(script.Instance.GetType()) != true)
                continue;

            field.SetValue(script.Instance, ScriptFieldUtility.ResolveStoredValueForAssignment(value, field.FieldType, script.Obj));
        }

        foreach (var child in obj.ChildEntries.Values)
            ApplyRuntimeConfigToScripts(child, field, value);
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

    private ScriptAsset? ResolveConfigOwnerAsset(FieldInfo field) {

        if (field.DeclaringType == null || field.DeclaringType == ScriptType)
            return this;

        return AssetManager.GetAll<ScriptAsset>()
            .FirstOrDefault(asset => asset.IsLoaded && asset.ScriptType == field.DeclaringType);
    }
}
