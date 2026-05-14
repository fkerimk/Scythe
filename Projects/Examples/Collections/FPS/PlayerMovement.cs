using System.Numerics;
using Raylib_cs;

internal class PlayerMovement : ScytheScript {
    
    private Rigidbody? _rb;
    [Expose]
    private float speed = 3f;

    public override void Start() {
        _rb = GetComponent<Rigidbody>();
    }

    public override void Loop(float dt) {
        Vector2 moveInput = new Vector2(
            (Raylib.IsKeyDown(KeyboardKey.D) ? 1f : 0f) - (Raylib.IsKeyDown(KeyboardKey.A) ? 1f : 0f),
            (Raylib.IsKeyDown(KeyboardKey.W) ? 1f : 0f) - (Raylib.IsKeyDown(KeyboardKey.S) ? 1f : 0f)
        );

        var cam = FindFirstCameraComponent(Core.ActiveLevel?.Root);
        if (cam == null || _rb == null) return;
        
        Vector3 rightFlat = cam.Obj.RightFlat;
        Vector3 fwdFlat = cam.Obj.FwdFlat;

        Vector3 vel = -moveInput.X * speed * rightFlat
                    + moveInput.Y * speed * fwdFlat
                    + Vector3.UnitY * _rb.Velocity.Y;

        _rb.Velocity = vel;
    }
    
    private Camera? FindFirstCameraComponent(Obj? obj) {
        if (obj == null) return null;
        foreach (var c in obj.Components.Values) if (c is Camera found) return found;
        foreach (var child in obj.Children.Values) {
            var cam = FindFirstCameraComponent(child);
            if (cam != null) return cam;
        }
        return null;
    }
}
