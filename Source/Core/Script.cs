using Raylib_cs;
using Newtonsoft.Json;

internal class Script(Obj obj) : Component(obj) {

    public override string LabelIcon => Icons.FaCode;
    public override Color LabelColor => Color.White;

    [Label("Asset"), JsonProperty("GUID"), RecordHistory, FindAsset("ScriptAsset")]
    public string GUID { get; set; } = "";

    [JsonProperty("Path")]
    private string LegacyPath {
        set => GUID = AssetManager.GetGuid<ScriptAsset>(value) ?? value ?? "";
    }

    public ScytheScript? Instance;

    private bool _started;

    public override bool Load() {

        if (!CommandLine.Runtime && !Core.IsPlaying) return true;

        GUID = AssetManager.NormalizeReference<ScriptAsset>(GUID);
        var asset = AssetManager.Get<ScriptAsset>(GUID);

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
