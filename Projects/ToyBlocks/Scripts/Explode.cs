using System;
using System.Numerics;
using Raylib_cs;

internal class Explode : ScytheScript {

    private Rigidbody rb = null!;
    private Vector3 forceDir;
    private bool _initialized;

    public override void Loop(float dt) {
        if (!_initialized) {
            rb = (Rigidbody)FindComponent("Rigidbody")!;
            var rand = new Random();
            forceDir = new Vector3((float)rand.NextDouble(), (float)rand.NextDouble(), (float)rand.NextDouble());
            _initialized = true;
        }

        if (Raylib.GetTime() < 3.5) return;
        rb.Velocity += forceDir * 0.25f;
    }
}
