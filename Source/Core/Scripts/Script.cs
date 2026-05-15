using Raylib_cs;
using Newtonsoft.Json;
using System.Reflection;

internal class Script(Obj obj) : Component(obj) {

    public override string LabelIcon => Icons.FaCode;
    public override Color LabelColor => Color.White;

    [Label("Asset"), JsonProperty("GUID"), RecordHistory, FindAsset("ScriptAsset")]
    public string GUID { get; set; } = "";

    [JsonProperty("Path"), RecordHistory]
    public string Path { get; set; } = "";

    [JsonProperty("Exposed"), RecordHistory]
    public Dictionary<string, string> ExposedValues { get; set; } = [];

    public ScytheScript? Instance;

    private bool _started;
    private ScytheScript? _hotReloadInstance;
    private bool _hotReloadStarted;

    public override bool Load() {

        if (!CommandLine.Runtime && !Core.IsPlaying) return true;

        var asset = ResolveAssetReference(markDirty: !Core.IsLoadingLevel);
        if (asset == null || !asset.IsLoaded || asset.ScriptType == null) return false;

        Instance = Activator.CreateInstance(asset.ScriptType) as ScytheScript;

        if (Instance == null) return false;

        Instance.Obj = Obj;
        RestoreHotReloadState();
        ApplyStoredFieldValues(asset);

        return true;
    }

    public void EnsureStarted() {

        if ((!CommandLine.Runtime && !Core.IsPlaying) || Instance == null || _started) return;

        _started = true;
        Instance.Start();
    }

    public override void Logic() {

        if ((!CommandLine.Runtime && !Core.IsPlaying) || Instance == null) return;

        EnsureStarted();

        Instance.Loop(Raylib.GetFrameTime());
    }

    public void PrepareForHotReload() {

        _hotReloadInstance = Instance;
        _hotReloadStarted = _started;
    }

    private void RestoreHotReloadState() {

        var previous = _hotReloadInstance;
        var started = _hotReloadStarted;

        _hotReloadInstance = null;
        _hotReloadStarted = false;

        if (Instance == null || previous == null) {
            _started = false;
            return;
        }

        CopyScriptFields(previous, Instance);
        Instance.Obj = Obj;
        _started = started;
    }

    public ScriptAsset? GetAsset() =>
        ResolveAssetReference(markDirty: !Core.IsLoadingLevel)
        ?? AssetManager.Get<ScriptAsset>(GUID)
        ?? AssetManager.Get<ScriptAsset>(Path)
        ?? AssetManager.GetOrImport<ScriptAsset>(Path);

    public bool UsesAsset(ScriptAsset asset) {

        var resolved = GetAsset();
        if (resolved == null) return false;
        return string.Equals(resolved.GUID, asset.GUID, StringComparison.OrdinalIgnoreCase)
            || string.Equals(System.IO.Path.GetFullPath(resolved.File), System.IO.Path.GetFullPath(asset.File), StringComparison.OrdinalIgnoreCase);
    }

    public object? GetExposeFieldValue(FieldInfo field, ScriptAsset asset) {

        if (ExposedValues.TryGetValue(field.Name, out var raw))
            return ScriptFieldUtility.DeserializeStoredValue(raw, field.FieldType, Obj);

        if (Instance != null && field.DeclaringType?.IsAssignableFrom(Instance.GetType()) == true)
            return field.GetValue(Instance);

        return asset.GetConfigFieldValue(field);
    }

    public void SetExposeFieldValue(FieldInfo field, object? value) {

        var asset = GetAsset();
        var baseValue = asset?.GetConfigFieldValue(field) ?? ScriptFieldUtility.GetCodeDefaultValue(field.DeclaringType, field);

        if (ScriptFieldUtility.ValueEquals(value, baseValue))
            ExposedValues.Remove(field.Name);
        else
            ExposedValues[field.Name] = ScriptFieldUtility.SerializeStoredValue(value);

        PrefabUtility.UpdateComponentOverrideState(this, nameof(ExposedValues), ExposedValues);
        if (Core.ActiveLevel != null) Core.ActiveLevel.IsDirty = true;
        ApplyFieldValueToInstance(field, value);
    }

    public void ReapplyStoredFieldValues(ScriptAsset? asset = null) {

        asset ??= GetAsset();
        if (Instance == null || asset?.ScriptType == null) return;

        ApplyStoredFieldValues(asset);
    }

    private void ApplyStoredFieldValues(ScriptAsset asset) {

        if (Instance == null || asset.ScriptType == null) return;

        foreach (var field in ScriptFieldUtility.GetFields(asset.ScriptType, ScriptFieldStorageKind.Config))
            ApplyFieldValueToInstance(field, asset.GetConfigFieldValue(field));

        foreach (var field in ScriptFieldUtility.GetFields(asset.ScriptType, ScriptFieldStorageKind.Expose))
            ApplyFieldValueToInstance(field, GetStoredExposeFieldValue(field, asset));
    }

    private object? GetStoredExposeFieldValue(FieldInfo field, ScriptAsset asset) =>
        ExposedValues.TryGetValue(field.Name, out var raw)
            ? ScriptFieldUtility.DeserializeStoredValue(raw, field.FieldType, Obj)
            : asset.GetConfigFieldValue(field);

    private void ApplyFieldValueToInstance(FieldInfo field, object? value) {

        if (Instance == null) return;
        if (field.DeclaringType?.IsAssignableFrom(Instance.GetType()) != true) return;

        field.SetValue(Instance, ScriptFieldUtility.ResolveStoredValueForAssignment(value, field.FieldType, Obj));
    }

    private ScriptAsset? ResolveAssetReference(bool markDirty) {

        var oldGuid = GUID;
        var oldPath = Path;
        var guid = GUID;
        var path = Path;
        var asset = AssetManager.ResolveReference<ScriptAsset>(ref guid, ref path);
        GUID = guid;
        Path = path;

        if (markDirty && (GUID != oldGuid || Path != oldPath) && Core.ActiveLevel != null)
            Core.ActiveLevel.IsDirty = true;

        return asset;
    }

    private static void CopyScriptFields(object source, object target) {

        var sourceFields = source.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .ToDictionary(field => field.Name, StringComparer.Ordinal);
        var targetFields = target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        foreach (var targetField in targetFields) {
            if (targetField.IsInitOnly) continue;
            if (!sourceFields.TryGetValue(targetField.Name, out var sourceField)) continue;
            if (!string.Equals(sourceField.FieldType.FullName, targetField.FieldType.FullName, StringComparison.Ordinal)) continue;

            var sourceValue = sourceField.GetValue(source);
            if (!CanAssignHotReloadValue(targetField.FieldType, sourceValue)) continue;

            targetField.SetValue(target, sourceValue);
        }
    }

    private static bool CanAssignHotReloadValue(Type targetType, object? value) {

        if (value == null) return !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null;

        return targetType.IsInstanceOfType(value);
    }
}
