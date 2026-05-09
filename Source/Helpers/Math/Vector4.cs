using System.Numerics;
using Raylib_cs;

internal static partial class Extensions {

    public static Color ToColor(this Vector4 color) => new(color.X, color.Y, color.Z, color.W);
}