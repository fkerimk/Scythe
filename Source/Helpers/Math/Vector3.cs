using System.Numerics;

internal static partial class Extensions {

    extension(Vector3 value) {

        public Vector3 ToRad() { return new Vector3(value.X.DegToRad(), value.Y.DegToRad(), value.Z.DegToRad()); }
        public Vector3 ToDeg() { return new Vector3(value.X.RadToDeg(), value.Y.RadToDeg(), value.Z.RadToDeg()); }
    }
}