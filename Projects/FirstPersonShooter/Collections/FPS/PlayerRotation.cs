using System.Numerics;
using Raylib_cs;

internal class PlayerRotation : ScytheScript {
    
    private Vector2 rot = Vector2.Zero;
    [Config]
    private float sensitivity = 0.3f;
    
    private Obj? _pivot;
    private Obj? _player;

    public override void Start() {
        Raylib.DisableCursor();

        _pivot  = Parent;
        _player = _pivot?.Parent;
        SetParent(Core.ActiveLevel?.Root, keepWorld: true);
    }

    public override void Loop(float dt) {
        Vector2 mouseDelta = Raylib.GetMouseDelta();

        rot.X = Math.Clamp(rot.X + mouseDelta.Y * sensitivity, -89f, 89f);
        rot.Y = rot.Y - mouseDelta.X * sensitivity;

        Rot = Raymath.QuaternionFromEuler(
            rot.X * (float)(Math.PI / 180.0),
            rot.Y * (float)(Math.PI / 180.0),
            0
        );

        if (_player != null) {
            _player.Transform.Rot = Raymath.QuaternionFromEuler(0, rot.Y * (float)(Math.PI / 180.0), 0);
        }

        if (_pivot != null) {
            Pos = Raymath.Vector3Lerp(Pos, _pivot.Transform.WorldPos, dt * 15f);
        }
    }
}
