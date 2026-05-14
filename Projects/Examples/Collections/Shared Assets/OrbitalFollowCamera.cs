using System.Numerics;
using Raylib_cs;

namespace SharedAssets;

internal class OrbitalFollowCamera : CameraController {
    
    private float _distance = 5f;
    private float _smoothDistance = 5f;
    
    private Vector3 _smoothTargetPos = Vector3.Zero;
    
    public override void Start() {
        
        base.Start();

        FollowTarget = Obj.Parent!.Find("Dwarf")!;
        _smoothTargetPos = FollowTarget.Pos;
    }

    protected override void CustomPass(float dt) {
        
        _distance = Math.Clamp(_distance - Math.Sign(Raylib.GetMouseWheelMove()), 1f, 10f);
        _smoothDistance = Raymath.Lerp(_smoothDistance, _distance, dt * 15f);
        
        _smoothTargetPos = Vector3.Lerp(_smoothTargetPos, FollowTarget.Pos, dt * 5f);
    }
    
    protected override void Rotation(Vector2 mouseDelta) {
        
        TargetRot.X = Math.Clamp(TargetRot.X + mouseDelta.Y * Sensitivity, 5f, 60f);
        TargetRot.Y -= mouseDelta.X * Sensitivity;
    }

    protected override void Movement() {
        
        TargetPos = _smoothTargetPos + Vector3.UnitY * 0.5f - Fwd * _smoothDistance;
    }
}