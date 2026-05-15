using System.Reflection;
using System.Numerics;
using Newtonsoft.Json;
using Raylib_cs;

[AttributeUsage(AttributeTargets.Field)]
internal class ExposeAttribute : Attribute {
}

[AttributeUsage(AttributeTargets.Field)]
internal class ConfigAttribute : Attribute {
}

internal enum ScriptFieldStorageKind {
    Expose,
    Config
}

internal static class ScriptFieldUtility {

    private const BindingFlags FieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static FieldInfo[] GetFields(Type scriptType, ScriptFieldStorageKind kind) =>
        scriptType.GetFields(FieldFlags)
                  .Where(field => !field.IsStatic && !field.IsInitOnly && !field.IsLiteral)
                  .Where(field => Attribute.IsDefined(field, kind == ScriptFieldStorageKind.Expose ? typeof(ExposeAttribute) : typeof(ConfigAttribute)))
                  .Where(field => IsSupportedFieldType(field.FieldType))
                  .OrderBy(field => field.MetadataToken)
                  .ToArray();

    public static bool IsSupportedFieldType(Type type) {

        if (type.IsArray && type.GetArrayRank() == 1 && type.GetElementType() is { } elementType)
            return !IsSceneReferenceType(elementType) && IsSupportedScalarFieldType(elementType);

        return IsSupportedScalarFieldType(type);
    }

    public static bool IsSupportedScalarFieldType(Type type) =>
        type == typeof(string)
        || type == typeof(float)
        || type == typeof(int)
        || type == typeof(bool)
        || type == typeof(Vector2)
        || type == typeof(Vector3)
        || type == typeof(Bool3)
        || type == typeof(Color)
        || IsSceneReferenceType(type)
        || type.IsEnum;

    public static bool IsSceneReferenceType(Type type) =>
        type == typeof(Obj)
        || typeof(Component).IsAssignableFrom(type)
        || typeof(ScytheScript).IsAssignableFrom(type);

    public static string GetLabel(FieldInfo field) =>
        field.GetCustomAttribute<LabelAttribute>()?.Value ?? field.Name;

    public static object? GetCodeDefaultValue(Type? scriptType, FieldInfo field) {

        if (scriptType == null) return GetTypeDefault(field.FieldType);

        try {

            if (Activator.CreateInstance(scriptType) is not object instance) return GetTypeDefault(field.FieldType);
            return field.GetValue(instance);

        } catch {

            return GetTypeDefault(field.FieldType);
        }
    }

    public static object? GetTypeDefault(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;

    public static object? DeserializeStoredValue(string raw, Type type, Obj? contextObj = null) {

        if (IsSceneReferenceType(type)) {
            try {
                var reference = JsonConvert.DeserializeObject<SceneReferenceValue>(raw);
                if (reference == null) return null;

                reference.ResolvedValue = ResolveSceneReference(reference, type, contextObj);
                return reference;
            } catch {
                return null;
            }
        }

        try {
            return JsonConvert.DeserializeObject(raw, type);
        } catch {
            return GetTypeDefault(type);
        }
    }

    public static string SerializeStoredValue(object? value) {

        if (value == null) return JsonConvert.SerializeObject(null, Formatting.None);

        if (value is SceneReferenceValue sceneReference)
            return JsonConvert.SerializeObject(sceneReference, Formatting.None);

        var valueType = value.GetType();
        if (IsSceneReferenceType(valueType)) {
            var sceneValue = SceneReferenceValue.FromTarget(value);
            return JsonConvert.SerializeObject(sceneValue, Formatting.None);
        }

        return JsonConvert.SerializeObject(value, Formatting.None);
    }

    public static object? ResolveStoredValueForAssignment(object? value, Type type, Obj? contextObj = null) {

        if (!IsSceneReferenceType(type)) return value;
        if (value == null) return null;

        if (value is SceneReferenceValue sceneReference)
            return ResolveSceneReference(sceneReference, type, contextObj);

        if (type.IsInstanceOfType(value)) return value;
        return null;
    }

    public static bool ValueEquals(object? left, object? right) {

        if (left == null && right == null) return true;
        if (left == null || right == null) return false;
        if (left is SceneReferenceValue leftReference && right is SceneReferenceValue rightReference)
            return leftReference.EqualsReference(rightReference);
        if (Equals(left, right)) return true;

        try {
            return ObjectGraph.AreEqual(left, right);
        } catch {
            return false;
        }
    }

    private static object? ResolveSceneReference(SceneReferenceValue reference, Type targetType, Obj? contextObj) {

        if (contextObj == null) return null;

        var obj = ResolveTargetObject(reference, contextObj);
        if (obj == null) return null;

        if (targetType == typeof(Obj)) return obj;
        if (typeof(ScytheScript).IsAssignableFrom(targetType))
            return ResolveTargetScript(reference, obj, targetType);
        if (typeof(Component).IsAssignableFrom(targetType))
            return ResolveTargetComponent(reference, obj, targetType);

        return null;
    }

    private static Obj? ResolveTargetObject(SceneReferenceValue reference, Obj contextObj) {

        var current = reference.IsPrefabLocal
            ? contextObj.FindPrefabRoot() ?? contextObj.GetRoot()
            : contextObj.GetRoot();

        foreach (var segment in reference.Path) {
            if (!current.ChildEntries.TryGetValue(segment.Name, segment.Occurrence, out var next))
                return null;

            current = next;
        }

        return current;
    }

    private static Component? ResolveTargetComponent(SceneReferenceValue reference, Obj obj, Type targetType) {

        if (string.IsNullOrWhiteSpace(reference.ComponentType)) return null;
        if (!obj.ComponentEntries.TryGetValue(reference.ComponentType, reference.ComponentOccurrence, out var component)) return null;

        return targetType.IsInstanceOfType(component) ? component : null;
    }

    private static ScytheScript? ResolveTargetScript(SceneReferenceValue reference, Obj obj, Type targetType) {

        if (!string.Equals(reference.ComponentType, nameof(Script), StringComparison.Ordinal)) return null;
        if (!obj.ComponentEntries.TryGetValue(nameof(Script), reference.ComponentOccurrence, out var component)) return null;
        if (component is not Script script) return null;

        return script.Instance != null && targetType.IsInstanceOfType(script.Instance)
            ? script.Instance
            : null;
    }
}

internal sealed class SceneReferenceValue {
    public List<SceneReferencePathSegment> Path { get; set; } = [];
    public string? ComponentType { get; set; }
    public int ComponentOccurrence { get; set; }
    public string? ScriptType { get; set; }
    public bool IsPrefabLocal { get; set; }
    [JsonIgnore] public object? ResolvedValue { get; set; }

    public static SceneReferenceValue FromTarget(object target, Obj? relativeRoot = null) => target switch {
        Obj obj => BuildForObject(obj, relativeRoot),
        ScytheScript script => BuildForScript(script, relativeRoot),
        Script script => BuildForScriptComponent(script, relativeRoot),
        Component component => BuildForComponent(component, relativeRoot),
        _ => throw new InvalidOperationException($"Unsupported scene reference target type: {target.GetType().FullName}")
    };

    public bool EqualsReference(SceneReferenceValue other) =>
        IsPrefabLocal == other.IsPrefabLocal
        && ComponentOccurrence == other.ComponentOccurrence
        && string.Equals(ComponentType, other.ComponentType, StringComparison.Ordinal)
        && string.Equals(ScriptType, other.ScriptType, StringComparison.Ordinal)
        && Path.Count == other.Path.Count
        && Path.Zip(other.Path, (left, right) =>
            string.Equals(left.Name, right.Name, StringComparison.Ordinal) && left.Occurrence == right.Occurrence).All(equal => equal);

    private static SceneReferenceValue BuildForObject(Obj obj, Obj? relativeRoot) =>
        new() { Path = BuildPath(obj, relativeRoot), IsPrefabLocal = relativeRoot != null };

    private static SceneReferenceValue BuildForComponent(Component component, Obj? relativeRoot) =>
        new() {
            Path = BuildPath(component.Obj, relativeRoot),
            ComponentType = component.GetType().Name,
            ComponentOccurrence = component.Obj.ComponentEntries.GetOccurrenceIndex(component),
            ScriptType = component is Script script ? script.GetAsset()?.ScriptType?.FullName : null,
            IsPrefabLocal = relativeRoot != null
        };

    private static SceneReferenceValue BuildForScriptComponent(Script script, Obj? relativeRoot) =>
        new() {
            Path = BuildPath(script.Obj, relativeRoot),
            ComponentType = nameof(Script),
            ComponentOccurrence = script.Obj.ComponentEntries.GetOccurrenceIndex(script),
            ScriptType = script.GetAsset()?.ScriptType?.FullName,
            IsPrefabLocal = relativeRoot != null
        };

    private static SceneReferenceValue BuildForScript(ScytheScript script, Obj? relativeRoot) {

        var component = script.Obj.ComponentEntries.Values
            .OfType<Script>()
            .FirstOrDefault(candidate => ReferenceEquals(candidate.Instance, script));

        if (component == null)
            throw new InvalidOperationException($"Script instance '{script.GetType().FullName}' is not attached to an Obj.");

        return new SceneReferenceValue {
            Path = BuildPath(component.Obj, relativeRoot),
            ComponentType = nameof(Script),
            ComponentOccurrence = component.Obj.ComponentEntries.GetOccurrenceIndex(component),
            ScriptType = script.GetType().FullName,
            IsPrefabLocal = relativeRoot != null
        };
    }

    private static List<SceneReferencePathSegment> BuildPath(Obj obj, Obj? relativeRoot = null) {

        var path = new List<SceneReferencePathSegment>();
        var current = obj;

        while (current.Parent != null && !ReferenceEquals(current, relativeRoot)) {
            path.Add(new SceneReferencePathSegment {
                Name = current.Name,
                Occurrence = current.Parent.ChildEntries.GetOccurrenceIndex(current)
            });
            current = current.Parent;
        }

        path.Reverse();
        return path;
    }
}

internal sealed class SceneReferencePathSegment {
    public string Name { get; set; } = "";
    public int Occurrence { get; set; }
}
