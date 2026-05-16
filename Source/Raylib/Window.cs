using Raylib_cs;
using static Raylib_cs.Raylib;

internal static class Window {

    private static int _lastFps = -2;

    public static void UpdateFps() {

        var targetFps = Core.IsRuntimePresentation ? OldConfig.Runtime.FpsLock : OldConfig.Editor.FpsLock;

        if (targetFps == -1) targetFps = Screen.RefreshRate;

        if (_lastFps == targetFps) return;

        _lastFps = targetFps;

        SetTargetFPS(targetFps);
    }

    public static void DrawFps(System.Numerics.Vector2 pos) {

        if (Core.IsRuntimePresentation ? !OldConfig.Runtime.DrawFps : !OldConfig.Editor.DrawFps) return;

        var fpsText = GetFPS().ToString();

        DrawTextEx(Fonts.RlMontserratRegular, fpsText, pos + new System.Numerics.Vector2(1, 0), 26, 1, Color.Black);
        DrawTextEx(Fonts.RlMontserratRegular, fpsText, pos, 26, 1, Colors.Primary);
    }

    public static void Show(int width = -1, int height = -1, bool maximize = false, bool fullscreen = false, bool borderless = true, string title = "SCYTHE", bool isSplash = false, params ConfigFlags[] flags) {

        if (!IsWindowReady()) {
            // Hard defaults for first init if not specified
            if (width == -1) width = 1600;
            if (height == -1) height = 900;

            // Flags
            Flags.Set(flags);

            // New window     
            InitWindow(width, height, title);
            SetWindowMonitor(0);
            SetExitKey(KeyboardKey.Null);

            // Window icon
            if (PathUtil.GetPath("Collection/Icon.png", out var iconPath)) {
                var img = LoadImage(iconPath);
                SetWindowIcon(img);
                UnloadImage(img);
            }

            // Initialize Audio
            if (!IsAudioDeviceReady()) InitAudioDevice();
        } else {
            SetWindowTitle(title);
            if (width > 0 && height > 0 && !IsWindowMaximized()) SetWindowSize(width, height);
        }

        // Fullscreen & maximizing t
        if (fullscreen) {
            if (borderless) {
                Flags.Add(ConfigFlags.UndecoratedWindow);
                SetWindowSize(GetMonitorWidth(GetCurrentMonitor()), GetMonitorHeight(GetCurrentMonitor()));
            } else if (!IsWindowFullscreen()) ToggleFullscreen();
        } else if (maximize) {
            if (!IsWindowMaximized()) MaximizeWindow();
        }
    }
}
