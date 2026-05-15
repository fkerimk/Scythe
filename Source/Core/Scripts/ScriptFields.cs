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
            return IsSupportedScalarFieldType(elementType);

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
        || type.IsEnum;

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

    public static object? DeserializeStoredValue(string raw, Type type) {

        try {
            return JsonConvert.DeserializeObject(raw, type);
        } catch {
            return GetTypeDefault(type);
        }
    }

    public static string SerializeStoredValue(object? value) =>
        JsonConvert.SerializeObject(value, Formatting.None);

    public static bool ValueEquals(object? left, object? right) {

        if (left == null && right == null) return true;
        if (left == null || right == null) return false;
        if (Equals(left, right)) return true;

        try {
            return ObjectGraph.AreEqual(left, right);
        } catch {
            return false;
        }
    }
}
