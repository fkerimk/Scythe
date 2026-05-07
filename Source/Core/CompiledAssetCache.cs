using System.IO.Compression;
using System.Numerics;
using Raylib_cs;
using static Raylib_cs.Raylib;

internal static class CompiledAssetCache {

    private const int TextureVersion = 3;
    private const int ModelVersion = 2;
    private const string TextureMagic = "STEX";
    private const string ModelMagic = "SMOD";

    public readonly record struct TextureCacheInfo(int Width, int Height, PixelFormat Format, string Compression, int SourceWidth, int SourceHeight, int MaxSize, string ResizeFilter, string EncodedFormat);

    public static unsafe string EnsureTextureCache(string sourceFile, string outputFile, AssetSidecarData.TextureImportSettings settings) {

        if (File.Exists(outputFile) && new FileInfo(outputFile).LastWriteTimeUtc >= new FileInfo(sourceFile).LastWriteTimeUtc)
            return outputFile;

        Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);

        var image = LoadImage(sourceFile);
        if (image.Data == null) return sourceFile;

        try {

            var sourceWidth = image.Width;
            var sourceHeight = image.Height;
            ApplyTextureImportSettings(ref image, settings);
            var encodedFormat = ChooseEncodedTextureFormat(sourceFile);
            var tempPath = outputFile + encodedFormat;
            ExportImage(image, tempPath);
            var encodedBytes = File.ReadAllBytes(tempPath);
            File.Delete(tempPath);

            using var file = File.Create(outputFile);
            using var writer = new BinaryWriter(file);

            writer.Write(TextureMagic);
            writer.Write(TextureVersion);
            writer.Write(sourceWidth);
            writer.Write(sourceHeight);
            writer.Write(image.Width);
            writer.Write(image.Height);
            writer.Write((int)image.Format);
            writer.Write(settings.MaxSize);
            writer.Write(settings.ResizeFilter ?? "Bilinear");
            writer.Write(settings.Compression ?? "Normal");
            writer.Write(encodedFormat);
            writer.Write(encodedBytes.Length);
            writer.Write(encodedBytes);

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

            _ = reader.ReadInt32();
            _ = reader.ReadInt32();
            var width = reader.ReadInt32();
            var height = reader.ReadInt32();
            var format = (PixelFormat)reader.ReadInt32();
            _ = reader.ReadInt32();
            _ = reader.ReadString();
            _ = reader.ReadString();
            var encodedFormat = reader.ReadString();
            var byteCount = reader.ReadInt32();
            var encodedBytes = reader.ReadBytes(byteCount);
            if (encodedBytes.Length != byteCount) return false;

            var image = LoadImageFromMemory(encodedFormat, encodedBytes);
            if (image.Data == null) return false;

            texture = LoadTextureFromImage(image);
            UnloadImage(image);

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

            var sourceWidth = reader.ReadInt32();
            var sourceHeight = reader.ReadInt32();
            var width = reader.ReadInt32();
            var height = reader.ReadInt32();
            var format = (PixelFormat)reader.ReadInt32();
            var maxSize = reader.ReadInt32();
            var resizeFilter = reader.ReadString();
            var compression = reader.ReadString();
            var encodedFormat = reader.ReadString();

            info = new TextureCacheInfo(width, height, format, compression, sourceWidth, sourceHeight, maxSize, resizeFilter, encodedFormat);
            return true;

        } catch {

            return false;
        }
    }

    public static string EnsureModelCache(string sourceFile, string outputFile) {

        if (File.Exists(outputFile) && new FileInfo(outputFile).LastWriteTimeUtc >= new FileInfo(sourceFile).LastWriteTimeUtc)
            return outputFile;

        Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);

        var data = AssimpLoader.Load(sourceFile);

        using var file = File.Create(outputFile);
        using var brotli = new BrotliStream(file, CompressionLevel.Fastest);
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

    public static unsafe bool LoadModel(string cacheFile, out List<AssimpMesh> meshes, out List<BoneInfo> bones, out ModelNode root, out Matrix4x4 globalInverse, out List<AnimationClip> animations) {

        meshes = [];
        bones = [];
        root = new ModelNode();
        globalInverse = Matrix4x4.Identity;
        animations = [];

        try {

            using var file = File.OpenRead(cacheFile);
            using var brotli = new BrotliStream(file, CompressionMode.Decompress);
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

        mesh.RlMesh = CreateUploadedMesh(mesh.Vertices, mesh.Normals, mesh.TexCoords, mesh.Indices);

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

    private static unsafe Mesh CreateUploadedMesh(Vector3[] vertices, Vector3[] normals, Vector2[] texCoords, uint[] indices) {

        var rlMesh = new Mesh {
            VertexCount = vertices.Length,
            TriangleCount = indices.Length / 3,
            Vertices = (float*)MemAlloc((uint)(vertices.Length * 3 * sizeof(float))),
            Normals = (float*)MemAlloc((uint)(normals.Length * 3 * sizeof(float))),
            TexCoords = (float*)MemAlloc((uint)(texCoords.Length * 2 * sizeof(float))),
            Indices = (ushort*)MemAlloc((uint)(indices.Length * sizeof(ushort)))
        };

        fixed (Vector3* v = vertices) Buffer.MemoryCopy(v, rlMesh.Vertices, (long)vertices.Length * 3 * sizeof(float), (long)vertices.Length * 3 * sizeof(float));
        fixed (Vector3* n = normals) Buffer.MemoryCopy(n, rlMesh.Normals, (long)normals.Length * 3 * sizeof(float), (long)normals.Length * 3 * sizeof(float));
        fixed (Vector2* t = texCoords) Buffer.MemoryCopy(t, rlMesh.TexCoords, (long)texCoords.Length * 2 * sizeof(float), (long)texCoords.Length * 2 * sizeof(float));

        for (var i = 0; i < indices.Length; i++) rlMesh.Indices[i] = (ushort)indices[i];

        GenMeshTangents(ref rlMesh);
        UploadMesh(ref rlMesh, false);

        return rlMesh;
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

    private static string ChooseEncodedTextureFormat(string sourceFile) => Path.GetExtension(sourceFile).ToLowerInvariant() switch {
        ".jpg" => ".jpg",
        ".jpeg" => ".jpg",
        _ => ".png"
    };
}
