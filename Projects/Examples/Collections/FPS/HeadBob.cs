using System.Numerics;
using Raylib_cs;

namespace FPS;

internal class HeadBob : ScytheScript {
    
    [Expose] private float speed = 7.5f;
    [Expose] private float power = 0.1f;

    public override void Loop(float dt) {
        
        var targetPos = Vector3.Zero;

        if (Raylib.IsKeyDown(KeyboardKey.W) ||
            Raylib.IsKeyDown(KeyboardKey.A) || 
            Raylib.IsKeyDown(KeyboardKey.S) ||
            Raylib.IsKeyDown(KeyboardKey.D)) {
            
            targetPos = new Vector3(
                
                (float)Math.Sin(Raylib.GetTime() * speed * 0.5f) * power, 
                (float)Math.Sin(Raylib.GetTime() * speed) * power, 
                0
            );
        }

        Transform.Pos = Raymath.Vector3Lerp(Transform.Pos, targetPos, dt * 5f);
    }
}
