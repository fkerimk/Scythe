using DotNet.Globbing;

internal static class AssetFilePatterns {

    private static readonly Glob TextureGlob = Glob.Parse("*.{png,jpg,jpeg,tga,bmp,webp,avif,dds,stex}");
    private static readonly Glob ScriptGlob = Glob.Parse("*.cs");
    private static readonly Glob ShaderGlob = Glob.Parse("*.{vs,fs}");
    private static readonly Glob FontGlob = Glob.Parse("*.{ttf,otf}");
    private static readonly Glob ModelGlob = Glob.Parse("*.{fbx,obj,gltf,glb,iqm,scymodel}");

    public static bool IsTexture(string path) => TextureGlob.IsMatch(Path.GetFileName(path));
    public static bool IsScript(string path) => ScriptGlob.IsMatch(Path.GetFileName(path));
    public static bool IsShader(string path) => ShaderGlob.IsMatch(Path.GetFileName(path));
    public static bool IsFont(string path) => FontGlob.IsMatch(Path.GetFileName(path));
    public static bool IsModel(string path) => ModelGlob.IsMatch(Path.GetFileName(path));
}
