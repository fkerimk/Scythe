using System.IO.Compression;
using System.Numerics;
#if !SCYTHE_RUNTIME_BUILD
using ImageMagick;
#endif
using Raylib_cs;
using static Raylib_cs.Raylib;

internal static partial class CompiledAssetCache {

    private const int TextureVersion = 6;
    private const int ModelVersion = 3;
    private const string TextureMagic = "STEX";
    private const string ModelMagic = "SMOD";

    public readonly record struct TextureCacheInfo(int Width, int Height, PixelFormat Format, string Compression, int SourceWidth, int SourceHeight, int MaxSize, string ResizeFilter, int Quality, string RequestedFormat, string PayloadFormat);

    private readonly record struct TextureCacheHeader(
        int SourceWidth,
        int SourceHeight,
        int Width,
        int Height,
        PixelFormat Format,
        int MaxSize,
        string ResizeFilter,
        string Compression,
        int Quality,
        string RequestedFormat,
        string PayloadFormat
    );

    public static unsafe string EnsureTextureCache(string sourceFile, string outputFile, AssetSidecarData.TextureImportSettings settings) {

        if (IsTextureCacheCurrent(sourceFile, outputFile, settings))
            return outputFile;

        Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);

        var image = LoadImportedTextureImage(sourceFile, outputFile, settings);
        if (image.Data == null) return sourceFile;

        try {

            var sourceWidth = image.Width;
            var sourceHeight = image.Height;
            ImageFormat(&image, PixelFormat.UncompressedR8G8B8A8);
            var pixelBytes = ExtractPixelBytes(image);
            var compressedBytes = CompressTextureBytes(pixelBytes, settings);

            using var file = File.Create(outputFile);
            using var writer = new BinaryWriter(file);

            writer.Write(TextureMagic);
            writer.Write(TextureVersion);
            var header = new TextureCacheHeader(
                sourceWidth,
                sourceHeight,
                image.Width,
                image.Height,
                image.Format,
                settings.MaxSize,
                settings.ResizeFilter ?? "Bilinear",
                settings.Compression ?? "Normal",
                Math.Clamp(settings.Quality, 1, 100),
                NormalizeRequestedFormat(settings.Format),
                "RGBA32+Brotli"
            );
            WriteTextureHeader(writer, header);
            writer.Write(pixelBytes.Length);
            writer.Write(compressedBytes.Length);
            writer.Write(compressedBytes);

        } finally {

            UnloadImage(image);
        }

        return outputFile;
    }

    public static unsafe bool LoadTexture(string cacheFile, out Texture2D texture) {

        texture = new Texture2D();

        try {

            using var file = File.OpenRead(cacheFile);
            using var reader = new BinaryReader(file);

            if (reader.ReadString() != TextureMagic) return false;
            if (reader.ReadInt32() != TextureVersion) return false;

            var header = ReadTextureHeader(reader);
            if (header == null) return false;
            var rawByteCount = reader.ReadInt32();
            var compressedByteCount = reader.ReadInt32();
            var compressedBytes = reader.ReadBytes(compressedByteCount);
            if (compressedBytes.Length != compressedByteCount) return false;

            var pixelBytes = DecompressTextureBytes(compressedBytes, rawByteCount);
            if (pixelBytes.Length != rawByteCount) return false;

            texture = LoadTextureFromRgbaPixels(header.Value.Width, header.Value.Height, pixelBytes);
            if (texture.Id == 0) return false;

            SetTextureFilter(texture, TextureFilter.Bilinear);
            return true;

        } catch {

            return false;
        }
    }

    public static bool TryReadTextureInfo(string cacheFile, out TextureCacheInfo info) {

        info = default;

        try {

            using var file = File.OpenRead(cacheFile);
            using var reader = new BinaryReader(file);

            if (reader.ReadString() != TextureMagic) return false;
            if (reader.ReadInt32() != TextureVersion) return false;

            var header = ReadTextureHeader(reader);
            if (header == null) return false;

            info = new TextureCacheInfo(
                header.Value.Width,
                header.Value.Height,
                header.Value.Format,
                header.Value.Compression,
                header.Value.SourceWidth,
                header.Value.SourceHeight,
                header.Value.MaxSize,
                header.Value.ResizeFilter,
                header.Value.Quality,
                header.Value.RequestedFormat,
                header.Value.PayloadFormat
            );
            return true;

        } catch {

            return false;
        }
    }

    private static TextureCacheHeader? ReadTextureHeader(BinaryReader reader) {
        try {
            return new TextureCacheHeader(
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                (PixelFormat)reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadInt32(),
                reader.ReadString(),
                reader.ReadString()
            );
        } catch {
            return null;
        }
    }

    private static void WriteTextureHeader(BinaryWriter writer, TextureCacheHeader header) {
        writer.Write(header.SourceWidth);
        writer.Write(header.SourceHeight);
        writer.Write(header.Width);
        writer.Write(header.Height);
        writer.Write((int)header.Format);
        writer.Write(header.MaxSize);
        writer.Write(header.ResizeFilter);
        writer.Write(header.Compression);
        writer.Write(header.Quality);
        writer.Write(header.RequestedFormat);
        writer.Write(header.PayloadFormat);
    }

    public static bool IsTextureCacheCurrent(string sourceFile, string cacheFile, AssetSidecarData.TextureImportSettings settings) {

        if (!File.Exists(sourceFile) || !File.Exists(cacheFile)) return false;

        var cacheTime = new FileInfo(cacheFile).LastWriteTimeUtc;
        var sourceTime = new FileInfo(sourceFile).LastWriteTimeUtc;
        var sidecarPath = sourceFile + ".json";
        var sidecarTime = File.Exists(sidecarPath) ? new FileInfo(sidecarPath).LastWriteTimeUtc : DateTime.MinValue;

        if (cacheTime < sourceTime || cacheTime < sidecarTime) return false;
        if (!TryReadTextureInfo(cacheFile, out var info)) return false;

        return info.MaxSize == settings.MaxSize
               && string.Equals(info.ResizeFilter, settings.ResizeFilter ?? "Bilinear", StringComparison.Ordinal)
               && string.Equals(info.Compression, settings.Compression ?? "Balanced", StringComparison.Ordinal)
               && info.Quality == Math.Clamp(settings.Quality, 1, 100)
               && string.Equals(info.RequestedFormat, NormalizeRequestedFormat(settings.Format), StringComparison.Ordinal);
    }

    private static unsafe byte[] ExtractPixelBytes(Image image) {

        var pixelCount = image.Width * image.Height;
        if (pixelCount <= 0) return [];

        var colors = LoadImageColors(image);
        if (colors == null) return [];

        try {
            var bytes = new byte[pixelCount * 4];
            fixed (byte* destination = bytes)
                Buffer.MemoryCopy(colors, destination, bytes.Length, bytes.Length);

            return bytes;
        } finally {
            UnloadImageColors(colors);
        }
    }

    private static byte[] CompressTextureBytes(byte[] pixelBytes, AssetSidecarData.TextureImportSettings settings) {

        using var buffer = new MemoryStream();
        using (var brotli = new BrotliStream(buffer, GetTextureCompressionLevel(settings), leaveOpen: true))
            brotli.Write(pixelBytes, 0, pixelBytes.Length);

        return buffer.ToArray();
    }

    private static byte[] DecompressTextureBytes(byte[] compressedBytes, int expectedSize) {

        using var input = new MemoryStream(compressedBytes);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream(expectedSize);
        brotli.CopyTo(output);
        return output.ToArray();
    }

    private static CompressionLevel GetTextureCompressionLevel(AssetSidecarData.TextureImportSettings settings) => (settings.Compression ?? "Balanced") switch {
        "Fast" => CompressionLevel.Fastest,
        "Best" => CompressionLevel.SmallestSize,
        _ => CompressionLevel.Optimal
    };

    private static string NormalizeRequestedFormat(string? format) => string.IsNullOrWhiteSpace(format) ? "Source" : format;

    private static unsafe Texture2D LoadTextureFromRgbaPixels(int width, int height, byte[] pixelBytes) {

        if (width <= 0 || height <= 0 || pixelBytes.Length == 0) return default;

        fixed (byte* pixels = pixelBytes) {
            return new Texture2D {
                Id = Rlgl.LoadTexture(pixels, width, height, PixelFormat.UncompressedR8G8B8A8, 1),
                Width = width,
                Height = height,
                Mipmaps = 1,
                Format = PixelFormat.UncompressedR8G8B8A8
            };
        }
    }

    private static unsafe Image LoadImportedTextureImage(string sourceFile, string outputFile, AssetSidecarData.TextureImportSettings settings) {

#if !SCYTHE_RUNTIME_BUILD
        var tempFile = Path.Combine(Path.GetTempPath(), $"scythe-texture-{Path.GetFileNameWithoutExtension(outputFile)}-{Guid.NewGuid():N}{TextureImportProcessor.GetOutputExtension(sourceFile, settings)}");

        try {
            if (!TextureImportProcessor.Import(sourceFile, tempFile, settings))
                return LoadTextureImageDirect(sourceFile, settings);

            var image = LoadImage(tempFile);
            if (image.Data != null) return image;

            image = LoadImageFromMagickFallback(tempFile, settings);
            return image.Data != null ? image : LoadTextureImageDirect(sourceFile, settings);

        } finally {
            try {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            } catch {
            }
        }
#else
        return default;
#endif
    }

    private static unsafe Image LoadTextureImageDirect(string sourceFile, AssetSidecarData.TextureImportSettings settings) {

        var image = LoadImage(sourceFile);
        if (image.Data == null) return default;

        ApplyTextureImportSettings(ref image, settings);
        return image;
    }

#if !SCYTHE_RUNTIME_BUILD
    public static string EnsureModelCache(string sourceFile, string outputFile) {

        if (File.Exists(outputFile) && new FileInfo(outputFile).LastWriteTimeUtc >= new FileInfo(sourceFile).LastWriteTimeUtc)
            return outputFile;

        Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);

        var data = AssimpLoader.Load(sourceFile);

        using var file = File.Create(outputFile);
        using var buffered = new BufferedStream(file, 1024 * 128);
        using var brotli = new BrotliStream(buffered, CompressionLevel.SmallestSize);
        using var writer = new BinaryWriter(brotli);

        writer.Write(ModelMagic);
        writer.Write(ModelVersion);

        WriteMatrix(writer, data.GlobalInverse);

        writer.Write(data.Bones.Count);
        foreach (var bone in data.Bones) {

            writer.Write(bone.Name);
            writer.Write(bone.Index);
            WriteMatrix(writer, bone.Offset);
        }

        writer.Write(data.Meshes.Count);
        foreach (var mesh in data.Meshes) WriteMesh(writer, mesh);

        WriteNode(writer, data.Root);

        writer.Write(data.Animations.Count);
        foreach (var anim in data.Animations) WriteAnimation(writer, anim);

        return outputFile;
    }
#endif

#if !SCYTHE_RUNTIME_BUILD
    private static Image LoadImageFromMagickFallback(string importedFile, AssetSidecarData.TextureImportSettings settings) {

        var effectiveFormat = TextureImportProcessor.GetEffectiveFormat(importedFile, settings);
        if (effectiveFormat is not "WebP" and not "Avif") return default;

        var tempPng = Path.Combine(Path.GetTempPath(), $"scythe-texture-fallback-{Guid.NewGuid():N}.png");

        try {
            using var image = new MagickImage(importedFile);
            image.Write(tempPng, MagickFormat.Png);
            return File.Exists(tempPng) ? LoadImage(tempPng) : default;
        } finally {
            try {
                if (File.Exists(tempPng)) File.Delete(tempPng);
            } catch {
            }
        }
    }
#endif

    public static unsafe bool LoadModel(string cacheFile, out List<AssimpMesh> meshes, out List<BoneInfo> bones, out ModelNode root, out Matrix4x4 globalInverse, out List<AnimationClip> animations) {

        meshes = [];
        bones = [];
        root = new ModelNode();
        globalInverse = Matrix4x4.Identity;
        animations = [];

        try {

            using var file = File.OpenRead(cacheFile);
            using var buffered = new BufferedStream(file, 1024 * 128);
            using var brotli = new BrotliStream(buffered, CompressionMode.Decompress);
            using var reader = new BinaryReader(brotli);

            if (reader.ReadString() != ModelMagic) return false;
            if (reader.ReadInt32() != ModelVersion) return false;

            globalInverse = ReadMatrix(reader);

            var boneCount = reader.ReadInt32();
            for (var i = 0; i < boneCount; i++) {

                bones.Add(new BoneInfo {
                    Name = reader.ReadString(),
                    Index = reader.ReadInt32(),
                    Offset = ReadMatrix(reader)
                });
            }

            var meshCount = reader.ReadInt32();
            for (var i = 0; i < meshCount; i++) meshes.Add(ReadMesh(reader));

            root = ReadNode(reader);

            var animationCount = reader.ReadInt32();
            for (var i = 0; i < animationCount; i++) animations.Add(ReadAnimation(reader));

            return true;

        } catch {

            foreach (var mesh in meshes)
                if (mesh.RlMesh.Vertices != null)
                    UnloadMesh(mesh.RlMesh);

            meshes = [];
            bones = [];
            root = new ModelNode();
            globalInverse = Matrix4x4.Identity;
            animations = [];
            return false;
        }
    }

    private static unsafe AssimpMesh ReadMesh(BinaryReader reader) {

        var vertexCount = reader.ReadInt32();
        var indexCount = reader.ReadInt32();

        var mesh = new AssimpMesh {
            Name = reader.ReadString(),
            MaterialIndex = reader.ReadInt32(),
            UsesSkinning = reader.ReadBoolean(),
            Vertices = new Vector3[vertexCount],
            Normals = new Vector3[vertexCount],
            AnimatedVertices = new Vector3[vertexCount],
            AnimatedNormals = new Vector3[vertexCount],
            TexCoords = new Vector2[vertexCount],
            Indices = new uint[indexCount],
            BoneData = new VertexBoneData[vertexCount]
        };

        for (var i = 0; i < vertexCount; i++) mesh.Vertices[i] = ReadVector3(reader);
        for (var i = 0; i < vertexCount; i++) mesh.Normals[i] = ReadVector3(reader);
        for (var i = 0; i < vertexCount; i++) mesh.TexCoords[i] = ReadVector2(reader);
        for (var i = 0; i < indexCount; i++) mesh.Indices[i] = reader.ReadUInt32();

        for (var i = 0; i < vertexCount; i++) {

            mesh.BoneData[i] = new VertexBoneData {
                Bone0 = reader.ReadInt32(),
                Bone1 = reader.ReadInt32(),
                Bone2 = reader.ReadInt32(),
                Bone3 = reader.ReadInt32(),
                Weight0 = reader.ReadSingle(),
                Weight1 = reader.ReadSingle(),
                Weight2 = reader.ReadSingle(),
                Weight3 = reader.ReadSingle()
            };
        }

        Array.Copy(mesh.Vertices, mesh.AnimatedVertices, vertexCount);
        Array.Copy(mesh.Normals, mesh.AnimatedNormals, vertexCount);

        mesh.RlMesh = AssimpMesh.CreateUploadedMesh(mesh.Vertices, mesh.Normals, mesh.TexCoords, mesh.Indices);

        return mesh;
    }

    private static void WriteMesh(BinaryWriter writer, AssimpMesh mesh) {

        writer.Write(mesh.Vertices.Length);
        writer.Write(mesh.Indices.Length);
        writer.Write(mesh.Name);
        writer.Write(mesh.MaterialIndex);
        writer.Write(mesh.UsesSkinning);

        foreach (var value in mesh.Vertices) WriteVector3(writer, value);
        foreach (var value in mesh.Normals) WriteVector3(writer, value);
        foreach (var value in mesh.TexCoords) WriteVector2(writer, value);
        foreach (var value in mesh.Indices) writer.Write(value);

        foreach (var value in mesh.BoneData) {

            writer.Write(value.Bone0);
            writer.Write(value.Bone1);
            writer.Write(value.Bone2);
            writer.Write(value.Bone3);
            writer.Write(value.Weight0);
            writer.Write(value.Weight1);
            writer.Write(value.Weight2);
            writer.Write(value.Weight3);
        }
    }

    private static void WriteNode(BinaryWriter writer, ModelNode node) {

        writer.Write(node.Name);
        WriteMatrix(writer, node.Transformation);
        writer.Write(node.Children.Count);

        foreach (var child in node.Children) WriteNode(writer, child);
    }

    private static ModelNode ReadNode(BinaryReader reader) {

        var node = new ModelNode {
            Name = reader.ReadString(),
            Transformation = ReadMatrix(reader)
        };

        var childCount = reader.ReadInt32();
        for (var i = 0; i < childCount; i++) node.Children.Add(ReadNode(reader));

        return node;
    }

    private static void WriteAnimation(BinaryWriter writer, AnimationClip clip) {

        writer.Write(clip.Name);
        writer.Write(clip.SourceTrack);
        writer.Write(clip.StartFrame);
        writer.Write(clip.EndFrame);
        writer.Write(clip.Loop);
        writer.Write(clip.Duration);
        writer.Write(clip.TicksPerSecond);
        writer.Write(clip.Channels.Count);

        foreach (var channel in clip.Channels) {

            writer.Write(channel.NodeName);

            writer.Write(channel.PositionKeys.Count);
            foreach (var (time, position) in channel.PositionKeys) {

                writer.Write(time);
                WriteVector3(writer, position);
            }

            writer.Write(channel.RotationKeys.Count);
            foreach (var (time, rotation) in channel.RotationKeys) {

                writer.Write(time);
                WriteQuaternion(writer, rotation);
            }

            writer.Write(channel.ScaleKeys.Count);
            foreach (var (time, scale) in channel.ScaleKeys) {

                writer.Write(time);
                WriteVector3(writer, scale);
            }
        }
    }

    private static AnimationClip ReadAnimation(BinaryReader reader) {

        var clip = new AnimationClip {
            Name = reader.ReadString(),
            SourceTrack = reader.ReadInt32(),
            StartFrame = reader.ReadDouble(),
            EndFrame = reader.ReadDouble(),
            Loop = reader.ReadBoolean(),
            Duration = reader.ReadDouble(),
            TicksPerSecond = reader.ReadDouble()
        };

        var channelCount = reader.ReadInt32();
        for (var i = 0; i < channelCount; i++) {

            var channel = new AnimationChannel { NodeName = reader.ReadString() };

            var positionCount = reader.ReadInt32();
            for (var j = 0; j < positionCount; j++) channel.PositionKeys.Add((reader.ReadDouble(), ReadVector3(reader)));

            var rotationCount = reader.ReadInt32();
            for (var j = 0; j < rotationCount; j++) channel.RotationKeys.Add((reader.ReadDouble(), ReadQuaternion(reader)));

            var scaleCount = reader.ReadInt32();
            for (var j = 0; j < scaleCount; j++) channel.ScaleKeys.Add((reader.ReadDouble(), ReadVector3(reader)));

            clip.Channels.Add(channel);
            clip.ChannelMap[channel.NodeName] = channel;
        }

        return clip;
    }

    private static void WriteMatrix(BinaryWriter writer, Matrix4x4 matrix) {

        writer.Write(matrix.M11); writer.Write(matrix.M12); writer.Write(matrix.M13); writer.Write(matrix.M14);
        writer.Write(matrix.M21); writer.Write(matrix.M22); writer.Write(matrix.M23); writer.Write(matrix.M24);
        writer.Write(matrix.M31); writer.Write(matrix.M32); writer.Write(matrix.M33); writer.Write(matrix.M34);
        writer.Write(matrix.M41); writer.Write(matrix.M42); writer.Write(matrix.M43); writer.Write(matrix.M44);
    }

    private static Matrix4x4 ReadMatrix(BinaryReader reader) => new(
        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()
    );

    private static void WriteVector3(BinaryWriter writer, Vector3 value) {

        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }

    private static Vector3 ReadVector3(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    private static void WriteVector2(BinaryWriter writer, Vector2 value) {

        writer.Write(value.X);
        writer.Write(value.Y);
    }

    private static Vector2 ReadVector2(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle());

    private static void WriteQuaternion(BinaryWriter writer, Quaternion value) {

        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
        writer.Write(value.W);
    }

    private static Quaternion ReadQuaternion(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    private static unsafe void ApplyTextureImportSettings(ref Image image, AssetSidecarData.TextureImportSettings settings) {

        settings ??= new AssetSidecarData.TextureImportSettings();

        var maxSize = settings.MaxSize;
        if (maxSize > 0 && (image.Width > maxSize || image.Height > maxSize)) {

            var scale = Math.Min((float)maxSize / image.Width, (float)maxSize / image.Height);
            var targetWidth = Math.Max(1, (int)MathF.Round(image.Width * scale));
            var targetHeight = Math.Max(1, (int)MathF.Round(image.Height * scale));

            if (string.Equals(settings.ResizeFilter, "Nearest", StringComparison.OrdinalIgnoreCase))
                ImageResizeNN(ref image, targetWidth, targetHeight);
            else
                ImageResize(ref image, targetWidth, targetHeight);
        }
    }
}
