using System.Numerics;
using Raylib_cs;
using static Raylib_cs.Raylib;

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
    public Mesh RlMesh;
    public int MaterialIndex;
    public bool UsesSkinning;

    public Vector3[] AnimatedVertices = null!;
    public Vector3[] AnimatedNormals = null!;

    public AssimpMesh Clone() =>
        new() {
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
            RlMesh = CreateUploadedMesh(Vertices, Normals, TexCoords, Indices)
        };

    public static unsafe Mesh CreateUploadedMesh(Vector3[] vertices, Vector3[] normals, Vector2[] texCoords, uint[] indices) {

        var mesh = new Mesh {
            VertexCount = vertices.Length,
            TriangleCount = indices.Length / 3,
            Vertices = (float*)MemAlloc((uint)(vertices.Length * 3 * sizeof(float))),
            Normals = (float*)MemAlloc((uint)(normals.Length * 3 * sizeof(float))),
            TexCoords = (float*)MemAlloc((uint)(texCoords.Length * 2 * sizeof(float))),
            Indices = (ushort*)MemAlloc((uint)(indices.Length * sizeof(ushort)))
        };

        fixed (Vector3* v = vertices)
            Buffer.MemoryCopy(v, mesh.Vertices, (long)vertices.Length * 3 * sizeof(float), (long)vertices.Length * 3 * sizeof(float));
        fixed (Vector3* n = normals)
            Buffer.MemoryCopy(n, mesh.Normals, (long)normals.Length * 3 * sizeof(float), (long)normals.Length * 3 * sizeof(float));
        fixed (Vector2* t = texCoords)
            Buffer.MemoryCopy(t, mesh.TexCoords, (long)texCoords.Length * 2 * sizeof(float), (long)texCoords.Length * 2 * sizeof(float));

        for (var i = 0; i < indices.Length; i++)
            mesh.Indices[i] = (ushort)indices[i];

        GenMeshTangents(ref mesh);
        UploadMesh(ref mesh, false);
        return mesh;
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
    public int SourceTrack;
    public double StartFrame;
    public double EndFrame;
    public bool Loop = true;
    public double Duration;
    public double TicksPerSecond;
    public readonly List<AnimationChannel> Channels = [];
    public readonly Dictionary<string, AnimationChannel> ChannelMap = [];
}

internal static partial class AssimpLoader {

    public static AnimationClip CreateClipSegment(AnimationClip source, string name, int sourceTrack, double startFrame, double endFrame, bool loop) {

        var clampedStart = Math.Clamp(startFrame, 0d, source.Duration);
        var clampedEnd = Math.Clamp(endFrame, clampedStart, source.Duration);
        var clip = new AnimationClip {
            Name = name,
            SourceTrack = sourceTrack,
            StartFrame = clampedStart,
            EndFrame = clampedEnd,
            Loop = loop,
            Duration = clampedEnd - clampedStart,
            TicksPerSecond = source.TicksPerSecond
        };

        foreach (var sourceChannel in source.Channels) {
            var channel = new AnimationChannel { NodeName = sourceChannel.NodeName };
            AddVector3Keys(channel.PositionKeys, sourceChannel.PositionKeys, clampedStart, clampedEnd, value => value);
            AddQuaternionKeys(channel.RotationKeys, sourceChannel.RotationKeys, clampedStart, clampedEnd, value => value);
            AddVector3Keys(channel.ScaleKeys, sourceChannel.ScaleKeys, clampedStart, clampedEnd, value => value);
            clip.Channels.Add(channel);
            clip.ChannelMap[channel.NodeName] = channel;
        }

        return clip;
    }

    private static void AddVector3Keys(List<(double Time, Vector3 Value)> target, List<(double Time, Vector3 Value)> source, double start, double end, Func<Vector3, Vector3> map) {

        if (source.Count == 0) return;

        target.Add((0d, map(InterpolateVector3(source, start))));

        foreach (var (time, value) in source) {
            if (time <= start || time >= end) continue;
            target.Add((time - start, map(value)));
        }

        var localEnd = end - start;
        if (localEnd > 0d)
            target.Add((localEnd, map(InterpolateVector3(source, end))));
    }

    private static void AddQuaternionKeys(List<(double Time, Quaternion Value)> target, List<(double Time, Quaternion Value)> source, double start, double end, Func<Quaternion, Quaternion> map) {

        if (source.Count == 0) return;

        target.Add((0d, map(InterpolateQuaternion(source, start))));

        foreach (var (time, value) in source) {
            if (time <= start || time >= end) continue;
            target.Add((time - start, map(value)));
        }

        var localEnd = end - start;
        if (localEnd > 0d)
            target.Add((localEnd, map(InterpolateQuaternion(source, end))));
    }

    private static Vector3 InterpolateVector3(List<(double Time, Vector3 Value)> keys, double time) {

        if (keys.Count == 1) return keys[0].Value;
        if (time <= keys[0].Time) return keys[0].Value;
        if (time >= keys[^1].Time) return keys[^1].Value;

        for (var i = 0; i < keys.Count - 1; i++) {
            var current = keys[i];
            var next = keys[i + 1];
            if (time < current.Time || time > next.Time) continue;
            var length = next.Time - current.Time;
            var factor = length <= double.Epsilon ? 0f : (float)((time - current.Time) / length);
            return Vector3.Lerp(current.Value, next.Value, factor);
        }

        return keys[^1].Value;
    }

    private static Quaternion InterpolateQuaternion(List<(double Time, Quaternion Value)> keys, double time) {

        if (keys.Count == 1) return keys[0].Value;
        if (time <= keys[0].Time) return keys[0].Value;
        if (time >= keys[^1].Time) return keys[^1].Value;

        for (var i = 0; i < keys.Count - 1; i++) {
            var current = keys[i];
            var next = keys[i + 1];
            if (time < current.Time || time > next.Time) continue;
            var length = next.Time - current.Time;
            var factor = length <= double.Epsilon ? 0f : (float)((time - current.Time) / length);
            return Quaternion.Normalize(Quaternion.Slerp(current.Value, next.Value, factor));
        }

        return keys[^1].Value;
    }

    public static void UpdateAnimation(ModelNode node, AnimationClip clip, double time, in Matrix4x4 parentTransform, in Matrix4x4 globalInverse, Dictionary<string, List<BoneInfo>> boneMap) {

        var nodeTransform = node.Transformation;

        if (clip.ChannelMap.TryGetValue(node.Name, out var channel))
            nodeTransform = GetInterpolatedTransform(channel, time, node.Transformation);

        var globalTransform = nodeTransform * parentTransform;

        if (boneMap.TryGetValue(node.Name, out var bones))
            foreach (var bone in bones)
                bone.FinalTransformation = bone.Offset * globalTransform * globalInverse;

        foreach (var child in node.Children)
            UpdateAnimation(child, clip, time, globalTransform, globalInverse, boneMap);
    }

    public static void ApplyBindPose(ModelNode node, in Matrix4x4 parentTransform, in Matrix4x4 globalInverse, Dictionary<string, List<BoneInfo>> boneMap) {

        var globalTransform = node.Transformation * parentTransform;

        if (boneMap.TryGetValue(node.Name, out var bones))
            foreach (var bone in bones)
                bone.FinalTransformation = bone.Offset * globalTransform * globalInverse;

        foreach (var child in node.Children)
            ApplyBindPose(child, globalTransform, globalInverse, boneMap);
    }

    public static void UpdateAnimationBlended(ModelNode node, AnimationClip clipA, double timeA, AnimationClip clipB, double timeB, float blend, in Matrix4x4 parentTransform, in Matrix4x4 globalInverse, Dictionary<string, List<BoneInfo>> boneMap) {

        var nodeTransform = GetBlendedTransform(
            clipA.ChannelMap.GetValueOrDefault(node.Name),
            timeA,
            clipB.ChannelMap.GetValueOrDefault(node.Name),
            timeB,
            blend,
            node.Transformation
        );

        var globalTransform = nodeTransform * parentTransform;

        if (boneMap.TryGetValue(node.Name, out var bones))
            foreach (var bone in bones)
                bone.FinalTransformation = bone.Offset * globalTransform * globalInverse;

        foreach (var child in node.Children)
            UpdateAnimationBlended(child, clipA, timeA, clipB, timeB, blend, globalTransform, globalInverse, boneMap);
    }

    public static unsafe void SkinMesh(AssimpMesh mesh, List<BoneInfo> bones) {

        Parallel.For(0, mesh.Vertices.Length, i => {
            var boneData = mesh.BoneData[i];
            var totalWeight = boneData.Weight0 + boneData.Weight1 + boneData.Weight2 + boneData.Weight3;

            if (totalWeight < 0.001f) {
                mesh.AnimatedVertices[i] = mesh.Vertices[i];
                mesh.AnimatedNormals[i] = mesh.Normals[i];
                return;
            }

            var vertex = mesh.Vertices[i];
            var normal = mesh.Normals[i];
            var finalVertex = Vector3.Zero;
            var finalNormal = Vector3.Zero;

            AccumulateWeight(boneData.Bone0, boneData.Weight0);
            AccumulateWeight(boneData.Bone1, boneData.Weight1);
            AccumulateWeight(boneData.Bone2, boneData.Weight2);
            AccumulateWeight(boneData.Bone3, boneData.Weight3);

            mesh.AnimatedVertices[i] = finalVertex;
            mesh.AnimatedNormals[i] = Vector3.Normalize(finalNormal);

            void AccumulateWeight(int boneIndex, float weight) {
                if (weight <= 0) return;

                var matrix = bones[boneIndex].FinalTransformation;
                finalVertex += Vector3.Transform(vertex, matrix) * weight;
                finalNormal += Vector3.TransformNormal(normal, matrix) * weight;
            }
        });

        fixed (Vector3* v = mesh.AnimatedVertices)
            Buffer.MemoryCopy(v, mesh.RlMesh.Vertices, (long)mesh.AnimatedVertices.Length * 3 * sizeof(float), (long)mesh.AnimatedVertices.Length * 3 * sizeof(float));
        fixed (Vector3* n = mesh.AnimatedNormals)
            Buffer.MemoryCopy(n, mesh.RlMesh.Normals, (long)mesh.AnimatedNormals.Length * 3 * sizeof(float), (long)mesh.AnimatedNormals.Length * 3 * sizeof(float));

        UpdateMeshBuffer(mesh.RlMesh, 0, mesh.RlMesh.Vertices, mesh.AnimatedVertices.Length * 3 * sizeof(float), 0);
        UpdateMeshBuffer(mesh.RlMesh, 2, mesh.RlMesh.Normals, mesh.AnimatedNormals.Length * 3 * sizeof(float), 0);
    }

    private static Matrix4x4 GetInterpolatedTransform(AnimationChannel channel, double time, Matrix4x4 bindPose) {

        Matrix4x4.Decompose(bindPose, out var bindScale, out var bindRotation, out var bindPosition);
        var position = InterpolatePosition(channel.PositionKeys, time, bindPosition);
        var rotation = InterpolateRotation(channel.RotationKeys, time, bindRotation);
        var scale = InterpolateScale(channel.ScaleKeys, time, bindScale);

        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
    }

    private static Matrix4x4 GetBlendedTransform(AnimationChannel? channelA, double timeA, AnimationChannel? channelB, double timeB, float blend, Matrix4x4 bindPose) {

        Matrix4x4.Decompose(bindPose, out var bindScale, out var bindRotation, out var bindPosition);

        var positionA = channelA != null ? InterpolatePosition(channelA.PositionKeys, timeA, bindPosition) : bindPosition;
        var rotationA = channelA != null ? InterpolateRotation(channelA.RotationKeys, timeA, bindRotation) : bindRotation;
        var scaleA = channelA != null ? InterpolateScale(channelA.ScaleKeys, timeA, bindScale) : bindScale;

        var positionB = channelB != null ? InterpolatePosition(channelB.PositionKeys, timeB, bindPosition) : bindPosition;
        var rotationB = channelB != null ? InterpolateRotation(channelB.RotationKeys, timeB, bindRotation) : bindRotation;
        var scaleB = channelB != null ? InterpolateScale(channelB.ScaleKeys, timeB, bindScale) : bindScale;

        var position = Vector3.Lerp(positionA, positionB, blend);
        var rotation = Quaternion.Slerp(rotationA, rotationB, blend);
        var scale = Vector3.Lerp(scaleA, scaleB, blend);

        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
    }

    private static Vector3 InterpolatePosition(List<(double Time, Vector3 Position)> keys, double time, Vector3 fallback) =>
        keys.Count switch {
            0 => fallback,
            1 => keys[0].Position,
            _ => Vector3.Lerp(keys[FindKeyIndex(keys, time)].Position, keys[FindNextKeyIndex(keys, time)].Position, GetBlendFactor(keys, time))
        };

    private static Quaternion InterpolateRotation(List<(double Time, Quaternion Rotation)> keys, double time, Quaternion fallback) =>
        keys.Count switch {
            0 => fallback,
            1 => keys[0].Rotation,
            _ => Quaternion.Slerp(keys[FindKeyIndex(keys, time)].Rotation, keys[FindNextKeyIndex(keys, time)].Rotation, GetBlendFactor(keys, time))
        };

    private static Vector3 InterpolateScale(List<(double Time, Vector3 Scale)> keys, double time, Vector3 fallback) =>
        keys.Count switch {
            0 => fallback,
            1 => keys[0].Scale,
            _ => Vector3.Lerp(keys[FindKeyIndex(keys, time)].Scale, keys[FindNextKeyIndex(keys, time)].Scale, GetBlendFactor(keys, time))
        };

    private static int FindKeyIndex<T>(List<(double Time, T Value)> keys, double time) {

        for (var i = 0; i < keys.Count - 1; i++)
            if (time < keys[i + 1].Time)
                return i;

        return keys.Count - 1;
    }

    private static int FindNextKeyIndex<T>(List<(double Time, T Value)> keys, double time) {

        var index = FindKeyIndex(keys, time);
        return (index + 1) % keys.Count;
    }

    private static float GetBlendFactor<T>(List<(double Time, T Value)> keys, double time) {

        var index = FindKeyIndex(keys, time);
        var nextIndex = (index + 1) % keys.Count;
        var currentTime = keys[index].Time;
        var nextTime = keys[nextIndex].Time;

        if (nextTime <= currentTime) return 0f;
        return Math.Clamp((float)((time - currentTime) / (nextTime - currentTime)), 0f, 1f);
    }
}
