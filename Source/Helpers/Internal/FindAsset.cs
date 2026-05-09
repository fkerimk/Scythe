[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
internal class FindAssetAttribute(string typeName) : Attribute {
    public string TypeName { get; } = typeName;
}