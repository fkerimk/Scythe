[AttributeUsage(AttributeTargets.Property)]
internal class HeaderAttribute(string title) : Attribute {
    public string Title { get; } = title;
}