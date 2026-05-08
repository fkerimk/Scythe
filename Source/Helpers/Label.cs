[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
internal class LabelAttribute(string value) : Attribute {

    public string Value { get; } = value;
}
