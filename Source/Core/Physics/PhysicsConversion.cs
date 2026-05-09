using System.Numerics;
using Jitter2.LinearMath;

internal static class Conversion {

    public static JVector ToJitter(Vector3 v) => new(v.X, v.Y, v.Z);
    public static Vector3 FromJitter(JVector v) => new(v.X, v.Y, v.Z);

    public static JQuaternion ToJitter(Quaternion q) => new(q.X, q.Y, q.Z, q.W);
    public static Quaternion FromJitter(JQuaternion jq) => new(jq.X, jq.Y, jq.Z, jq.W);
}