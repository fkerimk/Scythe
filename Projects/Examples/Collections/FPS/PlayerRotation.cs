using System.Numerics;
using Raylib_cs;
using SharedAssets;

namespace FPS;

internal class PlayerRotation : ScytheScript {
    
    [Expose] private CameraController _cameraController;

    private Vector3 _offset;
    
    public override void Start() {
        
        Raylib.DisableCursor();

        _cameraController = GetChildAt(0).GetChildAt(0).GetComponent<CameraController>();
        _cameraController.SetParent(Core.ActiveLevel?.Root, keepWorld: true);
        
        _offset = _cameraController.Pos - Pos;
    }

    public override void Loop(float dt) {
        
        Transform.Rot = Raymath.QuaternionFromEuler(0, _cameraController.TargetRot.Y * (float)(Math.PI / 180.0), 0);
        _cameraController.Pos = Raymath.Vector3Lerp(_cameraController.Pos, Pos + _offset, dt * 15f);
    }
}
