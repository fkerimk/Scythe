internal static class AssetPaths {
    public static bool HasExtension(string path, string extension) =>
        path.EndsWith(extension, StringComparison.OrdinalIgnoreCase);

    public static bool HasAnyExtension(string path, params string[] extensions) =>
        extensions.Any(extension => HasExtension(path, extension));

    public static bool IsLevel(string path) => HasExtension(path, ".lvl");
    public static bool IsMaterial(string path) => HasExtension(path, ".mat");
    public static bool IsPrefab(string path) => HasExtension(path, ".pre");
    public static bool IsJson(string path) => HasExtension(path, ".json");
    public static bool IsFragmentShader(string path) => HasExtension(path, ".fs");
    public static bool IsPublishMetadata(string path) =>
        HasAnyExtension(path, ".pdb", ".dll", ".json", ".deps.json", ".runtimeconfig.json");

    public static string GetDisplayName(string path, string extension) {
        var fileName = Path.GetFileName(path.Replace('\\', '/'));
        return HasExtension(fileName, extension)
            ? fileName[..^extension.Length]
            : Path.GetFileNameWithoutExtension(fileName);
    }
}
