using System.Numerics;
using Raylib_cs;
using Newtonsoft.Json;
using static Raylib_cs.Raylib;

internal class MaterialAsset : Asset {

    public static uint GlobalVersion { get; private set; } = 1;
    public uint Version { get; private set; } = 1;

    public Material Material;
    [RecordHistory] public MaterialData Data = new();

    public class MaterialData : ICloneable {

        public string GUID = "";
        public string Shader = "Pbr";
        public string ShaderPath = "";

        public Dictionary<string, string> Textures = new();
        public Dictionary<string, string> TexturePaths = new();

        public Dictionary<string, float> Floats = new() {
            ["metallic_value"] = 0.0f,
            ["roughness_value"] = 0.5f,
            ["aoValue"] = 1.0f,
            ["emissive_intensity"] = 1.0f,
            ["normalValue"] = 1.0f
        };

        public Dictionary<string, int> Ints = new() { ["is_directx_normal"] = 0 };

        public Dictionary<string, Color> Colors = new() { ["albedo_color"] = new Color(255, 255, 255, 255), ["emissive_color"] = new Color(0, 0, 0, 255) };

        public Dictionary<string, Vector2> Vectors = new() { ["tiling"] = Vector2.One, ["offset"] = Vector2.Zero };

        public object Clone() => ObjectGraph.DeepClone(this);
    }

    public static MaterialAsset Default {
        get {

            if (field != null) return field;

            var asset = AssetManager.Get<MaterialAsset>("Collection/Default.mat");

            if (asset != null) {

                field = asset;

                return field;
            }

            field = new MaterialAsset { File = "Default", Material = LoadMaterialDefault(), IsLoaded = true, Data = new MaterialData() };

            field.ApplyChanges();

            return field;
        }
    }

    public override bool Load() {

        if (!System.IO.File.Exists(File)) return false;

        try {

            var json = System.IO.File.ReadAllText(File);
            Data = JsonConvert.DeserializeObject<MaterialData>(json) ?? new MaterialData();
            var changed = false;
            if (string.IsNullOrWhiteSpace(Data.GUID)) {

                Data.GUID = System.Guid.NewGuid().ToString("N");
                changed = true;
            }

            GUID = Data.GUID;
            changed |= NormalizeReferences();
            if (changed) Save();

        } catch {

            return false;
        }

        Material = LoadMaterialDefault();
        IsLoaded = true;
        ThumbnailDirty = true;

        ApplyChanges(updateThumbnail: false);
        if (!AssetManager.IsInitializing) Preview.UpdateThumbnail(this);

        return true;
    }

    public unsafe void ApplyChanges(bool updateThumbnail = true, bool bumpVersion = true) {

        if (!IsLoaded) return;

        if (bumpVersion) {

            GlobalVersion++;
            Version++;
        }

        var shaderName = string.IsNullOrEmpty(Data.Shader) ? "Collection/pbr.vs" : Data.Shader;
        var shaderAsset = AssetManager.Get<ShaderAsset>(shaderName) ?? AssetManager.Get<ShaderAsset>("Collection/pbr.vs");
        if (shaderAsset != null) Material.Shader = shaderAsset.Shader;

        ApplyMap("albedo_map", MaterialMapIndex.Albedo);
        ApplyMap("normal_map", MaterialMapIndex.Normal);
        ApplyMap("metallic_map", MaterialMapIndex.Metalness);
        ApplyMap("roughness_map", MaterialMapIndex.Roughness);
        ApplyMap("occlusion_map", MaterialMapIndex.Occlusion);
        ApplyMap("emissive_map", MaterialMapIndex.Emission);

        if (updateThumbnail) ThumbnailDirty = true;
        if (updateThumbnail) Preview.UpdateThumbnail(this);

        return;

        void ApplyMap(string key, MaterialMapIndex index) {

            var path = Data.Textures.GetValueOrDefault(key, "");
            var tex = AssetManager.Get<TextureAsset>(path);

            fixed (Material* p = &Material) {

                SetMaterialTexture(p, index, tex?.Texture ?? new Texture2D());

                if (index == MaterialMapIndex.Albedo) p->Maps[(int)MaterialMapIndex.Albedo].Color = Data.Colors.GetValueOrDefault("albedo_color", Color.White);
            }
        }
    }

    public void ApplyUniforms(Shader shader) {

        var shaderName = string.IsNullOrEmpty(Data.Shader) ? "Collection/pbr.vs" : Data.Shader;
        var shaderAsset = AssetManager.Get<ShaderAsset>(shaderName);

        if (shaderAsset == null) return;

        SetFlag("use_tex_albedo", "albedo_map");
        SetFlag("use_tex_normal", "normal_map");
        SetFlag("use_tex_metallic", "metallic_map");
        SetFlag("use_tex_roughness", "roughness_map");
        SetFlag("use_tex_occlusion", "occlusion_map");
        SetFlag("use_tex_emissive", "emissive_map");

        foreach (var prop in shaderAsset.Properties) {

            var loc = shaderAsset.GetLoc(prop.Name);

            if (loc == -1) continue;

            // Skip usage flags as we handled them above
            if (prop.Name.StartsWith("use_tex_")) continue;

            switch (prop.Type) {

                case "float": {

                    var val = Data.Floats.GetValueOrDefault(prop.Name, Default.Data.Floats.GetValueOrDefault(prop.Name, 0f));
                    SetShaderValue(shader, loc, val, ShaderUniformDataType.Float);

                    break;
                }

                case "int": {

                    var val = Data.Ints.GetValueOrDefault(prop.Name, Default.Data.Ints.GetValueOrDefault(prop.Name, 0));
                    SetShaderValue(shader, loc, val, ShaderUniformDataType.Int);

                    break;
                }

                case "vec2": {

                    var val = Data.Vectors.GetValueOrDefault(prop.Name, Default.Data.Vectors.GetValueOrDefault(prop.Name, prop.Name == "tiling" ? Vector2.One : Vector2.Zero));
                    SetShaderValue(shader, loc, val, ShaderUniformDataType.Vec2);

                    break;
                }

                case "vec3": {

                    var val = Vector3.Zero;
                    if (Data.Colors.TryGetValue(prop.Name, out var col))
                        val = new Vector3(col.R / 255f, col.G / 255f, col.B / 255f);
                    else if (Default.Data.Colors.TryGetValue(prop.Name, out var defCol)) val = new Vector3(defCol.R / 255f, defCol.G / 255f, defCol.B / 255f);

                    SetShaderValue(shader, loc, val, ShaderUniformDataType.Vec3);

                    break;
                }

                case "vec4": {

                    var val = Data.Colors.TryGetValue(prop.Name, out var col) ? ColorNormalize(col) : Default.Data.Colors.TryGetValue(prop.Name, out var defCol) ? ColorNormalize(defCol) : Vector4.One;
                    SetShaderValue(shader, loc, val, ShaderUniformDataType.Vec4);

                    break;
                }
            }
        }

        return;

        // Set texture usage flags
        void SetFlag(string name, string texKey) {

            var loc = shaderAsset.GetLoc(name);
            if (loc != -1) SetShaderValue(shader, loc, Data.Textures.ContainsKey(texKey) && !string.IsNullOrEmpty(Data.Textures[texKey]) ? 1 : 0, ShaderUniformDataType.Int);
        }
    }

    public override unsafe void Unload() {

        // Reset shared shader/textures before unloading to prevent double-free/crash.
        Material.Shader = new Shader();
        if (Material.Maps != null)
            for (var i = 0; i < 12; i++)
                Material.Maps[i].Texture = new Texture2D();

        UnloadMaterial(Material);
        Material = new Material();

        if (Thumbnail.HasValue) {

            UnloadTexture(Thumbnail.Value);
            Thumbnail = null;
        }

        ThumbnailDirty = true;
        IsLoaded = false;
    }

    public void Save() {

        Data.ShaderPath = AssetManager.GetStoredPath(AssetManager.GetPath<ShaderAsset>(Data.Shader) ?? Data.ShaderPath);
        foreach (var key in Data.Textures.Keys.ToList())
            Data.TexturePaths[key] = AssetManager.GetStoredPath(AssetManager.GetPath<TextureAsset>(Data.Textures[key]) ?? Data.TexturePaths.GetValueOrDefault(key, ""));

        Data.GUID = GUID;
        var json = JsonConvert.SerializeObject(Data, Formatting.Indented);
        AssetManager.RegisterInternalWrite(File);
        System.IO.File.WriteAllText(File, json);
    }

    public bool NormalizeReferences() {

        var changed = false;
        var shaderGuid = Data.Shader;
        var shaderPath = Data.ShaderPath;
        if (AssetManager.ResolveReference<ShaderAsset>(ref shaderGuid, ref shaderPath) != null) {

            if (shaderGuid != Data.Shader) {

                Data.Shader = shaderGuid;
                changed = true;
            }

            if (shaderPath != Data.ShaderPath) {

                Data.ShaderPath = shaderPath;
                changed = true;
            }
        } else if (string.IsNullOrWhiteSpace(Data.ShaderPath) && !string.IsNullOrWhiteSpace(Data.Shader)) {

            Data.ShaderPath = Data.Shader;
            changed = true;
        }

        foreach (var key in Data.Textures.Keys.Union(Data.TexturePaths.Keys).ToList()) {

            var guid = Data.Textures.GetValueOrDefault(key, "");
            var path = Data.TexturePaths.GetValueOrDefault(key, "");
            if (AssetManager.ResolveReference<TextureAsset>(ref guid, ref path) != null) {

                if (guid != Data.Textures.GetValueOrDefault(key, "")) {

                    Data.Textures[key] = guid;
                    changed = true;
                }

                if (path != Data.TexturePaths.GetValueOrDefault(key, "")) {

                    Data.TexturePaths[key] = path;
                    changed = true;
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(guid)) {

                Data.TexturePaths[key] = guid;
                changed = true;
            }
        }

        if (!string.Equals(GUID, Data.GUID, StringComparison.OrdinalIgnoreCase)) {

            GUID = Data.GUID;
            changed = true;
        }

        return changed;
    }
}
