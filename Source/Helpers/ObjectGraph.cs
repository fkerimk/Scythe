using System.Reflection;
using FastMember;
using Force.DeepCloner;
using KellermanSoftware.CompareNetObjects;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class ObjectGraph {
    private static readonly CompareLogic Comparer = new(new ComparisonConfig {
        MaxDifferences = 1,
        IgnoreCollectionOrder = false
    });

    public static bool AreEqual(object? left, object? right) {
        if (left == null && right == null) return true;
        if (left == null || right == null) return false;
        if (ReferenceEquals(left, right) || Equals(left, right)) return true;

        try {
            return Comparer.Compare(left, right).AreEqual;
        } catch {
            try {
                return JToken.DeepEquals(JToken.FromObject(left), JToken.FromObject(right));
            } catch {
                return false;
            }
        }
    }

    public static T DeepClone<T>(T value) =>
        value.DeepClone();

    public static void CopyJsonState(object source, object target) {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var sourceAccessor = TypeAccessor.Create(source.GetType(), true);
        var targetAccessor = TypeAccessor.Create(target.GetType(), true);

        foreach (var property in source.GetType().GetProperties(flags)) {
            if (!property.CanRead) continue;
            if (!Attribute.IsDefined(property, typeof(JsonPropertyAttribute))) continue;

            var targetProperty = target.GetType().GetProperty(property.Name, flags);
            if (targetProperty == null || !targetProperty.CanWrite) continue;

            targetAccessor[target, property.Name] = CloneMemberValue(sourceAccessor[source, property.Name]);
        }

        foreach (var field in source.GetType().GetFields(flags)) {
            if (field.IsInitOnly) continue;
            if (!Attribute.IsDefined(field, typeof(JsonPropertyAttribute))) continue;

            var targetField = target.GetType().GetField(field.Name, flags);
            if (targetField == null || targetField.IsInitOnly) continue;

            targetAccessor[target, field.Name] = CloneMemberValue(sourceAccessor[source, field.Name]);
        }
    }

    private static object? CloneMemberValue(object? value) {
        if (value == null) return null;
        if (value is string) return value;
        if (value.GetType().IsValueType) return value;
        return value.DeepClone();
    }
}
