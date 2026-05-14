using System.Numerics;
using Raylib_cs;
using SharedAssets;

internal class PlayerRotation : ScytheScript {
    
    private CameraController cameraController;

    private Vector3 offset;
    
    public override void Start() {
        
        Raylib.DisableCursor();

        cameraController = GetChildAt(0).GetChildAt(0).GetComponent<CameraController>();
        cameraController.SetParent(Core.ActiveLevel?.Root, keepWorld: true);
        
        offset = cameraController.Pos - Pos;
    }

    public override void Loop(float dt) {
        
        Transform.Rot = Raymath.QuaternionFromEuler(0, cameraController.TargetRot.Y * (float)(Math.PI / 180.0), 0);
        cameraController.Pos = Raymath.Vector3Lerp(cameraController.Pos, Pos + offset, dt * 15f);
    }
}
