using System.Numerics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[JsonObject(MemberSerialization.OptIn)]
internal class Level {

    // Custom converter to handle path relativization
    public class RelativePathConverter : JsonConverter {

        public override bool CanConvert(Type objectType) => objectType == typeof(string);

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer) {

            var val = (string?)reader.Value;

            if (string.IsNullOrEmpty(val)) return val;

            // Try explicit mod path first
            if (PathUtil.GetPath(val, out var fullPath)) return fullPath;

            // Try asset lookup
            if (Path.IsPathRooted(val)) return val;

            // If it's relative, assume it's relative to Mod Root or Resources
            if (PathUtil.GetPath(val, out var bestPath)) return bestPath;

            return val;
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) {

            var val = (string?)value;

            if (string.IsNullOrEmpty(val)) {

                writer.WriteValue(val);

                return;
            }

            var modPath = ScytheConfig.Current.Project;

            // Standardize separators
            val = val.Replace('\\', '/');
            if (!string.IsNullOrEmpty(modPath)) modPath = modPath.Replace('\\', '/');

            //  Try Mod Relative Path
            if (!string.IsNullOrEmpty(modPath) && val.StartsWith(modPath, StringComparison.OrdinalIgnoreCase)) {
                val = Path.GetRelativePath(modPath, val).Replace('\\', '/');
            } else {
                // Resources Heuristic: If it contains "/Resources/", relative to Parent of Resources
                // This covers both Source and Bin locations and ensures we output "Resources/..." 
                var resIndex = val.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase);

                if (resIndex != -1) {
                    // Keep "Resources/" prefix
                    // val is ".../Resources/Models/..." -> index points to first slash
                    val = val[(resIndex + 1)..];
                }
            }

            writer.WriteValue(val);
        }
    }

    public string Name { get; set; } = null!;
    public string JsonPath { get; set; } = null!;
    public bool IsDirty { get; set; }

    [JsonProperty] public readonly Obj Root = null!;
    [JsonProperty] public CameraData? EditorCamera;

    public class CameraData {

        public Vector3 Position;
        public Vector2 Rotation;
    }

    [JsonConstructor]
    private Level() {

        Root = new Obj("Root", null);
        Name = "New Level";
        JsonPath = "";
    }

    public Level(string? name) {

        if (name == null) return;

        Name = name;

        if (!PathUtil.GetPath($"Levels/{Name}.level.json", out var path))
            if (!PathUtil.GetPath($"{Name}.level.json", out path))
                if (!PathUtil.GetPath($"Levels/{Name}.json", out path))
                    if (!PathUtil.GetPath($"{Name}.json", out path))
                        throw new FileNotFoundException($"Could not find level file {Name} with .level.json or .json extension");

        JsonPath = path;
        Root = new Obj("Root", null);

        LoadInternal();
    }

    public Level(string name, string path, bool load = true) {

        Name = name;
        JsonPath = path;
        Root = new Obj("Root", null);

        if (load) LoadInternal();
    }

    public Level(string name, string path, string jsonBody) {

        Name = name;
        JsonPath = path;
        Root = new Obj("Root", null);
        LoadInternal(jsonBody);
    }

    private void LoadInternal(string? jsonOverride = null) {
        SafeExec.Try(() => {

                var jsonText = jsonOverride ?? File.ReadAllText(JsonPath);
                var rawData = JObject.Parse(jsonText);

                if (rawData["Root"]?["Children"] is JObject children) {

                    foreach (var property in children.Properties()) BuildHierarchy(new KeyValuePair<string, JToken>(property.Name, property.Value), Root);
                }

                // Load camera
                if (!CommandLine.Runtime && rawData["EditorCamera"] is JObject cameraJson) {

                    EditorCamera = cameraJson.ToObject<CameraData>();

                    if (EditorCamera == null) return;

                    FreeCam.Pos = EditorCamera.Position;
                    FreeCam.Rot = EditorCamera.Rotation;
                }
            }
        );
    }

    public void Save() {

        File.WriteAllText(JsonPath, ToSnapshot());
        IsDirty = false;
    }

    public string ToSnapshot() {

        if (!CommandLine.Runtime) {

            EditorCamera = new CameraData { Position = FreeCam.Pos, Rotation = FreeCam.Rot };
        }

        var settings = new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Ignore, NullValueHandling = NullValueHandling.Ignore, TypeNameHandling = TypeNameHandling.None, Converters = { new RelativePathConverter() } };

        return JsonConvert.SerializeObject(this, Formatting.Indented, settings);
    }

    private static void BuildHierarchy(KeyValuePair<string, JToken> dataPair, Obj parent) {

        if (dataPair.Value is not JObject data) return;

        var name = dataPair.Key;
        var obj = MakeObject(name, parent);

        // Load transform
        if (data["Transform"] is JObject) JsonConvert.PopulateObject(data["Transform"]!.ToString(), obj.Transform);

        // Load components
        var components = new Dictionary<string, Component>();

        if (data["Components"] is JObject jsonComponents) {

            foreach (var property in jsonComponents.Properties()) {

                if (Activator.CreateInstance(Type.GetType(property.Name) ?? throw new KeyNotFoundException($"{property.Name} cant be found"), obj) is not Component component) continue;

                JsonConvert.PopulateObject(data["Components"]![property.Name]!.ToString(), component);
                components[property.Name] = component;
            }
        }

        obj.Components = components;

        if (data["Children"] is not JObject children) return;

        foreach (var property in children.Properties()) BuildHierarchy(new KeyValuePair<string, JToken>(property.Name, property.Value), obj);
    }

    public static Obj MakeObject(string name, Obj? parent) {

        var obj = new Obj(parent == null ? name : Generators.AvailableName(name, parent.Children.Keys), parent);
        obj.SetParent(parent);

        return obj;
    }

    public static Obj RecordedMakeObject(string name, Obj? parent) {

        History.StartRecording(parent!, $"Create {name}");

        var obj = MakeObject(name, parent);

        History.SetUndoAction(obj.Delete);
        History.SetRedoAction(() => obj.SetParent(parent));

        if (Core.ActiveLevel != null) Core.ActiveLevel.IsDirty = true;
        History.StopRecording();

        return obj;
    }

    public Obj? Find(string[] names) => Root.Find(names);
    public Component? FindComponent(string[] names) => Root.FindComponent(names);
}