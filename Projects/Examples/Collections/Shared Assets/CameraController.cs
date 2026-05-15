using System.Numerics;
using Raylib_cs;

namespace SharedAssets;

internal class CameraController : ScytheScript {
    
    [Config] public float Sensitivity = 0.3f;
    
    public Vector2 TargetRot = Vector2.Zero;
    public Vector3 TargetPos = Vector3.Zero;
    
    [Expose] public Obj? FollowTarget;
    
    public override void Start() {
    
        Raylib.DisableCursor();

        if (FollowTarget != null) TargetPos = FollowTarget.Pos;
    }

    public override void Loop(float dt) {

        var mouseDelta = Raylib.GetMouseDelta();

        CustomPass(dt);
        
        Rotation(mouseDelta);
        if (FollowTarget != null) Movement();
        
        ApplyRotation();
        if (FollowTarget != null) ApplyMovement();
    }
    
    protected virtual void CustomPass(float dt) {}

    protected virtual void Rotation(Vector2 mouseDelta) {
        
        TargetRot.X = Math.Clamp(TargetRot.X + mouseDelta.Y * Sensitivity, -89f, 89f);
        TargetRot.Y -= mouseDelta.X * Sensitivity;
    }
    
    protected virtual void Movement() => TargetPos = FollowTarget.Pos;

    protected virtual void ApplyRotation() {
        
        Rot = Raymath.QuaternionFromEuler(
            
            TargetRot.X * (float)(Math.PI / 180.0),
            TargetRot.Y * (float)(Math.PI / 180.0), 
            0
        );
    }
    
    protected virtual void ApplyMovement() => Pos = TargetPos;
}