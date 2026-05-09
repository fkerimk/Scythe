using System.Numerics;
using Raylib_cs;

internal static partial class Extensions {

    public static Vector4 ToVector4(this Color color) => new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
}