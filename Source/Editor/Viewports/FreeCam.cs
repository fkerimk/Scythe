using System.Numerics;
using Raylib_cs;

internal static class FreeCam {

    private const float Sens = 0.003f;
    private const float Clamp = 1.55f;
    private const float Speed = 15;

    public static Vector3 Pos {
        get => _pos;
        set => _pos = _lerpedPos = value;
    }

    public static Vector2 Rot {
        get => _rot;
        set {
            _rot = value;
            UpdateForward();
        }
    }

    private static Vector3 _pos;
    private static Vector3 _lerpedPos;
    private static Vector3 _forward;

    private static Vector2 _rot;

    private static bool _isLocked;
    private static Vector2 _lockPos;

    public static void Loop(Viewport viewport) {

        if (Core.ActiveCamera == null) return;

        var center = viewport.WindowPos + viewport.ContentRegion / 2;

        if (viewport.IsHovered && Raylib.IsMouseButtonPressed(MouseButton.Right)) {

            _isLocked = true;
            _lockPos = center;

            Raylib.DisableCursor();
            ImGuiNET.ImGui.SetWindowFocus(null);
        }

        if (_isLocked && Raylib.IsMouseButtonReleased(MouseButton.Right)) {

            Raylib.EnableCursor();
            Raylib.SetMousePosition((int)_lockPos.X, (int)_lockPos.Y);

            _isLocked = false;
        }

        _lerpedPos = Raymath.Vector3Lerp(_lerpedPos, _pos, Raylib.GetFrameTime() * 15);

        Core.ActiveCamera.Position = _lerpedPos;
        Core.ActiveCamera.Target = _lerpedPos + _forward;

        if (!_isLocked) return;

        Movement();
        Rotation();

        Raylib.SetMousePosition((int)_lockPos.X, (int)_lockPos.Y);
    }

    private static void Rotation() {

        var input = Raylib.GetMouseDelta();

        _rot -= new Vector2(input.Y * Sens, input.X * Sens);
        _rot.X = Raymath.Clamp(_rot.X, -Clamp, Clamp);

        UpdateForward();
    }

    private static void UpdateForward() { _forward = new Vector3(MathF.Cos(_rot.X) * MathF.Sin(_rot.Y), MathF.Sin(_rot.X), MathF.Cos(_rot.X) * MathF.Cos(_rot.Y)); }

    private static void Movement() {

        if (Core.ActiveCamera == null) return;

        var input = Vector3.Zero;

        if (Raylib.IsKeyDown(KeyboardKey.W)) input.Z += 1;
        if (Raylib.IsKeyDown(KeyboardKey.S)) input.Z -= 1;
        if (Raylib.IsKeyDown(KeyboardKey.D)) input.X += 1;
        if (Raylib.IsKeyDown(KeyboardKey.A)) input.X -= 1;
        if (Raylib.IsKeyDown(KeyboardKey.E)) input.Y += 1;
        if (Raylib.IsKeyDown(KeyboardKey.Q)) input.Y -= 1;

        _pos += (Core.ActiveCamera.Up * input.Y + Core.ActiveCamera.Right * input.X + Core.ActiveCamera.Fwd * input.Z) * Speed * Raylib.GetFrameTime();
    }

    private static void SetFromTarget(Vector3 pos, Vector3 target) {

        if (Core.ActiveCamera == null) return;

        var dir = Raymath.Vector3Normalize(target - pos);

        var vertical = MathF.Asin(dir.Y);
        var horizontal = MathF.Atan2(dir.X, dir.Z);

        _lerpedPos = _pos = Core.ActiveCamera.Position;
        _rot = new Vector2(vertical, horizontal);

        _forward = new Vector3(MathF.Cos(_rot.X) * MathF.Sin(_rot.Y), MathF.Sin(_rot.X), MathF.Cos(_rot.X) * MathF.Cos(_rot.Y));
    }

    public static void SetFromTarget(Camera3D? camera) => SetFromTarget(camera?.Position ?? Vector3.Zero, camera?.Target ?? Vector3.Zero);
}