using System.Numerics;
using Raylib_cs;

namespace FPS;

internal class PlayerMovement : ScytheScript {
    
    [Expose] private Rigidbody? _rigidbody;
    [Expose] private float _speed = 3f;

    public override void Loop(float dt) {
        Vector2 moveInput = new Vector2(
            (Raylib.IsKeyDown(KeyboardKey.D) ? 1f : 0f) - (Raylib.IsKeyDown(KeyboardKey.A) ? 1f : 0f),
            (Raylib.IsKeyDown(KeyboardKey.W) ? 1f : 0f) - (Raylib.IsKeyDown(KeyboardKey.S) ? 1f : 0f)
        );

        var cam = FindFirstCameraComponent(Core.ActiveLevel?.Root);
        if (cam == null || _rigidbody == null) return;
        
        Vector3 rightFlat = cam.Obj.RightFlat;
        Vector3 fwdFlat = cam.Obj.FwdFlat;

        Vector3 vel = -moveInput.X * _speed * rightFlat
                    + moveInput.Y * _speed * fwdFlat
                    + Vector3.UnitY * _rigidbody.Velocity.Y;

        _rigidbody.Velocity = vel;
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
