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

        public class AnimationClipSettings {
            public string Name = "";
            public int Track;
            public int StartFrame;
            public int EndFrame;
            public bool Loop = true;
        }

        public string GUID = "";
        public string AnimationGUID = "";
        public string AnimationPath = "";
        public Dictionary<int, string> MeshMaterials = new();
        public Dictionary<int, string> MeshMaterialPaths = new();
        public float ImportScale = 1.0f;
        public List<AnimationClipSettings> AnimationClips = [];

        public object Clone() => ObjectGraph.DeepClone(this);
    }

    public void ApplySettings() {

        NormalizeReferences();

        for (var i = 0; i < Materials.Length; i++) {

            MaterialPaths[i] = Settings.MeshMaterials.GetValueOrDefault(i, "");
            CachedMaterialAssets[i] = ResolveMaterialAsset(i, MaterialPaths[i]);
            ApplyMaterialState(i, true);
        }
    }

    public override bool Load() {

        // Normalize path for Linux compatibility
        File = File.Replace('\\', '/');
        ImportedFile = "";
        var settingsChanged = false;

        if (!System.IO.File.Exists(File)) return false;

        try {

            var jsonPath = File + ".json";
            if (System.IO.File.Exists(jsonPath)) Settings = JsonFile.ReadOrDefault(jsonPath, new ModelSettings());

            if (string.IsNullOrWhiteSpace(Settings.GUID)) {

                Settings.GUID = System.Guid.NewGuid().ToString("N");
                settingsChanged = true;
            }

            if (string.IsNullOrWhiteSpace(Settings.AnimationGUID)) {

                Settings.AnimationGUID = System.Guid.NewGuid().ToString("N");
                settingsChanged = true;
            }

            GUID = Settings.GUID;
            ImportedFile = AssetManager.GetImportedModelFile(File, GUID);
            if (!TryLoadImportedOrRebuild()) return false;

            BoneMap.Clear();

            foreach (var b in Bones) {

                if (!BoneMap.TryGetValue(b.Name, out var list)) {

                    list = [];
                    BoneMap[b.Name] = list;
                }

                list.Add(b);
            }
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

        settingsChanged |= NormalizeReferences();
        ThumbnailDirty = true;

        if (settingsChanged) SaveSettings();

        if (!AssetManager.IsInitializing) Preview.UpdateThumbnail(this);

        return true;
    }

    private bool TryLoadImportedOrRebuild() {

        if (!string.Equals(Path.GetExtension(ResolvedFile), ".scymodel", StringComparison.OrdinalIgnoreCase)) {
#if !SCYTHE_RUNTIME_BUILD
            LoadSourceModel(File);
            return true;
#else
            return false;
#endif
        }

        if (TryLoadCompiledModel(ResolvedFile)) return true;

        AssetManager.DeleteImportedCache(this);
        ImportedFile = AssetManager.GetImportedModelFile(File, GUID);

        if (string.Equals(Path.GetExtension(ResolvedFile), ".scymodel", StringComparison.OrdinalIgnoreCase) && TryLoadCompiledModel(ResolvedFile))
            return true;

#if !SCYTHE_RUNTIME_BUILD
        LoadSourceModel(File);
        return true;
#else
        return false;
#endif
    }

    private bool TryLoadCompiledModel(string cacheFile) {

        if (!CompiledAssetCache.LoadModel(cacheFile, out var compiledMeshes, out var compiledBones, out var compiledRoot, out var compiledGlobalInverse, out var compiledAnimations))
            return false;

        Meshes = compiledMeshes;
        Bones = compiledBones;
        RootNode = compiledRoot;
        GlobalInverse = compiledGlobalInverse;
        Animations = BuildAnimationClips(compiledAnimations, Settings);
        return true;
    }

#if !SCYTHE_RUNTIME_BUILD
    private void LoadSourceModel(string path) {

        var data = AssimpLoader.Load(path);
        Meshes = data.Meshes;
        Bones = data.Bones;
        RootNode = data.Root;
        GlobalInverse = data.GlobalInverse;
        Animations = BuildAnimationClips(data.Animations, Settings);
    }
#endif

    public void SaveSettings() {

        var jsonPath = File + ".json";
        Settings.GUID = GUID;
        Settings.AnimationPath = AssetManager.GetStoredPath(File);
        for (var i = 0; i < MaterialPaths.Length; i++) Settings.MeshMaterials[i] = MaterialPaths[i];
        for (var i = 0; i < MaterialPaths.Length; i++) Settings.MeshMaterialPaths[i] = AssetManager.GetStoredPath(AssetManager.GetPath<MaterialAsset>(MaterialPaths[i]) ?? Settings.MeshMaterialPaths.GetValueOrDefault(i, ""));
        AssetManager.RegisterInternalWrite(jsonPath);
        JsonFile.WriteIndented(jsonPath, Settings);
    }

    public void ApplyMaterial(int index, string path) {

        if (index < 0 || index >= Materials.Length) return;

        MaterialPaths[index] = path;
        CachedMaterialAssets[index] = ResolveMaterialAsset(index, path);
        ApplyMaterialState(index, true);
        ThumbnailDirty = true;
        Preview.UpdateThumbnail(this);
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

            asset = ResolveMaterialAsset(index, MaterialPaths[index]);
            if (index < CachedMaterialAssets.Count) CachedMaterialAssets[index] = asset;
        }

        var shaderAsset = AssetManager.GetOrImport<ShaderAsset>(asset?.Data.Shader ?? "Collection/pbr.vs") ?? AssetManager.GetOrImport<ShaderAsset>("Collection/pbr.vs");

        if (shaderAsset == null) return;

        mat.Shader = shaderAsset.Shader;

        // Texture Assignment (Baked into Material Struct)
        fixed (Material* p = &mat) {

            var tex = ResolveTextureAsset(asset, "albedo_map");
            SetMaterialTexture(p, MaterialMapIndex.Albedo, tex?.Texture ?? new Texture2D());

            tex = ResolveTextureAsset(asset, "normal_map");
            SetMaterialTexture(p, MaterialMapIndex.Normal, tex?.Texture ?? new Texture2D());

            tex = ResolveTextureAsset(asset, "metallic_map");
            SetMaterialTexture(p, MaterialMapIndex.Metalness, tex?.Texture ?? new Texture2D());

            tex = ResolveTextureAsset(asset, "roughness_map");
            SetMaterialTexture(p, MaterialMapIndex.Roughness, tex?.Texture ?? new Texture2D());

            tex = ResolveTextureAsset(asset, "occlusion_map");
            SetMaterialTexture(p, MaterialMapIndex.Occlusion, tex?.Texture ?? new Texture2D());

            tex = ResolveTextureAsset(asset, "emissive_map");
            SetMaterialTexture(p, MaterialMapIndex.Emission, tex?.Texture ?? new Texture2D());
        }
    }

    private MaterialAsset? ResolveMaterialAsset(int index, string value) {

        var asset = AssetManager.GetOrImport<MaterialAsset>(value);
        asset ??= AssetManager.GetOrImport<MaterialAsset>(Settings.MeshMaterialPaths.GetValueOrDefault(index, ""));
        return asset;
    }

    private static TextureAsset? ResolveTextureAsset(MaterialAsset? asset, string key) {

        if (asset == null) return null;

        var texture = AssetManager.GetOrImport<TextureAsset>(asset.Data.Textures.GetValueOrDefault(key, ""));
        texture ??= AssetManager.GetOrImport<TextureAsset>(asset.Data.TexturePaths.GetValueOrDefault(key, ""));
        return texture;
    }

    public override unsafe void Unload() {

        foreach (var mesh in Meshes) UnloadMesh(mesh.RlMesh);
        Meshes.Clear();
        Bones.Clear();
        BoneMap.Clear();
        Animations.Clear();

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

        Materials = [];
        MaterialPaths = [];
        CachedMaterialAssets.Clear();
        RootNode = null!;

        if (Thumbnail.HasValue) {

            UnloadTexture(Thumbnail.Value);
            Thumbnail = null;
        }

        ThumbnailDirty = true;
        IsLoaded = false;
    }

    public bool NormalizeReferences() {

        var changed = false;

        if (!string.IsNullOrWhiteSpace(Settings.GUID) && !string.Equals(GUID, Settings.GUID, StringComparison.OrdinalIgnoreCase)) {

            GUID = Settings.GUID;
            changed = true;
        }

        for (var i = 0; i < MaterialPaths.Length; i++) {

            var guid = MaterialPaths[i];
            var path = Settings.MeshMaterialPaths.GetValueOrDefault(i, "");
            if (AssetManager.ResolveReference<MaterialAsset>(ref guid, ref path) != null) {

                if (guid != MaterialPaths[i]) {

                    MaterialPaths[i] = guid;
                    changed = true;
                }

                if (path != Settings.MeshMaterialPaths.GetValueOrDefault(i, "")) {

                    Settings.MeshMaterialPaths[i] = path;
                    changed = true;
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(guid)) {

                Settings.MeshMaterialPaths[i] = guid;
                changed = true;
            }
        }

        foreach (var key in Settings.MeshMaterials.Keys.Union(Settings.MeshMaterialPaths.Keys).ToList()) {

            var guid = Settings.MeshMaterials.GetValueOrDefault(key, "");
            var path = Settings.MeshMaterialPaths.GetValueOrDefault(key, "");
            if (AssetManager.ResolveReference<MaterialAsset>(ref guid, ref path) != null) {

                if (guid != Settings.MeshMaterials.GetValueOrDefault(key, "")) {

                    Settings.MeshMaterials[key] = guid;
                    changed = true;
                }

                if (path != Settings.MeshMaterialPaths.GetValueOrDefault(key, "")) {

                    Settings.MeshMaterialPaths[key] = path;
                    changed = true;
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(guid)) {

                Settings.MeshMaterialPaths[key] = guid;
                changed = true;
            }
        }

        return changed;
    }

    public override IEnumerable<string> GetWatchedFiles() {

        yield return File;
        yield return File + ".json";
    }

    public static List<AnimationClip> BuildAnimationClips(List<AnimationClip> sourceAnimations, ModelSettings settings) {

        EnsureDefaultAnimationClips(sourceAnimations, settings);

        var clips = new List<AnimationClip>();
        foreach (var definition in settings.AnimationClips) {
            if (definition.Track < 0 || definition.Track >= sourceAnimations.Count) continue;

            var source = sourceAnimations[definition.Track];
            var clipName = string.IsNullOrWhiteSpace(definition.Name)
                ? GetDefaultClipName(source, definition.Track)
                : definition.Name;
            var startFrame = Math.Clamp((double)definition.StartFrame, 0d, source.Duration);
            var endFrame = Math.Clamp((double)definition.EndFrame, startFrame, source.Duration);
            clips.Add(AssimpLoader.CreateClipSegment(source, clipName, definition.Track, startFrame, endFrame, definition.Loop));
        }

        return clips;
    }

    public static void EnsureDefaultAnimationClips(List<AnimationClip> sourceAnimations, ModelSettings settings) {

        settings.AnimationClips ??= [];

        if (sourceAnimations.Count == 0) {
            if (settings.AnimationClips.Count > 0)
                settings.AnimationClips.Clear();
            return;
        }

        if (settings.AnimationClips.Count > 0) return;

        for (var i = 0; i < sourceAnimations.Count; i++) {
            var source = sourceAnimations[i];
            settings.AnimationClips.Add(new ModelSettings.AnimationClipSettings {
                Name = GetDefaultClipName(source, i),
                Track = i,
                StartFrame = 0,
                EndFrame = (int)Math.Ceiling(source.Duration),
                Loop = true
            });
        }
    }

    public static string GetDefaultClipName(AnimationClip source, int trackIndex) =>
        string.IsNullOrWhiteSpace(source.Name) ? $"Track {trackIndex}" : source.Name;
}
