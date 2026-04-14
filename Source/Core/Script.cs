using System.Numerics;
using Raylib_cs;
using MoonSharp.Interpreter;
using Newtonsoft.Json;

internal class Script(Obj obj) : Component(obj) {

    public override string LabelIcon => Icons.FaCode;
    public override Color LabelColor => Color.White;

    [Label("Path"), JsonProperty, RecordHistory, FindAsset("ScriptAsset")]
    public string Path { get; set; } = "";

    public required MoonSharp.Interpreter.Script LuaScript;
    public DynValue? LuaLoop;

    public static LuaMt? LuaMt;
    public static LuaTime? LuaTime;
    public static LuaKb? LuaKb;
    public static LuaMouse? LuaMouse;
    public static LuaF2? LuaF2;
    public static LuaF3? LuaF3;
    public static LuaQuat? LuaQuat;
    public static LuaGame? LuaGame;
    public static LuaColor? LuaColor;

    public static void Register() {

        // Objects & components
        UserData.RegisterType<Obj>();
        UserData.RegisterType<Obj?>();
        UserData.RegisterType<Component>();
        UserData.RegisterType<Animation>();
        UserData.RegisterType<Camera>();
        UserData.RegisterType<Light>();
        UserData.RegisterType<Model>();
        UserData.RegisterType<Script>();
        UserData.RegisterType<Transform>();
        UserData.RegisterType<Rigidbody>();
        UserData.RegisterType<BoxCollider>();
        UserData.RegisterType<SphereCollider>();

        // Libraries & class data types
        UserData.RegisterType<LuaMt>();
        LuaMt = new LuaMt();

        UserData.RegisterType<LuaTime>();
        LuaTime = new LuaTime();

        UserData.RegisterType<LuaKb>();
        LuaKb = new LuaKb();

        UserData.RegisterType<LuaMouse>();
        LuaMouse = new LuaMouse();

        UserData.RegisterType<LuaF2>();
        LuaF2 = new LuaF2();

        UserData.RegisterType<LuaF3>();
        LuaF3 = new LuaF3();

        UserData.RegisterType<LuaQuat>();
        LuaQuat = new LuaQuat();

        UserData.RegisterType<LuaGame>();
        LuaGame = new LuaGame();

        UserData.RegisterType<LuaColor>();
        LuaColor = new LuaColor();

        // Core classes
        UserData.RegisterType<Level>();
        UserData.RegisterType<RenderSettings>();

        // Data structures
        UserData.RegisterType<Vector2>();
        UserData.RegisterType<Vector3>();
        UserData.RegisterType<Quaternion>();
        UserData.RegisterType<Color>();
        UserData.RegisterType<LuaKey>();

        // Generate definitions
        Make(generateDefinitions: true);
    }

    private static MoonSharp.Interpreter.Script Make(Obj? obj = null, bool generateDefinitions = false) {

        var script = new MoonSharp.Interpreter.Script {
            Options = { DebugPrint = Console.WriteLine },
            Globals = {
                ["self"] = generateDefinitions ? new Obj(null!, null) : obj,
                ["level"] = generateDefinitions ? new Level(null!) : Core.ActiveLevel,
                ["cam"] = generateDefinitions ? new Camera(null!) { Cam = new Camera3D() } : FindFirstCameraComponent(Core.ActiveLevel?.Root),
                ["renderSettings"] = generateDefinitions ? new RenderSettings() : Core.RenderSettings,
                ["f2"] = LuaF2,
                ["f3"] = LuaF3,
                ["mt"] = LuaMt,
                ["time"] = LuaTime,
                ["kb"] = LuaKb,
                ["mouse"] = LuaMouse,
                ["quat"] = LuaQuat,
                ["game"] = LuaGame,
                ["color"] = LuaColor,
                ["key"] = typeof(LuaKey),
            }
        };

        if (!generateDefinitions) return script;

        PathUtil.ValidateFile("Project/Definitions.lua", out var definitionsPath, "", true);
        LuaDefinitionGenerator.Generate(script, definitionsPath);

        return script;
    }

    private static Camera? FindFirstCameraComponent(Obj? obj) {

        if (obj == null) return null;
        if (obj.Components.Values.FirstOrDefault(c => c is Camera) is Camera found) return found;

        foreach (var child in obj.Children.Values) {

            var cam = FindFirstCameraComponent(child);

            if (cam != null) return cam;
        }

        return null;
    }

    public override bool Load() {

        if (!CommandLine.Runtime && !Core.IsPlaying) return true;

        var asset = AssetManager.Get<ScriptAsset>(Path);

        if (asset == null || !asset.IsLoaded) return false;

        LuaScript = Make(Obj);

        SafeExec.LuaCall(() => LuaScript.DoString(asset.Content));
        LuaLoop = LuaScript.Globals.Get("loop");

        return true;
    }

    public override void Logic() {

        if ((!CommandLine.Runtime && !Core.IsPlaying) || LuaLoop == null || LuaLoop.IsNil()) return;

        SafeExec.LuaCall(() => LuaScript.Call(LuaLoop, Raylib.GetFrameTime()));
    }
}