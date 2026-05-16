using Assimp;
using ImageMagick;

internal static partial class AssimpLoader {

    internal sealed class EmbeddedImportData {
        public List<EmbeddedTextureData> Textures { get; } = [];
        public List<EmbeddedMaterialData> Materials { get; } = [];
        public bool HasEmbeddedAssets => Textures.Count > 0 || Materials.Count > 0;
    }

    internal sealed class EmbeddedTextureData {
        public string Key = "";
        public string Name = "";
        public string Extension = ".png";
        public byte[] Bytes = [];
    }

    internal sealed class EmbeddedMaterialData {
        public int Index;
        public string Name = "";
        public Dictionary<string, string> TextureBindings = new();
    }

    public static EmbeddedImportData ExtractEmbeddedImportData(string path) {

        var scene = ImportScene(path);
        if (scene == null || scene.SceneFlags.HasFlag(SceneFlags.Incomplete) || scene.RootNode == null)
            throw new Exception($"Assimp error: {path}");

        var result = new EmbeddedImportData();
        var texturesByKey = new Dictionary<string, EmbeddedTextureData>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < scene.TextureCount; i++) {
            var texture = scene.Textures[i];
            var exported = ExportEmbeddedTexture(texture, i);
            if (exported == null) continue;

            result.Textures.Add(exported);
            RegisterTextureKey(texturesByKey, exported.Key, exported);

            if (!string.IsNullOrWhiteSpace(texture.Filename)) {
                RegisterTextureKey(texturesByKey, texture.Filename, exported);
                RegisterTextureKey(texturesByKey, Path.GetFileName(texture.Filename), exported);
                RegisterTextureKey(texturesByKey, Path.GetFileNameWithoutExtension(texture.Filename), exported);
            }
        }

        for (var i = 0; i < scene.MaterialCount; i++) {
            var material = scene.Materials[i];
            var exported = new EmbeddedMaterialData {
                Index = i,
                Name = material.Name ?? ""
            };

            TryBindTexture(material, TextureType.BaseColor, "albedo_map", texturesByKey, exported.TextureBindings);
            TryBindTexture(material, TextureType.Diffuse, "albedo_map", texturesByKey, exported.TextureBindings);
            TryBindTexture(material, TextureType.NormalCamera, "normal_map", texturesByKey, exported.TextureBindings);
            TryBindTexture(material, TextureType.Normals, "normal_map", texturesByKey, exported.TextureBindings);
            TryBindTexture(material, TextureType.Height, "normal_map", texturesByKey, exported.TextureBindings);
            TryBindTexture(material, TextureType.Metalness, "metallic_map", texturesByKey, exported.TextureBindings);
            TryBindTexture(material, TextureType.Roughness, "roughness_map", texturesByKey, exported.TextureBindings);
            TryBindTexture(material, TextureType.AmbientOcclusion, "occlusion_map", texturesByKey, exported.TextureBindings);
            TryBindTexture(material, TextureType.Lightmap, "occlusion_map", texturesByKey, exported.TextureBindings);
            TryBindTexture(material, TextureType.EmissionColor, "emissive_map", texturesByKey, exported.TextureBindings);
            TryBindTexture(material, TextureType.Emissive, "emissive_map", texturesByKey, exported.TextureBindings);

            result.Materials.Add(exported);
        }

        return result;
    }

    private static EmbeddedTextureData? ExportEmbeddedTexture(EmbeddedTexture texture, int index) {

        byte[] bytes;
        string extension;

        if (texture.IsCompressed) {
            if (!texture.HasCompressedData || texture.CompressedDataSize <= 0 || texture.CompressedData == null)
                return null;

            bytes = texture.CompressedData;
            extension = NormalizeTextureExtension(texture.CompressedFormatHint, texture.Filename);

        } else {
            if (!texture.HasNonCompressedData || texture.Width <= 0 || texture.Height <= 0 || texture.NonCompressedData == null)
                return null;

            bytes = EncodeRawTexture(texture.NonCompressedData, texture.Width, texture.Height);
            extension = ".png";
        }

        return new EmbeddedTextureData {
            Key = $"*{index}",
            Name = BuildTextureName(texture.Filename, index),
            Extension = extension,
            Bytes = bytes
        };
    }

    private static byte[] EncodeRawTexture(Texel[] texels, int width, int height) {

        var pixels = new byte[texels.Length * 4];
        for (var i = 0; i < texels.Length; i++) {
            var offset = i * 4;
            pixels[offset] = texels[i].B;
            pixels[offset + 1] = texels[i].G;
            pixels[offset + 2] = texels[i].R;
            pixels[offset + 3] = texels[i].A;
        }

        var settings = new PixelReadSettings((uint)width, (uint)height, StorageType.Char, PixelMapping.BGRA);
        using var image = new MagickImage(pixels, settings);
        return image.ToByteArray(MagickFormat.Png);
    }

    private static void TryBindTexture(Material material, TextureType textureType, string targetKey, Dictionary<string, EmbeddedTextureData> texturesByKey, Dictionary<string, string> bindings) {

        if (bindings.ContainsKey(targetKey)) return;

        var count = material.GetMaterialTextureCount(textureType);
        for (var i = 0; i < count; i++) {
            if (!material.GetMaterialTexture(textureType, i, out var slot)) continue;
            if (string.IsNullOrWhiteSpace(slot.FilePath)) continue;

            if (!texturesByKey.TryGetValue(slot.FilePath, out var texture))
                continue;

            bindings[targetKey] = texture.Key;
            return;
        }
    }

    private static void RegisterTextureKey(Dictionary<string, EmbeddedTextureData> textureLookup, string? key, EmbeddedTextureData texture) {

        if (string.IsNullOrWhiteSpace(key)) return;
        textureLookup[key] = texture;
    }

    private static string BuildTextureName(string? filename, int index) {

        var name = Path.GetFileNameWithoutExtension(filename);
        return string.IsNullOrWhiteSpace(name) ? $"Texture_{index}" : name;
    }

    private static string NormalizeTextureExtension(string? formatHint, string? filename) {

        var extension = Path.GetExtension(filename);
        if (!string.IsNullOrWhiteSpace(extension))
            return extension.StartsWith('.') ? extension : "." + extension;

        if (!string.IsNullOrWhiteSpace(formatHint))
            return "." + formatHint.Trim().TrimStart('.').ToLowerInvariant();

        return ".png";
    }
}
