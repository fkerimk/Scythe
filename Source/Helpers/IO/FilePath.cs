[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
internal class FilePathAttribute(string category, string extension) : Attribute {

    public string Category  { get; } = category;
    public string Extension { get; } = extension;
}
