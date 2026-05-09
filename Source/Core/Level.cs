using System.Numerics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Raylib_cs;

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

            // If it's relative, assume it's relative to Mod Root or the built-in collection
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
                var builtInIndex = val.IndexOf("/Collection/", StringComparison.OrdinalIgnoreCase);

                if (builtInIndex != -1) {
                    val = val[(builtInIndex + 1)..];
                } else {
                    var legacyIndex = val.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase);

                    if (legacyIndex != -1)
                        val = "Collection/" + Path.GetFileName(val);
                }
            }

            writer.WriteValue(val);
        }
    }

    public string Name { get; set; } = null!;
    [JsonProperty] public string GUID { get; set; } = System.Guid.NewGuid().ToString("N");
    public string JsonPath { get; set; } = null!;
    public bool IsPrefabDocument { get; private set; }
    public bool IsDirty { get; set; }
    [JsonProperty] public string Skybox { get; set; } = "";
    [JsonProperty] public string SkyboxPath { get; set; } = "";
    [JsonProperty] public Color SkyboxTint { get; set; } = Color.White;
    [JsonProperty] public Color BackgroundColor { get; set; } = new Color(25, 25, 25, 255);
    [JsonProperty] public Color AmbientColor { get; set; } = Color.White;
    [JsonProperty] public bool SkyboxAmbientEnabled { get; set; }
    [JsonProperty] public float SkyboxAmbientIntensity { get; set; } = 1.0f;

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
        GUID = System.Guid.NewGuid().ToString("N");
        JsonPath = "";
    }

    public Level(string? name) {

        if (name == null) return;

        Name = name;
        GUID = System.Guid.NewGuid().ToString("N");

        if (!PathUtil.GetPath($"Levels/{Name}.lvl", out var path))
            if (!PathUtil.GetPath($"{Name}.lvl", out path))
                throw new FileNotFoundException($"Could not find level file {Name} with .lvl extension");

        JsonPath = path;
        IsPrefabDocument = path.EndsWith(".pre", StringComparison.OrdinalIgnoreCase);
        Root = new Obj("Root", null);

        LoadInternal();
    }

    public Level(string name, string path, bool load = true, bool applyEditorCamera = true) {

        Name = name;
        GUID = System.Guid.NewGuid().ToString("N");
        JsonPath = path;
        IsPrefabDocument = path.EndsWith(".pre", StringComparison.OrdinalIgnoreCase);
        Root = new Obj("Root", null);

        if (load) LoadInternal(applyEditorCamera: applyEditorCamera);
    }

    public Level(string name, string path, string jsonBody, bool applyEditorCamera = true) {

        Name = name;
        GUID = System.Guid.NewGuid().ToString("N");
        JsonPath = path;
        IsPrefabDocument = path.EndsWith(".pre", StringComparison.OrdinalIgnoreCase);
        Root = new Obj("Root", null);
        LoadInternal(jsonBody, applyEditorCamera);
    }

    private void LoadInternal(string? jsonOverride = null, bool applyEditorCamera = true) {
        SafeExec.Try(() => {

                var jsonText = jsonOverride ?? File.ReadAllText(JsonPath);
                var rawData = JObject.Parse(jsonText);
                GUID = rawData["GUID"]?.Value<string>() ?? GUID;
                Skybox = rawData["Skybox"]?.Value<string>() ?? "";
                SkyboxPath = rawData["SkyboxPath"]?.Value<string>() ?? "";
                if (rawData["SkyboxTint"] is JObject skyboxTintJson) {
                    var parsedSkyboxTint = skyboxTintJson.ToObject<Color?>();
                    if (parsedSkyboxTint.HasValue) SkyboxTint = parsedSkyboxTint.Value;
                }
                SkyboxAmbientEnabled = rawData["SkyboxAmbientEnabled"]?.Value<bool>() ?? false;
                SkyboxAmbientIntensity = Math.Clamp(rawData["SkyboxAmbientIntensity"]?.Value<float>() ?? 1.0f, 0.0f, 1.0f);

                if (rawData["BackgroundColor"] is JObject backgroundColorJson) {
                    var parsedBackgroundColor = backgroundColorJson.ToObject<Color?>();
                    if (parsedBackgroundColor.HasValue) BackgroundColor = parsedBackgroundColor.Value;
                }

                if (rawData["AmbientColor"] is JObject ambientColorJson) {
                    var parsedAmbientColor = ambientColorJson.ToObject<Color?>();
                    if (parsedAmbientColor.HasValue) AmbientColor = parsedAmbientColor.Value;
                }

                if (rawData["Root"]?["Children"] is JObject children) {

                    foreach (var property in children.Properties()) BuildHierarchy(new KeyValuePair<string, JToken>(property.Name, property.Value), Root);
                }

                PrefabUtility.ApplyPrefabInstances(this);

                // Load camera
                if (!CommandLine.Runtime && rawData["EditorCamera"] is JObject cameraJson) {

                    EditorCamera = cameraJson.ToObject<CameraData>();

                    if (!applyEditorCamera || EditorCamera == null) return;

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
        var serializer = JsonSerializer.Create(settings);
        var root = JObject.FromObject(this, serializer);
        root["Root"] = BuildRootSnapshot(Root, serializer);
        return root.ToString(Formatting.Indented);
    }

    private static JObject BuildRootSnapshot(Obj root, JsonSerializer serializer) {

        var children = new JObject();

        foreach (var child in root.Children.Values) {

            var childToken = BuildObjectSnapshot(child, serializer);
            if (childToken != null)
                children[child.Name] = childToken;
        }

        return new JObject {
            ["Name"] = root.Name,
            ["Children"] = children
        };
    }

    private static JObject? BuildObjectSnapshot(Obj obj, JsonSerializer serializer) {

        var prefabRoot = obj.FindPrefabRoot();
        var isPrefabRoot = prefabRoot == obj && !string.IsNullOrWhiteSpace(obj.Prefab);

        if (prefabRoot == null || string.IsNullOrWhiteSpace(prefabRoot.Prefab)) {
            return CreateFullObjectSnapshot(obj, serializer);
        }

        if (!PrefabUtility.TryGetSourceObject(obj, out var sourceObj) || sourceObj == null) {
            var added = CreateFullObjectSnapshot(obj, serializer);
            added.Remove(nameof(Obj.Prefab));
            added.Remove(nameof(Obj.PrefabPath));
            var overrides = obj.PrefabOverrides.Where(value => !string.IsNullOrWhiteSpace(value)).Append("__added_child").Distinct().OrderBy(value => value);
            added[nameof(Obj.PrefabOverrides)] = JArray.FromObject(overrides);
            return added;
        }

        var token = new JObject();

        if (isPrefabRoot) {
            token["Prefab"] = obj.Prefab;
            token["PrefabPath"] = obj.PrefabPath;
            if (obj.PrefabOverrides.Count > 0) token["PrefabOverrides"] = JArray.FromObject(obj.PrefabOverrides.OrderBy(value => value));
            token["Transform"] = CreateFullComponentSnapshot(obj.Transform, serializer);
        } else if (obj.PrefabOverrides.Count > 0)
            token["PrefabOverrides"] = JArray.FromObject(obj.PrefabOverrides.OrderBy(value => value));

        if (!isPrefabRoot) {
            var transform = BuildSparseComponentSnapshot(obj.Transform, sourceObj.Transform, obj.Transform.PrefabOverrides, serializer);
            if (transform != null) token["Transform"] = transform;
        }

        var components = new JObject();

        foreach (var (componentName, component) in obj.Components) {

            if (!sourceObj.Components.TryGetValue(componentName, out var sourceComponent)) {
                components[componentName] = JObject.FromObject(component, serializer);
                continue;
            }

            var componentToken = BuildSparseComponentSnapshot(component, sourceComponent, component.PrefabOverrides, serializer);
            if (componentToken != null) components[componentName] = componentToken;
        }

        if (components.Count > 0) token["Components"] = components;

        var children = new JObject();

        foreach (var child in obj.Children.Values) {

            var childToken = BuildObjectSnapshot(child, serializer);
            if (childToken != null) children[child.Name] = childToken;
        }

        if (children.Count > 0) token["Children"] = children;

        return token.Count == 0 ? null : token;
    }

    private static JObject CreateFullObjectSnapshot(Obj obj, JsonSerializer serializer) {

        var token = JObject.FromObject(obj, serializer);
        StripPrefabMetadata(token);
        return token;
    }

    private static JObject CreateFullComponentSnapshot(object component, JsonSerializer serializer) {

        var token = JObject.FromObject(component, serializer);
        token.Remove(nameof(Component.PrefabOverrides));
        return token;
    }

    private static void StripPrefabMetadata(JObject token) {

        token.Remove(nameof(Obj.PrefabOverrides));

        if (token["Transform"] is JObject transformToken)
            transformToken.Remove(nameof(Component.PrefabOverrides));

        if (token["Components"] is JObject componentsToken)
            foreach (var componentToken in componentsToken.Properties().Select(property => property.Value).OfType<JObject>())
                componentToken.Remove(nameof(Component.PrefabOverrides));

        if (token["Children"] is JObject childrenToken)
            foreach (var childToken in childrenToken.Properties().Select(property => property.Value).OfType<JObject>())
                StripPrefabMetadata(childToken);
    }

    private static JObject? BuildSparseComponentSnapshot(object target, object source, IEnumerable<string> overrides, JsonSerializer serializer) {

        var overrideSet = overrides.Where(value => !string.IsNullOrWhiteSpace(value)).ToHashSet(StringComparer.Ordinal);
        if (overrideSet.Remove(nameof(Transform.Euler)))
            overrideSet.Add(nameof(Transform.Rot));
        if (overrideSet.Count == 0) return null;

        var full = JObject.FromObject(target, serializer);
        var sparse = new JObject();

        foreach (var propertyName in overrideSet)
            if (full.TryGetValue(propertyName, out var value))
                sparse[propertyName] = value;

        sparse["PrefabOverrides"] = JArray.FromObject(overrideSet.OrderBy(value => value));
        if (sparse.Count == 0) return null;
        return sparse;
    }

    private static void BuildHierarchy(KeyValuePair<string, JToken> dataPair, Obj parent) {

        if (dataPair.Value is not JObject data) return;

        var name = dataPair.Key;
        var obj = MakeObject(name, parent);

        // Load transform
        if (data["Transform"] is JObject transformData) {
            JsonConvert.PopulateObject(transformData.ToString(), obj.Transform);

            if (transformData["PrefabOverrides"] is JArray transformOverrides) {
                obj.Transform.PrefabOverrides.Clear();

                foreach (var value in transformOverrides.Values<string>().Where(value => !string.IsNullOrWhiteSpace(value))) {
                    var overrideName = value == nameof(Transform.Euler) ? nameof(Transform.Rot) : value!;
                    obj.Transform.PrefabOverrides.Add(overrideName);
                }
            }
        }

        // Load components
        var components = new Dictionary<string, Component>();

        if (data["Components"] is JObject jsonComponents) {

            foreach (var property in jsonComponents.Properties()) {

                if (Activator.CreateInstance(Type.GetType(property.Name) ?? throw new KeyNotFoundException($"{property.Name} cant be found"), obj) is not Component component) continue;

                JsonConvert.PopulateObject(data["Components"]![property.Name]!.ToString(), component);

                if (data["Components"]![property.Name]!["PrefabOverrides"] is JArray componentOverrides) {
                    component.PrefabOverrides.Clear();

                    foreach (var value in componentOverrides.Values<string>().Where(value => !string.IsNullOrWhiteSpace(value)))
                        component.PrefabOverrides.Add(value!);
                }

                components[property.Name] = component;
            }
        }

        obj.Components = components;

        obj.Prefab = data["Prefab"]?.Value<string>() ?? "";
        obj.PrefabPath = data["PrefabPath"]?.Value<string>() ?? "";

        if (data["PrefabOverrides"] is JArray prefabOverrides) {
            obj.PrefabOverrides.Clear();

            foreach (var value in prefabOverrides.Values<string>().Where(value => !string.IsNullOrWhiteSpace(value)))
                obj.PrefabOverrides.Add(value!);
        }

        if (data["Children"] is not JObject children) return;

        foreach (var property in children.Properties()) BuildHierarchy(new KeyValuePair<string, JToken>(property.Name, property.Value), obj);
    }

    public static Obj MakeObject(string name, Obj? parent) {

        var obj = new Obj(parent == null ? name : Generators.AvailableName(name, parent.Children.Keys), parent);
        obj.SetParent(parent);

        if (parent?.FindPrefabRoot() != null && Core.ActiveLevel?.IsPrefabDocument != true)
            PrefabUtility.MarkAddedChildSubtree(obj);

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
