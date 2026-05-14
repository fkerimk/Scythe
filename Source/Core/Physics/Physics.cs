using System.Numerics;
using Jitter2;
using Jitter2.Dynamics;
using static Raylib_cs.Raylib;

internal static class Physics {

    public static World World { get; private set; } = null!;

    public static void Init() {

        foreach (var rigidbody in RegisteredBodies.ToArray())
            rigidbody.OnPhysicsWorldReset();

        RegisteredBodies.Clear();
        RegisteredWorldBodies.Clear();
        _accumulator = 0;
        World = new World();
        World.Gravity = new Vector3(0, -9.81f, 0);
    }

    private static readonly HashSet<Rigidbody> RegisteredBodies = [];
    private static readonly HashSet<RigidBody> RegisteredWorldBodies = [];
    private static float _accumulator;
    private const float TimeStep = 1.0f / 60.0f;

    public static void Register(Rigidbody rb) => RegisteredBodies.Add(rb);
    public static void Unregister(Rigidbody rb) => RegisteredBodies.Remove(rb);
    public static void TrackBody(RigidBody body) => RegisteredWorldBodies.Add(body);

    public static void TryRemoveBody(RigidBody? body) {

        if (body == null) return;
        if (!RegisteredWorldBodies.Remove(body)) return;

        World.Remove(body);
    }

    public static void Update() {

        var dt = GetFrameTime();

        switch (dt) {

            case <= 0: return;
            case > 0.25f:
                dt = 0.25f; // Cap dt to avoid "spiral of death"

                break;
        }

        _accumulator += dt;

        while (_accumulator >= TimeStep) {

            World.Step(TimeStep);
            _accumulator -= TimeStep;
        }
    }
}
