using System.Numerics;
using Raylib_cs;

namespace Dwarf;

internal class Dwarf : ScytheScript {
    
    private const float MoveSpeed = 3f;
    
    private Quaternion _rotTarget = Quaternion.Identity;

    private Animation _anim = null!;
    private Rigidbody _rb = null!;
    private Obj _camObj = null!;
    
    private bool _initialized;
    
    public override void Start() {
        
        Raylib.DisableCursor();
    }

    public override void Loop(float dt) {
        
        if (!_initialized) {
            
            _anim = (Animation)Obj.FindComponent("Animation")!;
            _rb = (Rigidbody)Obj.FindComponent("Rigidbody")!;
            _camObj = Root.Find("Camera")!;
            _initialized = true;
        }

        Movement(dt);

        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) Runtime.Quit(); 
    }

    private void Movement(float dt) {
        
        var moveInput = new Vector2(
            
            (Raylib.IsKeyDown(KeyboardKey.D) ? 1 : 0) - (Raylib.IsKeyDown(KeyboardKey.A) ? 1 : 0),
            (Raylib.IsKeyDown(KeyboardKey.W) ? 1 : 0) - (Raylib.IsKeyDown(KeyboardKey.S) ? 1 : 0)
        );

        if (moveInput != Vector2.Zero) {
            
            var forwardRad = MathF.Atan2(_camObj.FwdFlat.X, _camObj.FwdFlat.Z);
            var inputAngle = MathF.Atan2(-moveInput.X, moveInput.Y);
            var qBase = Quaternion.CreateFromAxisAngle(Vector3.UnitY, forwardRad);
            var qDelta = Quaternion.CreateFromAxisAngle(Vector3.UnitY, inputAngle); 
            _rotTarget = qBase * qDelta; 
        }

        var vel = -moveInput.X * MoveSpeed * _camObj.RightFlat
                + moveInput.Y * MoveSpeed * _camObj.FwdFlat
                + Vector3.UnitY * _rb.Velocity.Y;

        _rb.Velocity = vel;

        Transform.Rot = Quaternion.Slerp(Transform.Rot, _rotTarget, dt * 15f);

        var track = 5;
        if (moveInput != Vector2.Zero) track = 6;
        if (vel.Y < -0.5f) track = 3;

        _anim.Track = track;
    }
}
