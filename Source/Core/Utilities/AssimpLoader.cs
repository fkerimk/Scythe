using System.Numerics;
using Assimp;
using Matrix4x4 = System.Numerics.Matrix4x4;
using Quaternion = System.Numerics.Quaternion;

internal static partial class AssimpLoader {

    private static readonly AssimpContext Context = new();
    private static readonly PostProcessSteps DefaultPostProcessSteps =
        PostProcessSteps.Triangulate
        | PostProcessSteps.FlipUVs
        | PostProcessSteps.GenerateSmoothNormals
        | PostProcessSteps.CalculateTangentSpace
        | PostProcessSteps.LimitBoneWeights
        | PostProcessSteps.SortByPrimitiveType;

    public static (List<AssimpMesh> Meshes, List<BoneInfo> Bones, ModelNode Root, Matrix4x4 GlobalInverse, List<AnimationClip> Animations) Load(string path) {

        var scene = ImportScene(path);

        if (scene == null || scene.SceneFlags.HasFlag(SceneFlags.Incomplete) || scene.RootNode == null)
            throw new Exception($"Assimp error: {path}");

        var globalInverse = scene.RootNode.Transform.ToNumerics();
        Matrix4x4.Invert(globalInverse, out globalInverse);

        var bones = new List<BoneInfo>();
        var boneMapping = new Dictionary<string, List<int>>();

        var animations = scene.Animations.Select(ProcessAnimation).ToList();
        for (var i = 0; i < animations.Count; i++) {
            animations[i].SourceTrack = i;
            animations[i].StartFrame = 0d;
            animations[i].EndFrame = animations[i].Duration;
        }

        return (
            scene.Meshes.Select(mesh => ProcessMesh(mesh, bones, boneMapping)).ToList(),
            bones,
            ProcessNode(scene.RootNode),
            globalInverse,
            animations
        );
    }

    private static Scene ImportScene(string path) {

        var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        var errors = new List<Exception>();

        if (extension == "glb") {
            var hintOrder = new[] { "gltf2", "glb" };
            foreach (var hint in hintOrder)
                if (TryImportSceneFromStream(path, hint, out var hintedScene, out var hintedError))
                    return hintedScene;
                else if (hintedError != null)
                    errors.Add(hintedError);
        }

        try {
            return Context.ImportFile(path, DefaultPostProcessSteps);
        } catch (Exception e) {
            errors.Add(e);
        }

        if (errors.Count == 1) throw errors[0];

        throw new AggregateException($"Failed to import model {path}", errors);
    }

    private static bool TryImportSceneFromStream(string path, string formatHint, out Scene scene, out Exception? error) {

        try {
            using var stream = File.OpenRead(path);
            scene = Context.ImportFileFromStream(stream, DefaultPostProcessSteps, formatHint);
            error = null;
            return true;
        } catch (Exception e) {
            scene = null!;
            error = e;
            return false;
        }
    }

    private static AssimpMesh ProcessMesh(Assimp.Mesh mesh, List<BoneInfo> bones, Dictionary<string, List<int>> boneMapping) {

        var assimpMesh = new AssimpMesh {
            Name = mesh.Name,
            Vertices = new Vector3[mesh.VertexCount],
            Normals = new Vector3[mesh.VertexCount],
            AnimatedVertices = new Vector3[mesh.VertexCount],
            AnimatedNormals = new Vector3[mesh.VertexCount],
            TexCoords = new Vector2[mesh.VertexCount],
            Indices = new uint[mesh.FaceCount * 3],
            BoneData = new VertexBoneData[mesh.VertexCount],
            MaterialIndex = mesh.MaterialIndex
        };

        for (var i = 0; i < mesh.VertexCount; i++) {
            assimpMesh.Vertices[i] = mesh.Vertices[i].ToNumerics();
            assimpMesh.Normals[i] = mesh.Normals[i].ToNumerics();

            if (mesh.HasTextureCoords(0))
                assimpMesh.TexCoords[i] = new Vector2(mesh.TextureCoordinateChannels[0][i].X, mesh.TextureCoordinateChannels[0][i].Y);
        }

        for (var i = 0; i < mesh.FaceCount; i++) {
            assimpMesh.Indices[i * 3] = (uint)mesh.Faces[i].Indices[0];
            assimpMesh.Indices[i * 3 + 1] = (uint)mesh.Faces[i].Indices[1];
            assimpMesh.Indices[i * 3 + 2] = (uint)mesh.Faces[i].Indices[2];
        }

        foreach (var bone in mesh.Bones) {
            if (!boneMapping.TryGetValue(bone.Name, out var matchingIndices)) {
                matchingIndices = [];
                boneMapping[bone.Name] = matchingIndices;
            }

            var offset = bone.OffsetMatrix.ToNumerics();
            var boneIndex = matchingIndices.FirstOrDefault(idx => MatricesAreEqual(bones[idx].Offset, offset), -1);

            if (boneIndex == -1) {
                boneIndex = bones.Count;
                bones.Add(new BoneInfo { Name = bone.Name, Index = boneIndex, Offset = offset });
                matchingIndices.Add(boneIndex);
            }

            foreach (var weight in bone.VertexWeights)
                assimpMesh.BoneData[weight.VertexID].AddBoneData(boneIndex, weight.Weight);
        }

        assimpMesh.UsesSkinning = mesh.Bones.Count > 0;
        assimpMesh.RlMesh = AssimpMesh.CreateUploadedMesh(assimpMesh.Vertices, assimpMesh.Normals, assimpMesh.TexCoords, assimpMesh.Indices);
        return assimpMesh;
    }

    private static ModelNode ProcessNode(Node node) {

        var modelNode = new ModelNode {
            Name = node.Name,
            Transformation = node.Transform.ToNumerics()
        };

        foreach (var child in node.Children)
            modelNode.Children.Add(ProcessNode(child));

        return modelNode;
    }

    private static AnimationClip ProcessAnimation(Assimp.Animation animation) {

        var clip = new AnimationClip {
            Name = animation.Name,
            TicksPerSecond = animation.TicksPerSecond != 0 ? animation.TicksPerSecond : 25.0
        };

        var maxTime = 0d;

        foreach (var channel in animation.NodeAnimationChannels) {
            var animationChannel = new AnimationChannel { NodeName = channel.NodeName };

            foreach (var key in channel.PositionKeys) {
                animationChannel.PositionKeys.Add((key.Time, key.Value.ToNumerics()));
                maxTime = Math.Max(maxTime, key.Time);
            }

            foreach (var key in channel.RotationKeys) {
                animationChannel.RotationKeys.Add((key.Time, key.Value.ToNumerics()));
                maxTime = Math.Max(maxTime, key.Time);
            }

            foreach (var key in channel.ScalingKeys) {
                animationChannel.ScaleKeys.Add((key.Time, key.Value.ToNumerics()));
                maxTime = Math.Max(maxTime, key.Time);
            }

            clip.Channels.Add(animationChannel);
            clip.ChannelMap[animationChannel.NodeName] = animationChannel;
        }

        clip.Duration = maxTime > 0 ? maxTime : animation.DurationInTicks;
        return clip;
    }

    private static bool MatricesAreEqual(Matrix4x4 left, Matrix4x4 right) {

        const float epsilon = 0.0001f;

        return Math.Abs(left.M11 - right.M11) < epsilon
               && Math.Abs(left.M12 - right.M12) < epsilon
               && Math.Abs(left.M13 - right.M13) < epsilon
               && Math.Abs(left.M14 - right.M14) < epsilon
               && Math.Abs(left.M21 - right.M21) < epsilon
               && Math.Abs(left.M22 - right.M22) < epsilon
               && Math.Abs(left.M23 - right.M23) < epsilon
               && Math.Abs(left.M24 - right.M24) < epsilon
               && Math.Abs(left.M31 - right.M31) < epsilon
               && Math.Abs(left.M32 - right.M32) < epsilon
               && Math.Abs(left.M33 - right.M33) < epsilon
               && Math.Abs(left.M34 - right.M34) < epsilon
               && Math.Abs(left.M41 - right.M41) < epsilon
               && Math.Abs(left.M42 - right.M42) < epsilon
               && Math.Abs(left.M43 - right.M43) < epsilon
               && Math.Abs(left.M44 - right.M44) < epsilon;
    }

    // Assimp matrices are effectively transposed relative to the System.Numerics convention used by the engine.
    private static Matrix4x4 ToNumerics(this Matrix4x4 matrix) => Matrix4x4.Transpose(matrix);
    private static Vector3 ToNumerics(this Vector3 vector) => vector;
    private static Quaternion ToNumerics(this Quaternion quaternion) => quaternion;
}
