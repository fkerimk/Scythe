internal static class AssetFilePatterns {
    private static readonly HashSet<string> TextureExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".webp", ".avif", ".dds", ".stex" };
    private static readonly HashSet<string> ScriptExtensions = new(StringComparer.OrdinalIgnoreCase) { ".cs" };
    private static readonly HashSet<string> ShaderExtensions = new(StringComparer.OrdinalIgnoreCase) { ".vs", ".fs" };
    private static readonly HashSet<string> FontExtensions = new(StringComparer.OrdinalIgnoreCase) { ".ttf", ".otf" };
    private static readonly HashSet<string> ModelExtensions = new(StringComparer.OrdinalIgnoreCase) { ".fbx", ".obj", ".gltf", ".glb", ".iqm", ".scymodel" };

    public static bool IsTexture(string path) => TextureExtensions.Contains(Path.GetExtension(path));
    public static bool IsScript(string path) => ScriptExtensions.Contains(Path.GetExtension(path));
    public static bool IsShader(string path) => ShaderExtensions.Contains(Path.GetExtension(path));
    public static bool IsFont(string path) => FontExtensions.Contains(Path.GetExtension(path));
    public static bool IsModel(string path) => ModelExtensions.Contains(Path.GetExtension(path));

    public static bool IsImportable(string path) =>
        IsTexture(path) || IsScript(path) || IsShader(path) || IsModel(path);
}
