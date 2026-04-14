using System.Numerics;
using Raylib_cs;

internal class HeadBob : ScytheScript {
    
    private float speed = 7.5f;
    private float power = 0.1f;

    public override void Loop(float dt) {
        Vector3 targetPos = Vector3.Zero;

        if (Raylib.IsKeyDown(KeyboardKey.D) || Raylib.IsKeyDown(KeyboardKey.A) || 
            Raylib.IsKeyDown(KeyboardKey.W) || Raylib.IsKeyDown(KeyboardKey.S)) {
            targetPos = new Vector3(
                (float)Math.Sin(Raylib.GetTime() * speed * 0.5f) * power, 
                (float)Math.Sin(Raylib.GetTime() * speed) * power, 
                0
            );
        }

        Transform.Pos = Raymath.Vector3Lerp(Transform.Pos, targetPos, dt * 5f);
    }
}
