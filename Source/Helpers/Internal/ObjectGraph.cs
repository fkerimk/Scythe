using System.Reflection;
#if !SCYTHE_RUNTIME_BUILD
using FastMember;
using KellermanSoftware.CompareNetObjects;
using Force.DeepCloner;
#endif
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class ObjectGraph {
#if !SCYTHE_RUNTIME_BUILD
    private static readonly CompareLogic Comparer = new(new ComparisonConfig {
        MaxDifferences = 1,
        IgnoreCollectionOrder = false
    });
#endif

    public static bool AreEqual(object? left, object? right) {
        if (left == null && right == null) return true;
        if (left == null || right == null) return false;
        if (ReferenceEquals(left, right) || Equals(left, right)) return true;

        try {
#if !SCYTHE_RUNTIME_BUILD
            return Comparer.Compare(left, right).AreEqual;
#else
            return JToken.DeepEquals(JToken.FromObject(left), JToken.FromObject(right));
#endif
        } catch {
            try {
                return JToken.DeepEquals(JToken.FromObject(left), JToken.FromObject(right));
            } catch {
                return false;
            }
        }
    }

    public static T DeepClone<T>(T value) {
#if !SCYTHE_RUNTIME_BUILD
        return value.DeepClone();
#else
        if (value == null) return value!;
        var token = JToken.FromObject(value);
        return token.ToObject<T>()!;
#endif
    }

    public static void CopyJsonState(object source, object target) {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
#if !SCYTHE_RUNTIME_BUILD
        var sourceAccessor = TypeAccessor.Create(source.GetType(), true);
        var targetAccessor = TypeAccessor.Create(target.GetType(), true);
#endif

        foreach (var property in source.GetType().GetProperties(flags)) {
            if (!property.CanRead) continue;
            if (!Attribute.IsDefined(property, typeof(JsonPropertyAttribute))) continue;

            var targetProperty = target.GetType().GetProperty(property.Name, flags);
            if (targetProperty == null || !targetProperty.CanWrite) continue;

#if !SCYTHE_RUNTIME_BUILD
            targetAccessor[target, property.Name] = CloneMemberValue(sourceAccessor[source, property.Name]);
#else
            targetProperty.SetValue(target, CloneMemberValue(property.GetValue(source)));
#endif
        }

        foreach (var field in source.GetType().GetFields(flags)) {
            if (field.IsInitOnly) continue;
            if (!Attribute.IsDefined(field, typeof(JsonPropertyAttribute))) continue;

            var targetField = target.GetType().GetField(field.Name, flags);
            if (targetField == null || targetField.IsInitOnly) continue;

#if !SCYTHE_RUNTIME_BUILD
            targetAccessor[target, field.Name] = CloneMemberValue(sourceAccessor[source, field.Name]);
#else
            targetField.SetValue(target, CloneMemberValue(field.GetValue(source)));
#endif
        }
    }

    private static object? CloneMemberValue(object? value) {
        if (value == null) return null;
        if (value is string) return value;
        if (value.GetType().IsValueType) return value;
#if !SCYTHE_RUNTIME_BUILD
        return value.DeepClone();
#else
        var token = JToken.FromObject(value);
        return token.ToObject(value.GetType());
#endif
    }
}
