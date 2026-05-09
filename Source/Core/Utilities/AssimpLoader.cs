using System.Numerics;
using Assimp;
using Raylib_cs;
using Matrix4x4 = System.Numerics.Matrix4x4;
using Quaternion = System.Numerics.Quaternion;

internal struct VertexBoneData {

    public int Bone0, Bone1, Bone2, Bone3;
    public float Weight0, Weight1, Weight2, Weight3;

    public void AddBoneData(int id, float weight) {

        if (weight <= 0) return;

        if (Weight0 <= 0) {

            Bone0 = id;
            Weight0 = weight;

        } else if (Weight1 <= 0) {

            Bone1 = id;
            Weight1 = weight;

        } else if (Weight2 <= 0) {

            Bone2 = id;
            Weight2 = weight;

        } else if (Weight3 <= 0) {

            Bone3 = id;
            Weight3 = weight;
        }
    }
}

internal class BoneInfo {

    public string Name = "";
    public int Index;
    public Matrix4x4 Offset;
    public Matrix4x4 FinalTransformation;
}

internal class ModelNode {

    public string Name = "";
    public Matrix4x4 Transformation;
    public readonly List<ModelNode> Children = [];
}

internal class AssimpMesh {

    public string Name = "";
    public Vector3[] Vertices = null!;
    public Vector3[] Normals = null!;
    public Vector2[] TexCoords = null!;
    public uint[] Indices = null!;
    public VertexBoneData[] BoneData = null!;
    public Raylib_cs.Mesh RlMesh;
    public int MaterialIndex;
    public bool UsesSkinning;

    public Vector3[] AnimatedVertices = null!;
    public Vector3[] AnimatedNormals = null!;

    public unsafe AssimpMesh Clone() {

        var am = new AssimpMesh {
            Name = Name,
            Vertices = (Vector3[])Vertices.Clone(),
            Normals = (Vector3[])Normals.Clone(),
            AnimatedVertices = (Vector3[])AnimatedVertices.Clone(),
            AnimatedNormals = (Vector3[])AnimatedNormals.Clone(),
            TexCoords = (Vector2[])TexCoords.Clone(),
            Indices = (uint[])Indices.Clone(),
            BoneData = (VertexBoneData[])BoneData.Clone(),
            MaterialIndex = MaterialIndex,
            UsesSkinning = UsesSkinning,

            // Initialize unique RlMesh for this instance
            RlMesh = new Raylib_cs.Mesh {
                VertexCount = Vertices.Length,
                TriangleCount = Indices.Length / 3,
                Vertices = (float*)Raylib.MemAlloc((uint)(Vertices.Length * 3 * sizeof(float))),
                Normals = (float*)Raylib.MemAlloc((uint)(Vertices.Length * 3 * sizeof(float))),
                TexCoords = (float*)Raylib.MemAlloc((uint)(Vertices.Length * 2 * sizeof(float))),
                Indices = (ushort*)Raylib.MemAlloc((uint)(Indices.Length * sizeof(ushort)))
            }
        };

        fixed (Vector3* v = Vertices) Buffer.MemoryCopy(v, am.RlMesh.Vertices, (long)Vertices.Length * 3 * sizeof(float), (long)Vertices.Length * 3 * sizeof(float));
        fixed (Vector3* n = Normals) Buffer.MemoryCopy(n, am.RlMesh.Normals, (long)Vertices.Length * 3 * sizeof(float), (long)Vertices.Length * 3 * sizeof(float));
        fixed (Vector2* t = TexCoords) Buffer.MemoryCopy(t, am.RlMesh.TexCoords, (long)Vertices.Length * 2 * sizeof(float), (long)Vertices.Length * 2 * sizeof(float));

        for (var i = 0; i < Indices.Length; i++) am.RlMesh.Indices[i] = (ushort)Indices[i];

        // Generate Tangents for the cloned mesh
        Raylib.GenMeshTangents(ref am.RlMesh);

        Raylib.UploadMesh(ref am.RlMesh, false);

        return am;
    }
}

internal class AnimationChannel {

    public string NodeName = "";
    public readonly List<(double Time, Vector3 Position)> PositionKeys = [];
    public readonly List<(double Time, Quaternion Rotation)> RotationKeys = [];
    public readonly List<(double Time, Vector3 Scale)> ScaleKeys = [];
}

internal class AnimationClip {

    public string Name = "";
    public double Duration;
    public double TicksPerSecond;
    public readonly List<AnimationChannel> Channels = [];
    public readonly Dictionary<string, AnimationChannel> ChannelMap = [];
}

internal static class AssimpLoader {

    private static readonly AssimpContext Context = new();

    public static (List<AssimpMesh> Meshes, List<BoneInfo> Bones, ModelNode Root, Matrix4x4 GlobalInverse, List<AnimationClip> Animations) Load(string path) {

        var scene = Context.ImportFile(path, PostProcessSteps.Triangulate | PostProcessSteps.FlipUVs | PostProcessSteps.GenerateSmoothNormals | PostProcessSteps.CalculateTangentSpace | PostProcessSteps.LimitBoneWeights | PostProcessSteps.SortByPrimitiveType);

        if (scene == null || scene.SceneFlags.HasFlag(SceneFlags.Incomplete) || scene.RootNode == null) throw new Exception($"Assimp error: {path}");

        var globalInverse = scene.RootNode.Transform.ToNumerics();

        Matrix4x4.Invert(globalInverse, out globalInverse);

        var bones = new List<BoneInfo>();
        var boneMapping = new Dictionary<string, List<int>>();

        var meshes = scene.Meshes.Select(mesh => ProcessMesh(mesh, bones, boneMapping)).ToList();

        var root = ProcessNode(scene.RootNode);

        var animations = scene.Animations.Select(ProcessAnimation).ToList();

        return (meshes, bones, root, globalInverse, animations);
    }

    private static unsafe AssimpMesh ProcessMesh(Assimp.Mesh mesh, List<BoneInfo> bones, Dictionary<string, List<int>> boneMapping) {

        var am = new AssimpMesh {
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

            am.Vertices[i] = mesh.Vertices[i].ToNumerics();
            am.Normals[i] = mesh.Normals[i].ToNumerics();

            if (mesh.HasTextureCoords(0)) am.TexCoords[i] = new Vector2(mesh.TextureCoordinateChannels[0][i].X, mesh.TextureCoordinateChannels[0][i].Y);
        }

        for (var i = 0; i < mesh.FaceCount; i++) {

            am.Indices[i * 3 + 0] = (uint)mesh.Faces[i].Indices[0];
            am.Indices[i * 3 + 1] = (uint)mesh.Faces[i].Indices[1];
            am.Indices[i * 3 + 2] = (uint)mesh.Faces[i].Indices[2];
        }

        foreach (var bone in mesh.Bones) {

            if (!boneMapping.TryGetValue(bone.Name, out var indices)) {

                indices = [];
                boneMapping[bone.Name] = indices;
            }

            var boneIndex = -1;
            var offset = bone.OffsetMatrix.ToNumerics();

            foreach (var idx in indices.Where(idx => MatricesAreEqual(bones[idx].Offset, offset))) {

                boneIndex = idx;

                break;
            }

            if (boneIndex == -1) {

                boneIndex = bones.Count;

                bones.Add(new BoneInfo { Name = bone.Name, Index = boneIndex, Offset = offset });

                indices.Add(boneIndex);
            }

            foreach (var weight in bone.VertexWeights) am.BoneData[weight.VertexID].AddBoneData(boneIndex, weight.Weight);
        }

        am.UsesSkinning = mesh.Bones.Count > 0;

        // Initialize RlMesh
        am.RlMesh = new Raylib_cs.Mesh {
            VertexCount = mesh.VertexCount,
            TriangleCount = mesh.FaceCount,
            Vertices = (float*)Raylib.MemAlloc((uint)(mesh.VertexCount * 3 * sizeof(float))),
            Normals = (float*)Raylib.MemAlloc((uint)(mesh.VertexCount * 3 * sizeof(float))),
            TexCoords = (float*)Raylib.MemAlloc((uint)(mesh.VertexCount * 2 * sizeof(float))),
            Indices = (ushort*)Raylib.MemAlloc((uint)(mesh.FaceCount * 3 * sizeof(ushort)))
        };

        // Fill initial data
        fixed (Vector3* v = am.Vertices) Buffer.MemoryCopy(v, am.RlMesh.Vertices, (long)am.RlMesh.VertexCount * 3 * sizeof(float), (long)am.RlMesh.VertexCount * 3 * sizeof(float));

        fixed (Vector3* n = am.Normals) Buffer.MemoryCopy(n, am.RlMesh.Normals, (long)am.RlMesh.VertexCount * 3 * sizeof(float), (long)am.RlMesh.VertexCount * 3 * sizeof(float));

        fixed (Vector2* t = am.TexCoords) Buffer.MemoryCopy(t, am.RlMesh.TexCoords, (long)am.RlMesh.VertexCount * 2 * sizeof(float), (long)am.RlMesh.VertexCount * 2 * sizeof(float));

        for (var i = 0; i < am.Indices.Length; i++) am.RlMesh.Indices[i] = (ushort)am.Indices[i];

        // Generate Tangents using Raylib's robust internal logic
        Raylib.GenMeshTangents(ref am.RlMesh);

        Raylib.UploadMesh(ref am.RlMesh, false);

        return am;
    }

    private static ModelNode ProcessNode(Node node) {

        var mn = new ModelNode { Name = node.Name, Transformation = node.Transform.ToNumerics() };

        foreach (var child in node.Children) mn.Children.Add(ProcessNode(child));

        return mn;
    }

    private static AnimationClip ProcessAnimation(Assimp.Animation anim) {

        var clip = new AnimationClip { Name = anim.Name, TicksPerSecond = anim.TicksPerSecond != 0 ? anim.TicksPerSecond : 25.0 };

        double maxTime = 0;

        foreach (var channel in anim.NodeAnimationChannels) {

            var c = new AnimationChannel { NodeName = channel.NodeName };

            foreach (var k in channel.PositionKeys) {

                c.PositionKeys.Add((k.Time, k.Value.ToNumerics()));
                if (k.Time > maxTime) maxTime = k.Time;
            }

            foreach (var k in channel.RotationKeys) {

                c.RotationKeys.Add((k.Time, k.Value.ToNumerics()));
                if (k.Time > maxTime) maxTime = k.Time;
            }

            foreach (var k in channel.ScalingKeys) {

                c.ScaleKeys.Add((k.Time, k.Value.ToNumerics()));
                if (k.Time > maxTime) maxTime = k.Time;
            }

            clip.Channels.Add(c);
        }

        // Use the actual content length, not the metadata duration which can be bloated
        clip.Duration = maxTime > 0 ? maxTime : anim.DurationInTicks;

        // Pre-build channel map for faster lookup
        foreach (var channel in clip.Channels) clip.ChannelMap[channel.NodeName] = channel;

        return clip;
    }

    public static void UpdateAnimation(ModelNode node, AnimationClip clip, double time, in Matrix4x4 parentTransform, in Matrix4x4 globalInverse, Dictionary<string, List<BoneInfo>> boneMap) {

        var nodeTransform = node.Transformation;

        if (clip.ChannelMap.TryGetValue(node.Name, out var channel)) nodeTransform = GetInterpolatedTransform(channel, time, node.Transformation);

        var globalTransform = nodeTransform * parentTransform;

        if (boneMap.TryGetValue(node.Name, out var bones))
            foreach (var bone in bones)
                bone.FinalTransformation = bone.Offset * globalTransform * globalInverse;

        foreach (var child in node.Children) UpdateAnimation(child, clip, time, globalTransform, globalInverse, boneMap);
    }

    public static void ApplyBindPose(ModelNode node, in Matrix4x4 parentTransform, in Matrix4x4 globalInverse, Dictionary<string, List<BoneInfo>> boneMap) {

        var globalTransform = node.Transformation * parentTransform;

        if (boneMap.TryGetValue(node.Name, out var bones))
            foreach (var bone in bones)
                bone.FinalTransformation = bone.Offset * globalTransform * globalInverse;

        foreach (var child in node.Children) ApplyBindPose(child, globalTransform, globalInverse, boneMap);
    }

    public static void UpdateAnimationBlended(ModelNode node, AnimationClip clipA, double timeA, AnimationClip clipB, double timeB, float blend, in Matrix4x4 parentTransform, in Matrix4x4 globalInverse, Dictionary<string, List<BoneInfo>> boneMap) {

        var nodeTransform = GetBlendedTransform(clipA.ChannelMap.GetValueOrDefault(node.Name), timeA, clipB.ChannelMap.GetValueOrDefault(node.Name), timeB, blend, node.Transformation);

        var globalTransform = nodeTransform * parentTransform;

        if (boneMap.TryGetValue(node.Name, out var bones))
            foreach (var bone in bones)
                bone.FinalTransformation = bone.Offset * globalTransform * globalInverse;

        foreach (var child in node.Children) UpdateAnimationBlended(child, clipA, timeA, clipB, timeB, blend, globalTransform, globalInverse, boneMap);
    }

    private static Matrix4x4 GetInterpolatedTransform(AnimationChannel channel, double time, Matrix4x4 bindPose) {

        Matrix4x4.Decompose(bindPose, out var bScale, out var bRot, out var bPos);

        var pos = InterpolatePosition(channel.PositionKeys, time, bPos);
        var rot = InterpolateRotation(channel.RotationKeys, time, bRot);
        var scale = InterpolateScale(channel.ScaleKeys, time, bScale);

        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);
    }

    private static Matrix4x4 GetBlendedTransform(AnimationChannel? channelA, double timeA, AnimationChannel? channelB, double timeB, float blend, Matrix4x4 bindPose) {

        Matrix4x4.Decompose(bindPose, out var bScale, out var bRot, out var bPos);
        Vector3 posA, posB, scaleA, scaleB;
        Quaternion rotA, rotB;

        if (channelA != null) {

            posA = InterpolatePosition(channelA.PositionKeys, timeA, bPos);
            rotA = InterpolateRotation(channelA.RotationKeys, timeA, bRot);
            scaleA = InterpolateScale(channelA.ScaleKeys, timeA, bScale);

        } else {

            posA = bPos;
            rotA = bRot;
            scaleA = bScale;
        }

        if (channelB != null) {

            posB = InterpolatePosition(channelB.PositionKeys, timeB, bPos);
            rotB = InterpolateRotation(channelB.RotationKeys, timeB, bRot);
            scaleB = InterpolateScale(channelB.ScaleKeys, timeB, bScale);

        } else {

            posB = bPos;
            rotB = bRot;
            scaleB = bScale;
        }

        var pos = Vector3.Lerp(posA, posB, blend);
        var rot = Quaternion.Slerp(rotA, rotB, blend);
        var scale = Vector3.Lerp(scaleA, scaleB, blend);

        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);
    }

    private static Vector3 InterpolatePosition(List<(double Time, Vector3 Position)> keys, double time, Vector3 fallback) {

        switch (keys.Count) {

            case 0: return fallback;
            case 1: return keys[0].Position;
        }

        var i = 0;

        for (; i < keys.Count - 1; i++)
            if (time < keys[i + 1].Time)
                break;

        var next = (i + 1) % keys.Count;
        var t1 = keys[i].Time;
        var t2 = keys[next].Time;

        if (t2 <= t1) return keys[i].Position;

        var factor = (float)((time - t1) / (t2 - t1));

        return Vector3.Lerp(keys[i].Position, keys[next].Position, Math.Clamp(factor, 0f, 1f));
    }

    private static Quaternion InterpolateRotation(List<(double Time, Quaternion Rotation)> keys, double time, Quaternion fallback) {

        switch (keys.Count) {

            case 0: return fallback;
            case 1: return keys[0].Rotation;
        }

        var i = 0;

        for (; i < keys.Count - 1; i++)
            if (time < keys[i + 1].Time)
                break;

        var next = (i + 1) % keys.Count;
        var t1 = keys[i].Time;
        var t2 = keys[next].Time;

        if (t2 <= t1) return keys[i].Rotation;

        var factor = (float)((time - t1) / (t2 - t1));

        return Quaternion.Slerp(keys[i].Rotation, keys[next].Rotation, Math.Clamp(factor, 0f, 1f));
    }

    private static Vector3 InterpolateScale(List<(double Time, Vector3 Scale)> keys, double time, Vector3 fallback) {

        switch (keys.Count) {

            case 0: return fallback;
            case 1: return keys[0].Scale;
        }

        var i = 0;

        for (; i < keys.Count - 1; i++)
            if (time < keys[i + 1].Time)
                break;

        var next = (i + 1) % keys.Count;
        var t1 = keys[i].Time;
        var t2 = keys[next].Time;

        if (t2 <= t1) return keys[i].Scale;

        var factor = (float)((time - t1) / (t2 - t1));

        return Vector3.Lerp(keys[i].Scale, keys[next].Scale, Math.Clamp(factor, 0f, 1f));
    }

    public static unsafe void SkinMesh(AssimpMesh mesh, List<BoneInfo> bones) {

        Parallel.For(
            0,
            mesh.Vertices.Length,
            i => {

                var bd = mesh.BoneData[i];

                // Safety: If no bone weights, don't modify the vertex
                var totalWeight = bd.Weight0 + bd.Weight1 + bd.Weight2 + bd.Weight3;

                if (totalWeight < 0.001f) {

                    mesh.AnimatedVertices[i] = mesh.Vertices[i];
                    mesh.AnimatedNormals[i] = mesh.Normals[i];

                    return;
                }

                var v = mesh.Vertices[i];
                var n = mesh.Normals[i];

                var finalV = Vector3.Zero;
                var finalN = Vector3.Zero;

                if (bd.Weight0 > 0) {

                    var m = bones[bd.Bone0].FinalTransformation;
                    finalV += Vector3.Transform(v, m) * bd.Weight0;
                    finalN += Vector3.TransformNormal(n, m) * bd.Weight0;
                }

                if (bd.Weight1 > 0) {

                    var m = bones[bd.Bone1].FinalTransformation;
                    finalV += Vector3.Transform(v, m) * bd.Weight1;
                    finalN += Vector3.TransformNormal(n, m) * bd.Weight1;
                }

                if (bd.Weight2 > 0) {

                    var m = bones[bd.Bone2].FinalTransformation;
                    finalV += Vector3.Transform(v, m) * bd.Weight2;
                    finalN += Vector3.TransformNormal(n, m) * bd.Weight2;
                }

                if (bd.Weight3 > 0) {

                    var m = bones[bd.Bone3].FinalTransformation;
                    finalV += Vector3.Transform(v, m) * bd.Weight3;
                    finalN += Vector3.TransformNormal(n, m) * bd.Weight3;
                }

                mesh.AnimatedVertices[i] = finalV;
                mesh.AnimatedNormals[i] = Vector3.Normalize(finalN);
            }
        );

        fixed (Vector3* v = mesh.AnimatedVertices) Buffer.MemoryCopy(v, mesh.RlMesh.Vertices, (long)mesh.AnimatedVertices.Length * 3 * sizeof(float), (long)mesh.AnimatedVertices.Length * 3 * sizeof(float));

        fixed (Vector3* n = mesh.AnimatedNormals) Buffer.MemoryCopy(n, mesh.RlMesh.Normals, (long)mesh.AnimatedNormals.Length * 3 * sizeof(float), (long)mesh.AnimatedNormals.Length * 3 * sizeof(float));

        Raylib.UpdateMeshBuffer(mesh.RlMesh, 0, mesh.RlMesh.Vertices, mesh.AnimatedVertices.Length * 3 * sizeof(float), 0);
        Raylib.UpdateMeshBuffer(mesh.RlMesh, 2, mesh.RlMesh.Normals, mesh.AnimatedNormals.Length * 3 * sizeof(float), 0);
    }

    private static bool MatricesAreEqual(Matrix4x4 a, Matrix4x4 b) {

        const float epsilon = 0.0001f;

        return Math.Abs(a.M11 - b.M11) < epsilon && Math.Abs(a.M12 - b.M12) < epsilon && Math.Abs(a.M13 - b.M13) < epsilon && Math.Abs(a.M14 - b.M14) < epsilon && Math.Abs(a.M21 - b.M21) < epsilon && Math.Abs(a.M22 - b.M22) < epsilon && Math.Abs(a.M23 - b.M23) < epsilon && Math.Abs(a.M24 - b.M24) < epsilon && Math.Abs(a.M31 - b.M31) < epsilon && Math.Abs(a.M32 - b.M32) < epsilon && Math.Abs(a.M33 - b.M33) < epsilon && Math.Abs(a.M34 - b.M34) < epsilon && Math.Abs(a.M41 - b.M41) < epsilon && Math.Abs(a.M42 - b.M42) < epsilon && Math.Abs(a.M43 - b.M43) < epsilon && Math.Abs(a.M44 - b.M44) < epsilon;
    }

    private static Matrix4x4 ToNumerics(this Assimp.Matrix4x4 m) { return new Matrix4x4(m.A1, m.B1, m.C1, m.D1, m.A2, m.B2, m.C2, m.D2, m.A3, m.B3, m.C3, m.D3, m.A4, m.B4, m.C4, m.D4); }

    private static Vector3 ToNumerics(this Vector3D v) => new(v.X, v.Y, v.Z);
    private static Quaternion ToNumerics(this Assimp.Quaternion q) => new(q.X, q.Y, q.Z, q.W);
}
