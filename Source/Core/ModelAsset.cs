using System.Numerics;
using Raylib_cs;
using static Raylib_cs.Raylib;
using Newtonsoft.Json;

internal class ModelAsset : Asset {

    public List<AssimpMesh> Meshes = [];
    public List<BoneInfo> Bones = [];
    public readonly Dictionary<string, List<BoneInfo>> BoneMap = new();
    public ModelNode RootNode = null!;
    public Matrix4x4 GlobalInverse;
    public List<AnimationClip> Animations = [];
    public Material[] Materials = null!;
    public string[] MaterialPaths = null!;
    public List<MaterialAsset?> CachedMaterialAssets = [];

    private uint _lastBakeVersion;

    [RecordHistory] public ModelSettings Settings = new();

    public class ModelSettings : ICloneable {

        public string GUID = "";
        public string AnimationGUID = "";
        public Dictionary<int, string> MeshMaterials = new();
        public float ImportScale = 1.0f;

        public object Clone() => new ModelSettings { GUID = GUID, AnimationGUID = AnimationGUID, MeshMaterials = new Dictionary<int, string>(MeshMaterials), ImportScale = ImportScale };
    }

    public void ApplySettings() {

        NormalizeReferences();

        for (var i = 0; i < Materials.Length; i++) {

            MaterialPaths[i] = Settings.MeshMaterials.GetValueOrDefault(i, "");
            CachedMaterialAssets[i] = AssetManager.Get<MaterialAsset>(MaterialPaths[i]);
            ApplyMaterialState(i, true);
        }
    }

    public override bool Load() {

        // Normalize path for Linux compatibility
        File = File.Replace('\\', '/');

        if (!System.IO.File.Exists(File)) return false;

        try {

            var data = AssimpLoader.Load(File);
            Meshes = data.Meshes;
            Bones = data.Bones;
            RootNode = data.Root;
            GlobalInverse = data.GlobalInverse;
            Animations = data.Animations;

            BoneMap.Clear();

            foreach (var b in Bones) {

                if (!BoneMap.TryGetValue(b.Name, out var list)) {

                    list = [];
                    BoneMap[b.Name] = list;
                }

                list.Add(b);
            }

            var jsonPath = File + ".json";
            if (System.IO.File.Exists(jsonPath)) Settings = JsonConvert.DeserializeObject<ModelSettings>(System.IO.File.ReadAllText(jsonPath)) ?? new ModelSettings();

            if (string.IsNullOrWhiteSpace(Settings.GUID)) Settings.GUID = System.Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(Settings.AnimationGUID)) Settings.AnimationGUID = System.Guid.NewGuid().ToString("N");
            GUID = Settings.GUID;

        } catch (Exception e) {

            TraceLog(TraceLogLevel.Error, $"Failed to load model {File}: {e}");

            return false;
        }

        IsLoaded = true;
        var matCount = Meshes.Count > 0 ? Meshes.Max(m => m.MaterialIndex) + 1 : 1;
        Materials = new Material[matCount];
        MaterialPaths = new string[matCount];
        CachedMaterialAssets = [];

        for (var i = 0; i < matCount; i++) {

            Materials[i] = LoadMaterialDefault();
            MaterialPaths[i] = Settings.MeshMaterials.GetValueOrDefault(i, "");
            CachedMaterialAssets.Add(null);
            ApplyMaterialState(i, true);
        }

        NormalizeReferences();

        Preview.UpdateThumbnail(this);

        return true;
    }

    public void SaveSettings() {

        var jsonPath = File + ".json";
        Settings.GUID = GUID;
        for (var i = 0; i < MaterialPaths.Length; i++) Settings.MeshMaterials[i] = MaterialPaths[i];
        System.IO.File.WriteAllText(jsonPath, JsonConvert.SerializeObject(Settings, Formatting.Indented));
    }

    public void ApplyMaterial(int index, string path) {

        if (index < 0 || index >= Materials.Length) return;

        MaterialPaths[index] = path;
        CachedMaterialAssets[index] = AssetManager.Get<MaterialAsset>(path);
        ApplyMaterialState(index, true);
        SaveSettings();
    }

    public void UpdateMaterialsIfDirty() {

        if (_lastBakeVersion == MaterialAsset.GlobalVersion) return;

        for (var i = 0; i < Materials.Length; i++) ApplyMaterialState(i);
        _lastBakeVersion = MaterialAsset.GlobalVersion;
    }

    public unsafe void ApplyMaterialState(int index, bool force = false) {

        if (index < 0 || index >= Materials.Length) return;

        ref var mat = ref Materials[index];
        var asset = index < CachedMaterialAssets.Count ? CachedMaterialAssets[index] : null;

        if (asset == null && !string.IsNullOrEmpty(MaterialPaths[index])) {

            asset = AssetManager.Get<MaterialAsset>(MaterialPaths[index]);
            if (index < CachedMaterialAssets.Count) CachedMaterialAssets[index] = asset;
        }

        var shaderAsset = AssetManager.Get<ShaderAsset>(asset?.Data.Shader ?? "pbr") ?? AssetManager.Get<ShaderAsset>("pbr");

        if (shaderAsset == null) return;

        mat.Shader = shaderAsset.Shader;

        // Texture Assignment (Baked into Material Struct)
        fixed (Material* p = &mat) {

            var tPath = asset?.Data.Textures.GetValueOrDefault("albedo_map", "");
            var tex = AssetManager.Get<TextureAsset>(tPath);
            SetMaterialTexture(p, MaterialMapIndex.Albedo, tex?.Texture ?? new Texture2D());

            tPath = asset?.Data.Textures.GetValueOrDefault("normal_map", "");
            tex = AssetManager.Get<TextureAsset>(tPath);
            SetMaterialTexture(p, MaterialMapIndex.Normal, tex?.Texture ?? new Texture2D());

            tPath = asset?.Data.Textures.GetValueOrDefault("metallic_map", "");
            tex = AssetManager.Get<TextureAsset>(tPath);
            SetMaterialTexture(p, MaterialMapIndex.Metalness, tex?.Texture ?? new Texture2D());

            tPath = asset?.Data.Textures.GetValueOrDefault("roughness_map", "");
            tex = AssetManager.Get<TextureAsset>(tPath);
            SetMaterialTexture(p, MaterialMapIndex.Roughness, tex?.Texture ?? new Texture2D());

            tPath = asset?.Data.Textures.GetValueOrDefault("occlusion_map", "");
            tex = AssetManager.Get<TextureAsset>(tPath);
            SetMaterialTexture(p, MaterialMapIndex.Occlusion, tex?.Texture ?? new Texture2D());

            tPath = asset?.Data.Textures.GetValueOrDefault("emissive_map", "");
            tex = AssetManager.Get<TextureAsset>(tPath);
            SetMaterialTexture(p, MaterialMapIndex.Emission, tex?.Texture ?? new Texture2D());
        }
    }

    public override unsafe void Unload() {

        foreach (var mesh in Meshes) UnloadMesh(mesh.RlMesh);

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (Materials != null) {

            for (var i = 0; i < Materials.Length; i++) {

                Materials[i].Shader = new Shader();

                if (Materials[i].Maps != null)
                    for (var j = 0; j < 12; j++)
                        Materials[i].Maps[j].Texture = new Texture2D();

                UnloadMaterial(Materials[i]);
            }
        }

        IsLoaded = false;
    }

    public bool NormalizeReferences() {

        var changed = false;

        if (!string.IsNullOrWhiteSpace(Settings.GUID) && !string.Equals(GUID, Settings.GUID, StringComparison.OrdinalIgnoreCase)) {

            GUID = Settings.GUID;
            changed = true;
        }

        for (var i = 0; i < MaterialPaths.Length; i++) {

            var normalized = AssetManager.NormalizeReference<MaterialAsset>(MaterialPaths[i]);
            if (normalized == MaterialPaths[i]) continue;

            MaterialPaths[i] = normalized;
            changed = true;
        }

        foreach (var key in Settings.MeshMaterials.Keys.ToList()) {

            var normalized = AssetManager.NormalizeReference<MaterialAsset>(Settings.MeshMaterials[key]);
            if (normalized == Settings.MeshMaterials[key]) continue;

            Settings.MeshMaterials[key] = normalized;
            changed = true;
        }

        return changed;
    }

    public override IEnumerable<string> GetWatchedFiles() {

        yield return File;
        yield return File + ".json";
    }
}
