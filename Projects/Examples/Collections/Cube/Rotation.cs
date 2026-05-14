using System.Numerics;
using Raylib_cs;

namespace Cube;

internal class Rotation : ScytheScript {

    public override void Loop(float dt) {
        
        Transform.Pos = new Vector3(0, (float)Math.Sin(Raylib.GetTime() * 2.5) * 0.35f + 0.5f, 0);
        Transform.RotateY(dt * 50f);
    }
}