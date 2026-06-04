using System.Numerics;
using Raylib_cs;
using static Raylib_cs.Raylib;

internal class ShaderAsset : Asset {

    public Shader Shader;

    private readonly Dictionary<string, int> _locations = new();

    public override unsafe bool Load() {

        var name = Path.GetFileNameWithoutExtension(File);
        string? vsPath = null;
        string? fsPath = null;

        // Try to find matching vs/fs in the same directory
        var directory = Path.GetDirectoryName(File)!;

        // Potential extensions
        if (System.IO.File.Exists(Path.Combine(directory, name + ".vs"))) vsPath = Path.Combine(directory, name + ".vs");

        if (System.IO.File.Exists(Path.Combine(directory, name + ".fs"))) fsPath = Path.Combine(directory, name + ".fs");

        if (vsPath == null && fsPath != null && !string.Equals(name, "transform", StringComparison.OrdinalIgnoreCase)) {
            var sharedPostProcessPath = Path.Combine(directory, "postprocess.vs");
            if (System.IO.File.Exists(sharedPostProcessPath)) vsPath = sharedPostProcessPath;
        }

        if (vsPath == null && fsPath == null) return false;

        var jsonPath = File + ".json";
        if (System.IO.File.Exists(jsonPath)) {

            var meta = JsonFile.ReadOrDefault(jsonPath, new AssetSidecarData());
            var changed = false;
            if (string.IsNullOrWhiteSpace(meta.GUID)) {

                meta.GUID = System.Guid.NewGuid().ToString("N");
                changed = true;
            }

            GUID = meta.GUID;
            if (changed) JsonFile.WriteIndented(jsonPath, meta);

        } else {

            GUID = System.Guid.NewGuid().ToString("N");
            JsonFile.WriteIndented(jsonPath, new AssetSidecarData { GUID = GUID });
        }

        try {

            Shader = LoadShader(vsPath, fsPath);
            if (Shader.Id == 0) {
                TraceLog(TraceLogLevel.Error, $"SHADER: Failed to compile/load shader '{File}' (vs: {vsPath ?? "<default>"}, fs: {fsPath ?? "<none>"})");
                return false;
            }

            Properties.Clear();

            // Map standard locations
            var locAlbedo = GetFirstLocation("albedo_map", "texture0");
            if (locAlbedo != -1) Shader.Locs[(int)ShaderLocationIndex.MapAlbedo] = locAlbedo;

            var locNormal = GetFirstLocation("normal_map");
            if (locNormal != -1) Shader.Locs[(int)ShaderLocationIndex.MapNormal] = locNormal;

            var locMetallic = GetFirstLocation("metallic_map");
            if (locMetallic != -1) Shader.Locs[(int)ShaderLocationIndex.MapMetalness] = locMetallic;

            var locRoughness = GetFirstLocation("roughness_map");
            if (locRoughness != -1) Shader.Locs[(int)ShaderLocationIndex.MapRoughness] = locRoughness;

            var locOcclusion = GetFirstLocation("occlusion_map");
            if (locOcclusion != -1) Shader.Locs[(int)ShaderLocationIndex.MapOcclusion] = locOcclusion;

            var locEmission = GetFirstLocation("emissive_map");
            if (locEmission != -1) Shader.Locs[(int)ShaderLocationIndex.MapEmission] = locEmission;

            // Map standard attributes
            Shader.Locs[(int)ShaderLocationIndex.VertexPosition] = GetFirstLocation("vertex_pos", "vertexPosition");
            Shader.Locs[(int)ShaderLocationIndex.VertexTexcoord01] = GetFirstLocation("vertex_tex_pos", "vertexTexCoord");
            Shader.Locs[(int)ShaderLocationIndex.VertexNormal] = GetFirstLocation("vertex_normal", "vertexNormal");
            Shader.Locs[(int)ShaderLocationIndex.VertexTangent] = GetFirstLocation("vertex_tangent", "vertexTangent");
            Shader.Locs[(int)ShaderLocationIndex.VertexColor] = GetFirstLocation("vertex_color", "vertexColor");

            var locView = GetFirstLocation("view_pos", "viewPos", "cameraPos");
            if (locView != -1) Shader.Locs[(int)ShaderLocationIndex.VectorView] = locView;

            // Global defaults for tiling/offset
            var locTiling = GetShaderLocation(Shader, "tiling");
            if (locTiling != -1) SetShaderValue(Shader, locTiling, new Vector2(1.0f, 1.0f), ShaderUniformDataType.Vec2);
            var locOffset = GetShaderLocation(Shader, "offset");
            if (locOffset != -1) SetShaderValue(Shader, locOffset, new Vector2(0.0f, 0.0f), ShaderUniformDataType.Vec2);

            // Cubemap for Skybox
            var locEnv = GetFirstLocation("environmentMap");
            if (locEnv != -1) SetShaderValue(Shader, locEnv, (int)MaterialMapIndex.Cubemap, ShaderUniformDataType.Int);

            ParseUniforms(vsPath);
            ParseUniforms(fsPath);

        } catch {
            return false;
        }

        IsLoaded = true;

        return true;
    }

    public override void Unload() {

        if (IsLoaded) UnloadShader(Shader);

        IsLoaded = false;
        _locations.Clear();
    }

    public class ShaderProperty {

        public string Name = "";
        public string Type = "";
    }

    public List<ShaderProperty> Properties = [];

    private void ParseUniforms(string? path) {

        if (path == null || !System.IO.File.Exists(path)) return;

        var lines = System.IO.File.ReadAllLines(path);
        var regex = new System.Text.RegularExpressions.Regex(@"uniform\s+(float|int|bool|vec2|vec3|vec4|sampler2D|samplerCube|mat4)\s+(\w+)(?:\s*=\s*[^;]+)?\s*;");

        foreach (var line in lines) {

            var match = regex.Match(line);

            if (!match.Success) continue;

            var type = match.Groups[1].Value;
            var name = match.Groups[2].Value;

            // Skip standard uniforms
            if (name.StartsWith("lights[") || name.StartsWith("use_tex_") || name is "mvp" or "matModel" or "matNormal" or "matProjection" or "matView" or "matProjectionInverse" or "matViewProjInv" or "matPrevViewProj" or "matPrevModel" or "hasHistory" or "view_pos" or "viewPos" or "cameraPos" or "lightPos" or "light_count" or "lightVP" or "shadowMap" or "shadow_light_index" or "shadow_strength" or "shadow_map_resolution" or "receive_shadows" or "shadow_bias" or "tiling" or "offset" or "alpha_cutoff" or "texture0" or "colDiffuse" or "difColor" or "textureSize" or "renderSize" or "renderWidth" or "renderHeight" or "resolution" or "time" or "depthTexture" or "historyTexture" or "velocityTexture" or "jitter" or "ambient_color" or "ambient_intensity") continue;

            if (Properties.All(p => p.Name != name)) Properties.Add(new ShaderProperty { Name = name, Type = type });
        }
    }

    public int GetLoc(string name) {

        if (_locations.TryGetValue(name, out var loc)) return loc;

        loc = GetShaderLocation(Shader, name);
        _locations[name] = loc;

        return loc;
    }

    private int GetFirstLocation(params string[] names) {

        foreach (var name in names) {

            var loc = GetShaderLocation(Shader, name);
            if (loc != -1) return loc;
        }

        return -1;
    }

    public override IEnumerable<string> GetWatchedFiles() {

        yield return File;
        yield return File + ".json";

        var name = Path.GetFileNameWithoutExtension(File);
        var directory = Path.GetDirectoryName(File)!;
        var vsPath = Path.Combine(directory, name + ".vs");
        var fsPath = Path.Combine(directory, name + ".fs");

        if (!string.Equals(vsPath, File, StringComparison.OrdinalIgnoreCase)) yield return vsPath;
        if (!string.Equals(fsPath, File, StringComparison.OrdinalIgnoreCase)) yield return fsPath;
    }
}
