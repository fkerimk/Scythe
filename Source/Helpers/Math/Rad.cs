internal static partial class Extensions {

    private const float Deg2Rad = MathF.PI / 180.0f;
    private const float Rad2deg = 180.0f / MathF.PI;

    extension(float value) {

        public float DegToRad() { return value * Deg2Rad; }
        public float RadToDeg() { return value * Rad2deg; }
    }
}