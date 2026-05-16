#if !SCYTHE_RUNTIME_BUILD
using Assimp;
#endif

internal static class AssetFilePatterns {
    private static readonly HashSet<string> TextureExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".webp", ".avif", ".dds", ".stex" };
    private static readonly HashSet<string> ScriptExtensions = new(StringComparer.OrdinalIgnoreCase) { ".cs" };
    private static readonly HashSet<string> ShaderExtensions = new(StringComparer.OrdinalIgnoreCase) { ".vs", ".fs" };
    private static readonly HashSet<string> FontExtensions = new(StringComparer.OrdinalIgnoreCase) { ".ttf", ".otf" };
    private static readonly HashSet<string> ModelExtensions = CreateModelExtensions();

    public static bool IsTexture(string path) => TextureExtensions.Contains(Path.GetExtension(path));
    public static bool IsScript(string path) => ScriptExtensions.Contains(Path.GetExtension(path));
    public static bool IsShader(string path) => ShaderExtensions.Contains(Path.GetExtension(path));
    public static bool IsFont(string path) => FontExtensions.Contains(Path.GetExtension(path));
    public static bool IsModel(string path) => ModelExtensions.Contains(Path.GetExtension(path));

    public static bool IsImportable(string path) =>
        IsTexture(path) || IsScript(path) || IsShader(path) || IsModel(path);

    private static HashSet<string> CreateModelExtensions() {

        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".scymodel" };

#if !SCYTHE_RUNTIME_BUILD
        try {
            using var context = new AssimpContext();
            foreach (var description in context.GetImporterDescriptions()) {
                if (description?.FileExtensions == null) continue;

                foreach (var extension in description.FileExtensions) {
                    if (string.IsNullOrWhiteSpace(extension)) continue;

                    var normalized = extension.StartsWith('.') ? extension : "." + extension;
                    extensions.Add(normalized);
                }
            }
        } catch {
            extensions.UnionWith([".fbx", ".obj", ".gltf", ".glb", ".iqm"]);
        }
#else
        extensions.UnionWith([".fbx", ".obj", ".gltf", ".glb", ".iqm"]);
#endif

        return extensions;
    }
}
