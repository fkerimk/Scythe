using Raylib_cs;
using Newtonsoft.Json;
using System.Reflection;

internal class Script(Obj obj) : Component(obj) {

    public override string LabelIcon => Icons.FaCode;
    public override Color LabelColor => Color.White;

    [Label("Asset"), JsonProperty("GUID"), RecordHistory, FindAsset("ScriptAsset")]
    public string GUID { get; set; } = "";

    [JsonProperty("Path")]
    public string Path { get; set; } = "";

    public ScytheScript? Instance;

    private bool _started;
    private ScytheScript? _hotReloadInstance;
    private bool _hotReloadStarted;

    public override bool Load() {

        if (!CommandLine.Runtime && !Core.IsPlaying) return true;

        var oldGuid = GUID;
        var oldPath = Path;
        var guid = GUID;
        var path = Path;
        var asset = AssetManager.ResolveReference<ScriptAsset>(ref guid, ref path);
        GUID = guid;
        Path = path;

        if ((GUID != oldGuid || Path != oldPath) && Core.ActiveLevel != null && !Core.IsLoadingLevel) Core.ActiveLevel.IsDirty = true;

        if (asset == null || !asset.IsLoaded || asset.ScriptType == null) return false;

        Instance = Activator.CreateInstance(asset.ScriptType) as ScytheScript;

        if (Instance == null) return false;

        Instance.Obj = Obj;
        RestoreHotReloadState();

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

    private static void CopyScriptFields(object source, object target) {

        var sourceFields = source.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .ToDictionary(field => field.Name, StringComparer.Ordinal);
        var targetFields = target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        foreach (var targetField in targetFields) {
            if (targetField.IsInitOnly) continue;
            if (!sourceFields.TryGetValue(targetField.Name, out var sourceField)) continue;
            if (!string.Equals(sourceField.FieldType.FullName, targetField.FieldType.FullName, StringComparison.Ordinal)) continue;

            targetField.SetValue(target, sourceField.GetValue(source));
        }
    }
}
