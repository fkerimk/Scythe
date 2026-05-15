using System.Reflection;

[AttributeUsage(AttributeTargets.Class, Inherited = true)]
internal sealed class ScriptOrderAttribute(int order) : Attribute {

    public int Order { get; } = order;
}

internal static class ScriptOrderUtility {

    public static int GetExecutionOrder(Type? type) =>
        type?.GetCustomAttribute<ScriptOrderAttribute>(inherit: true)?.Order ?? 0;
}
