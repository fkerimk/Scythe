using System;
using System.Numerics;
using Raylib_cs;

internal class Player : ScytheScript {

    private float moveSpeed = 3f;
    private float sensitivity = 0.3f;
    private Quaternion rotTarget = Quaternion.Identity;
    private Vector2 camRotTarget = Vector2.Zero;
    private float camDistance = 5f;
    private float smoothCamDistance = 5f;

    private Animation anim = null!;
    private Rigidbody rb = null!;
    private Obj camObj = null!;
    private bool _initialized;

    public override void Loop(float dt) {
        if (!_initialized) {
            anim = (Animation)Obj.FindComponent("Animation")!;
            rb = (Rigidbody)Obj.FindComponent("Rigidbody")!;
            camObj = Obj.Parent!.Find("Camera")!;
            Raylib.DisableCursor();
            _initialized = true;
        }

        Camera(dt);
        Movement(dt);

        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) {
            Runtime.Quit(); 
        }
    }

    private void Movement(float dt) {
        var moveInput = new Vector2(
            (Raylib.IsKeyDown(KeyboardKey.D) ? 1 : 0) - (Raylib.IsKeyDown(KeyboardKey.A) ? 1 : 0),
            (Raylib.IsKeyDown(KeyboardKey.W) ? 1 : 0) - (Raylib.IsKeyDown(KeyboardKey.S) ? 1 : 0)
        );

        if (moveInput != Vector2.Zero) {
            float forwardRad = MathF.Atan2(camObj.FwdFlat.X, camObj.FwdFlat.Z);
            float inputAngle = MathF.Atan2(-moveInput.X, moveInput.Y);
            var qBase = Quaternion.CreateFromAxisAngle(Vector3.UnitY, forwardRad);
            var qDelta = Quaternion.CreateFromAxisAngle(Vector3.UnitY, inputAngle); 
            rotTarget = qBase * qDelta; 
        }

        var vel = -moveInput.X * moveSpeed * camObj.RightFlat
                + moveInput.Y * moveSpeed * camObj.FwdFlat
                + Vector3.UnitY * rb.Velocity.Y;

        rb.Velocity = vel;

        Transform.Rot = Quaternion.Slerp(Transform.Rot, rotTarget, dt * 15f);

        int track = 5;
        if (moveInput != Vector2.Zero) track = 6;
        if (vel.Y < -0.5f) track = 3;

        anim.Track = track;
    }

    private void Camera(float dt) {
        var delta = Raylib.GetMouseDelta();
        camRotTarget = new Vector2(
            Math.Clamp(camRotTarget.X + delta.Y * sensitivity, 5f, 60f),
            camRotTarget.Y + delta.X * sensitivity
        );

        camDistance = Math.Clamp(camDistance - Math.Sign(Raylib.GetMouseWheelMove()), 1f, 10f);
        smoothCamDistance = Raymath.Lerp(smoothCamDistance, camDistance, dt * 15f);

        camObj.Transform.Euler = new Vector3(camRotTarget.X, camRotTarget.Y, 0);
        camObj.Pos = Transform.Pos + Vector3.UnitY * 0.5f - camObj.Fwd * smoothCamDistance;
    }
}
