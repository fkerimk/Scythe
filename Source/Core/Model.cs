using Raylib_cs;
using Newtonsoft.Json;
using System.ComponentModel;
using static Raylib_cs.Raylib;

internal class Model(Obj obj) : Component(obj) {

    public override string LabelIcon => Icons.FaCube;
    public override Color LabelColor => Colors.GuiTypeModel;

    [Label("Asset"), JsonProperty("GUID"), RecordHistory, FindAsset("ModelAsset")]
    public string GUID { get; set; } = "";

    [JsonProperty("Path")]
    public string Path { get; set; } = "";

    [Label("Color"), JsonProperty, RecordHistory]
    public Color Color { get; set; } = Color.White;

    [Label("Transparent"), JsonProperty, RecordHistory]
    public bool IsTransparent { get; set; }

    [Label("Alpha Cutoff"), JsonProperty, RecordHistory]
    public float AlphaCutoff { get; set; } = 0.5f;

    [Label("Cast Shadows"), JsonProperty, RecordHistory, DefaultValue(true)]
    public bool CastShadows { get; set; } = true;

    [Label("Receive Shadows"), JsonProperty, RecordHistory, DefaultValue(true)]
    public bool ReceiveShadows { get; set; } = true;

    public List<AssimpMesh> Meshes = [];
    public List<BoneInfo> Bones = [];
    public Dictionary<string, List<BoneInfo>> BoneMap = new();
    public ModelAsset AssetRef = null!;

    public override bool Load() {

        var oldGuid = GUID;
        var oldPath = Path;
        var guid = GUID;
        var path = Path;
        var loaded = AssetManager.ResolveReference<ModelAsset>(ref guid, ref path);
        GUID = guid;
        Path = path;

        if ((GUID != oldGuid || Path != oldPath) && Core.ActiveLevel != null) Core.ActiveLevel.IsDirty = true;

        if (loaded is not { IsLoaded: true }) return false;

        // Ensure lists are clear before adding
        Meshes.Clear();
        Bones.Clear();
        BoneMap.Clear();

        AssetRef = loaded;
        foreach (var m in AssetRef.Meshes) Meshes.Add(m.Clone());

        // Copy bones and build the multi-bone map
        foreach (var b in AssetRef.Bones) {

            var newBone = new BoneInfo { Name = b.Name, Index = b.Index, Offset = b.Offset };
            Bones.Add(newBone);

            if (!BoneMap.TryGetValue(newBone.Name, out var list)) {

                list = [];
                BoneMap[newBone.Name] = list;
            }

            list.Add(newBone);
        }

        return true;
    }

    public override void Logic() { }

    public override void Render3D() {

        if (!IsTransparent) Draw();
    }

    public void DrawTransparent() {

        BeginBlendMode(BlendMode.Alpha);
        Draw();
        EndBlendMode();
    }

    public void DrawShadow() {

        if (CastShadows) Draw();
    }

    public void Draw(float? overrideAlphaCutoff = null) {

        if (AssetRef is not { IsLoaded: true }) return;

        // Global material update check (only if anything changed)
        AssetRef.UpdateMaterialsIfDirty();

        MaterialAsset? lastMatAsset = null;
        uint lastShaderId = 0;
        uint lastMatVersion = 0;

        foreach (var mesh in Meshes) {

            var material = mesh.MaterialIndex >= 0 && mesh.MaterialIndex < AssetRef.Materials.Length ? AssetRef.Materials[mesh.MaterialIndex] : MaterialAsset.Default.Material;

            var matAsset = mesh.MaterialIndex >= 0 && mesh.MaterialIndex < AssetRef.CachedMaterialAssets.Count ? AssetRef.CachedMaterialAssets[mesh.MaterialIndex] ?? MaterialAsset.Default : MaterialAsset.Default;

            // 2. Resolve Material Asset parameters (only for shared shader values)
            var shader = material.Shader;
            var locs = UniformCache.Get(shader);

            if (matAsset != lastMatAsset || shader.Id != lastShaderId || matAsset.Version != lastMatVersion) {

                matAsset.ApplyUniforms(shader);
                lastMatAsset = matAsset;
                lastMatVersion = matAsset.Version;
            }

            // Batch apply uniforms (Using cached locations) - ONLY if shader changed this draw call
            if (shader.Id != lastShaderId) {

                if (locs.AlbedoColor != -1) SetShaderValue(shader, locs.AlbedoColor, ColorNormalize(Color), ShaderUniformDataType.Vec4);
                if (locs.ReceiveShadows != -1) SetShaderValue(shader, locs.ReceiveShadows, ReceiveShadows ? 1 : 0, ShaderUniformDataType.Int);
                if (locs.AlphaCutoff != -1) SetShaderValue(shader, locs.AlphaCutoff, overrideAlphaCutoff ?? AlphaCutoff, ShaderUniformDataType.Float);

                // Global Ambient (Live Update)
                if (locs.AmbientIntensity != -1) SetShaderValue(shader, locs.AmbientIntensity, Core.RenderSettings.AmbientIntensity, ShaderUniformDataType.Float);
                if (locs.AmbientColor != -1) SetShaderValue(shader, locs.AmbientColor, Core.RenderSettings.AmbientColor.ToVector4(), ShaderUniformDataType.Vec3);
            }

            lastShaderId = shader.Id;

            // Draw
            var matModel = Obj.VisualWorldMatrix;

            if (Math.Abs(AssetRef.Settings.ImportScale - 1.0f) > 0.001f) {

                var s = AssetRef.Settings.ImportScale;

                // Scale only the basis vectors (rotation/scale) and NOT the translation (M41, M42, M43)
                matModel.M11 *= s;
                matModel.M12 *= s;
                matModel.M13 *= s;
                matModel.M21 *= s;
                matModel.M22 *= s;
                matModel.M23 *= s;
                matModel.M31 *= s;
                matModel.M32 *= s;
                matModel.M33 *= s;
            }

            DrawMesh(mesh.RlMesh, material, matModel);
        }
    }

    private static class UniformCache {

        private static readonly Dictionary<uint, ShaderLocations> Cache = new();

        public class ShaderLocations {

            public int AlbedoColor;
            public int ReceiveShadows;
            public int AlphaCutoff;
            public int AmbientIntensity;
            public int AmbientColor;
        }

        public static ShaderLocations Get(Shader shader) {

            if (Cache.TryGetValue(shader.Id, out var locs)) return locs;

            locs = new ShaderLocations {
                AlbedoColor = GetShaderLocation(shader, "albedo_color"),
                ReceiveShadows = GetShaderLocation(shader, "receive_shadows"),
                AlphaCutoff = GetShaderLocation(shader, "alpha_cutoff"),
                AmbientIntensity = GetShaderLocation(shader, "ambient_intensity"),
                AmbientColor = GetShaderLocation(shader, "ambient_color")
            };

            Cache[shader.Id] = locs;

            return locs;
        }
    }

    public override void Unload() {

        foreach (var m in Meshes) UnloadMesh(m.RlMesh);

        Meshes.Clear();
        Bones.Clear();
        BoneMap.Clear();
    }
}
