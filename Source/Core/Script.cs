using Raylib_cs;
using Newtonsoft.Json;

internal class Script(Obj obj) : Component(obj) {

    public override string LabelIcon => Icons.FaCode;
    public override Color LabelColor => Color.White;

    [Label("Asset"), JsonProperty("GUID"), RecordHistory, FindAsset("ScriptAsset")]
    public string GUID { get; set; } = "";

    [JsonProperty("Path")]
    public string Path { get; set; } = "";

    public ScytheScript? Instance;

    private bool _started;

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
        _started = false;

        return true;
    }

    public override void Logic() {

        if ((!CommandLine.Runtime && !Core.IsPlaying) || Instance == null) return;

        if (!_started) {
            
            _started = true;
            Instance.Start();
        }

        Instance.Loop(Raylib.GetFrameTime());
    }
}
