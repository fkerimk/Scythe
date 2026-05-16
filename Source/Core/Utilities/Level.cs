using System.Numerics;
using System.Text.Json.Nodes;
#if !SCYTHE_RUNTIME_BUILD
using Json.Path;
#endif
#if !SCYTHE_RUNTIME_BUILD
using JsonDiffPatchDotNet;
#endif
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Raylib_cs;

[JsonObject(MemberSerialization.OptIn)]
internal class Level {
#if !SCYTHE_RUNTIME_BUILD
    private static readonly JsonDiffPatch JsonDiffer = new();
#endif
    private const string ComponentTypeToken = "$ComponentType";

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
            var collectionsPath = Path.Combine(modPath, "Collections");

            // Standardize separators
            val = val.Replace('\\', '/');
            if (!string.IsNullOrEmpty(modPath)) modPath = modPath.Replace('\\', '/');
            if (!string.IsNullOrEmpty(collectionsPath)) collectionsPath = collectionsPath.Replace('\\', '/');

            //  Try Mod Relative Path
            if (!string.IsNullOrEmpty(collectionsPath) && val.StartsWith(collectionsPath, StringComparison.OrdinalIgnoreCase)) {
                val = Path.GetRelativePath(collectionsPath, val).Replace('\\', '/');
            } else if (!string.IsNullOrEmpty(modPath) && val.StartsWith(modPath, StringComparison.OrdinalIgnoreCase)) {
                val = Path.GetRelativePath(modPath, val).Replace('\\', '/');
            } else {
                var builtInIndex = val.IndexOf("/Collection/", StringComparison.OrdinalIgnoreCase);

                if (builtInIndex != -1) {
                    val = "Built In/" + val[(builtInIndex + "/Collection/".Length)..];
                } else {
                    var legacyIndex = val.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase);

                    if (legacyIndex != -1)
                        val = "Built In/" + Path.GetFileName(val);
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
    [JsonProperty, RecordHistory] public string Skybox { get; set; } = "";
    [JsonProperty, RecordHistory] public string SkyboxPath { get; set; } = "";
    [JsonProperty, RecordHistory] public Color SkyboxTint { get; set; } = Color.White;
    [JsonProperty, RecordHistory] public Color BackgroundColor { get; set; } = new Color(25, 25, 25, 255);
    [JsonProperty, RecordHistory] public Color AmbientColor { get; set; } = Color.White;
    [JsonProperty, RecordHistory] public bool SkyboxAmbientEnabled { get; set; }
    [JsonProperty, RecordHistory] public float SkyboxAmbientIntensity { get; set; } = 1.0f;

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
        IsPrefabDocument = AssetPaths.IsPrefab(path);
        Root = new Obj("Root", null);

        LoadInternal();
    }

    public Level(string name, string path, bool load = true, bool applyEditorCamera = true) {

        Name = name;
        GUID = System.Guid.NewGuid().ToString("N");
        JsonPath = path;
        IsPrefabDocument = AssetPaths.IsPrefab(path);
        Root = new Obj("Root", null);

        if (load) LoadInternal(applyEditorCamera: applyEditorCamera);
    }

    public Level(string name, string path, string jsonBody, bool applyEditorCamera = true) {

        Name = name;
        GUID = System.Guid.NewGuid().ToString("N");
        JsonPath = path;
        IsPrefabDocument = AssetPaths.IsPrefab(path);
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
                SkyboxTint = ReadJsonValue(rawData, "SkyboxTint", SkyboxTint);
                SkyboxAmbientEnabled = rawData["SkyboxAmbientEnabled"]?.Value<bool>() ?? false;
                SkyboxAmbientIntensity = Math.Max(rawData["SkyboxAmbientIntensity"]?.Value<float>() ?? 1.0f, 0.0f);
                BackgroundColor = ReadJsonValue(rawData, "BackgroundColor", BackgroundColor);
                AmbientColor = ReadJsonValue(rawData, "AmbientColor", AmbientColor);

                foreach (var (childName, childToken) in EnumerateChildTokens(rawData["Root"]?["Children"]))
                    BuildHierarchy(childToken, Root, childName);

                PrefabUtility.ApplyPrefabInstancesPreservingRootPlacement(this);

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

    private static JObject BuildRootSnapshot(Obj root, JsonSerializer serializer) =>
        new() {
            ["Name"] = root.Name,
            ["Children"] = BuildChildrenSnapshot(root.ChildEntries.Values, serializer)
        };

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
        token["Name"] = obj.Name;

        if (isPrefabRoot) {
            token["Prefab"] = obj.Prefab;
            token["PrefabPath"] = obj.PrefabPath;
            if (obj.PrefabOverrides.Count > 0) token["PrefabOverrides"] = JArray.FromObject(obj.PrefabOverrides.OrderBy(value => value));
            var rootTransform = CreateFullComponentSnapshot(obj.Transform, serializer);
            var rootTransformOverrides = obj.Transform.PrefabOverrides
                .Where(value => value == nameof(Transform.Scale))
                .OrderBy(value => value)
                .ToList();
            if (rootTransformOverrides.Count > 0)
                rootTransform[nameof(Component.PrefabOverrides)] = JArray.FromObject(rootTransformOverrides);
            token["Transform"] = rootTransform;
        } else if (obj.PrefabOverrides.Count > 0)
            token["PrefabOverrides"] = JArray.FromObject(obj.PrefabOverrides.OrderBy(value => value));

        if (!isPrefabRoot) {
            var transform = BuildSparseComponentSnapshot(obj.Transform, sourceObj.Transform, obj.Transform.PrefabOverrides, serializer);
            if (transform != null) token["Transform"] = transform;
        }

        var components = BuildSparseComponentsSnapshot(obj, sourceObj, serializer);
        if (components.Count > 0) token["Components"] = components;

        var children = BuildChildrenSnapshot(obj.ChildEntries.Values.Select(child => BuildObjectSnapshot(child, serializer)).OfType<JObject>());
        if (children.Count > 0) token["Children"] = children;

        return token.Count == 0 ? null : token;
    }

    private static JObject CreateFullObjectSnapshot(Obj obj, JsonSerializer serializer) =>
        new JObject {
            ["Name"] = obj.Name,
            ["Prefab"] = string.IsNullOrWhiteSpace(obj.Prefab) ? null : obj.Prefab,
            ["PrefabPath"] = string.IsNullOrWhiteSpace(obj.PrefabPath) ? null : obj.PrefabPath,
            [nameof(Obj.PrefabOverrides)] = obj.PrefabOverrides.Count > 0 ? JArray.FromObject(obj.PrefabOverrides.OrderBy(value => value)) : null,
            ["Transform"] = CreateFullComponentSnapshot(obj.Transform, serializer),
            ["Components"] = BuildFullComponentsSnapshot(obj.ComponentEntries.Values, serializer),
            ["Children"] = BuildChildrenSnapshot(obj.ChildEntries.Values.Select(child => CreateFullObjectSnapshot(child, serializer)))
        }.WithoutNullProperties();

    private static JObject CreateFullComponentSnapshot(object component, JsonSerializer serializer) {

        var token = JObject.FromObject(component, serializer);
        token.Remove(nameof(Component.PrefabOverrides));
        return token;
    }

    private static JObject? BuildSparseComponentSnapshot(object target, object source, IEnumerable<string> overrides, JsonSerializer serializer) {

        var overrideSet = overrides.Where(value => !string.IsNullOrWhiteSpace(value)).ToHashSet(StringComparer.Ordinal);
        if (overrideSet.Remove(nameof(Transform.Euler)))
            overrideSet.Add(nameof(Transform.Rot));
        if (overrideSet.Count == 0) return null;

        var sourceToken = JObject.FromObject(source, serializer);
        var full = JObject.FromObject(target, serializer);
#if !SCYTHE_RUNTIME_BUILD
        var diff = JsonDiffer.Diff(sourceToken, full);
        if (diff == null) return null;

        var diffNode = JsonNode.Parse(diff.ToString(Formatting.None));
#else
        var diffNode = (JsonNode?)null;
#endif
        var sparse = new JObject();
        var changedOverrides = new List<string>();

        foreach (var propertyName in overrideSet)
            if (HasDiffAtProperty(diffNode, propertyName) && full.TryGetValue(propertyName, out var value)) {
                sparse[propertyName] = value;
                changedOverrides.Add(propertyName);
            }

        if (changedOverrides.Count == 0) return null;
        sparse["PrefabOverrides"] = JArray.FromObject(changedOverrides.OrderBy(value => value));
        if (sparse.Count == 0) return null;
        return sparse;
    }

    private static bool HasDiffAtProperty(JsonNode? diffNode, string propertyName) {
#if !SCYTHE_RUNTIME_BUILD
        if (diffNode == null) return false;

        var jsonPath = Json.Path.JsonPath.Parse($"$['{propertyName.Replace("'", "\\'")}']");
        return jsonPath.Evaluate(diffNode).Matches.Count > 0;
#else
        return false;
#endif
    }

    private static void BuildHierarchy(JObject data, Obj parent, string? legacyName = null) {

        var name = data["Name"]?.Value<string>() ?? legacyName;
        if (string.IsNullOrWhiteSpace(name)) return;
        var obj = MakeObject(name, parent);

        // Load transform
        if (data["Transform"] is JObject transformData) {
            JsonConvert.PopulateObject(transformData.ToString(), obj.Transform);
            ApplyOverrides(transformData["PrefabOverrides"], obj.Transform.PrefabOverrides, value => value == nameof(Transform.Euler) ? nameof(Transform.Rot) : value);
        }

        // Load components
        var components = new ComponentCollection();

        foreach (var (typeName, componentToken) in EnumerateComponentTokens(data["Components"])) {

            if (Activator.CreateInstance(Type.GetType(typeName) ?? throw new KeyNotFoundException($"{typeName} cant be found"), obj) is not Component component) continue;

            var componentData = (JObject)componentToken.DeepClone();
            componentData.Remove(ComponentTypeToken);

            if (componentData["Type"] is JValue { Type: JTokenType.String } typeMetadata && string.Equals(typeMetadata.Value<string>(), typeName, StringComparison.Ordinal))
                componentData.Remove("Type");

            JsonConvert.PopulateObject(componentData.ToString(), component);

            ApplyOverrides(componentToken["PrefabOverrides"], component.PrefabOverrides);

            components.Add(component);
        }

        obj.ComponentEntries = components;

        obj.Prefab = data["Prefab"]?.Value<string>() ?? "";
        obj.PrefabPath = data["PrefabPath"]?.Value<string>() ?? "";

        ApplyOverrides(data["PrefabOverrides"], obj.PrefabOverrides);

        foreach (var (childName, childToken) in EnumerateChildTokens(data["Children"]))
            BuildHierarchy(childToken, obj, childName);
    }

    public static Obj MakeObject(string name, Obj? parent) {

        var obj = new Obj(name, parent);

        if (parent != null)
            parent.ChildEntries.Add(obj);

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

    private static JArray BuildChildrenSnapshot(IEnumerable<Obj> children, JsonSerializer serializer) =>
        BuildChildrenSnapshot(children.Select(child => BuildObjectSnapshot(child, serializer)).OfType<JObject>());

    private static JArray BuildChildrenSnapshot(IEnumerable<JObject> children) {

        var array = new JArray();

        foreach (var child in children)
            array.Add(child);

        return array;
    }

    private static JArray BuildFullComponentsSnapshot(IEnumerable<Component> components, JsonSerializer serializer) {

        var array = new JArray();

        foreach (var component in components) {
            var token = CreateFullComponentSnapshot(component, serializer);
            token[ComponentTypeToken] = component.GetType().Name;
            array.Add(token);
        }

        return array;
    }

    private static JArray BuildSparseComponentsSnapshot(Obj obj, Obj sourceObj, JsonSerializer serializer) {

        var array = new JArray();
        var sourceComponentsByType = sourceObj.ComponentEntries.Values
            .GroupBy(component => component.GetType().Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var sourceIndices = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var component in obj.ComponentEntries.Values) {
            var typeName = component.GetType().Name;
            var index = sourceIndices.GetValueOrDefault(typeName, 0);
            sourceIndices[typeName] = index + 1;

            if (!sourceComponentsByType.TryGetValue(typeName, out var sourceGroup) || index >= sourceGroup.Count) {
                var fullToken = CreateFullComponentSnapshot(component, serializer);
                if (component.PrefabOverrides.Count > 0)
                    fullToken[nameof(Component.PrefabOverrides)] = JArray.FromObject(component.PrefabOverrides.OrderBy(value => value));
                fullToken[ComponentTypeToken] = typeName;
                array.Add(fullToken);
                continue;
            }

            var componentToken = BuildSparseComponentSnapshot(component, sourceGroup[index], component.PrefabOverrides, serializer);
            if (componentToken == null) continue;
            componentToken[ComponentTypeToken] = typeName;
            array.Add(componentToken);
        }

        return array;
    }

    private static IEnumerable<(string Name, JObject Token)> EnumerateChildTokens(JToken? childrenToken) {

        switch (childrenToken) {
            case JArray childrenArray:
                foreach (var child in childrenArray.OfType<JObject>()) {
                    var name = child["Name"]?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(name))
                        yield return (name!, child);
                }

                break;

            case JObject childrenObject:
                foreach (var property in childrenObject.Properties())
                    if (property.Value is JObject childObject)
                        yield return (property.Name, childObject);

                break;
        }
    }

    private static IEnumerable<(string TypeName, JObject Token)> EnumerateComponentTokens(JToken? componentsToken) {

        switch (componentsToken) {
            case JArray componentsArray:
                foreach (var component in componentsArray.OfType<JObject>()) {
                    var typeName = component[ComponentTypeToken]?.Value<string>() ?? component["Type"]?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(typeName))
                        yield return (typeName!, component);
                }

                break;

            case JObject componentsObject:
                foreach (var property in componentsObject.Properties())
                    if (property.Value is JObject componentObject)
                        yield return (property.Name, componentObject);

                break;
        }
    }

    private static T ReadJsonValue<T>(JObject source, string propertyName, T fallback) {

        if (source[propertyName] is not JObject propertyJson)
            return fallback;

        var parsed = propertyJson.ToObject<T?>();
        return parsed is null ? fallback : parsed;
    }

    private static void ApplyOverrides(JToken? overridesToken, ISet<string> destination, Func<string, string>? normalize = null) {

        if (overridesToken is not JArray overrides)
            return;

        destination.Clear();

        foreach (var value in overrides.Values<string>().Where(value => !string.IsNullOrWhiteSpace(value)))
            destination.Add(normalize?.Invoke(value!) ?? value!);
    }
}

internal static class JObjectExtensions {
    public static JObject WithoutNullProperties(this JObject token) {

        foreach (var property in token.Properties().Where(property => property.Value.Type == JTokenType.Null).ToList())
            property.Remove();

        return token;
    }
}
