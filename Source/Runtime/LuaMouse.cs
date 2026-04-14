using System.Numerics;
using Raylib_cs;
using static Raylib_cs.Raylib;

internal class LuaMouse {

    public static bool IsLocked;

    public static Vector2 Delta;
    public static float Scroll => GetMouseWheelMove();

    public static void Loop() {

        // Handle ESC to unlock
        if (!CommandLine.Runtime && IsLocked && IsKeyPressed(KeyboardKey.Escape)) {
            IsLocked = false;
        }

        if (!IsWindowFocused()) {

            if (IsCursorHidden()) {
                EnableCursor();
                ShowCursor();
            }

            Delta = Vector2.Zero;

            return;
        }

        Delta = GetMouseDelta();

        if (IsLocked) {

            if (!IsCursorHidden()) DisableCursor();

            // Force center into viewport to prevent escaping
            if (!CommandLine.Runtime) {

                var center = Editor.RuntimeRender.ScreenPos + Editor.RuntimeRender.TexSize / 2f;
                SetMousePosition((int)center.X, (int)center.Y);
            }
        } else {

            if (IsCursorHidden()) {

                EnableCursor();
                ShowCursor();
            }
        }
    }

    public static void SetVisible(bool visible) {

        if (visible)
            ShowCursor();
        else
            HideCursor();
    }

    public static void MoveToCenter() {

        if (!IsWindowFocused()) return;

        var pos = new Vector2(GetScreenWidth() / 2f, GetScreenHeight() / 2f);
        SetMousePosition((int)pos.X, (int)pos.Y);
    }
}