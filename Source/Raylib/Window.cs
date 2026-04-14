using Raylib_cs;
using static Raylib_cs.Raylib;

internal static class Window {

    private static int _lastFps = -2;

    public static void UpdateFps() {

        var targetFps = CommandLine.Editor ? OldConfig.Editor.FpsLock : OldConfig.Runtime.FpsLock;

        if (targetFps == -1) targetFps = Screen.RefreshRate;

        if (_lastFps == targetFps) return;

        _lastFps = targetFps;

        SetTargetFPS(targetFps);
    }

    public static void DrawFps(System.Numerics.Vector2 pos) {

        if (CommandLine.Editor ? !OldConfig.Editor.DrawFps : !OldConfig.Runtime.DrawFps) return;

        var fpsText = GetFPS().ToString();

        DrawTextEx(Fonts.RlMontserratRegular, fpsText, pos + new System.Numerics.Vector2(1, 0), 26, 1, Color.Black);
        DrawTextEx(Fonts.RlMontserratRegular, fpsText, pos, 26, 1, Colors.Primary);
    }

    public static void Show(int width = 1600, int height = 900, bool maximize = false, bool fullscreen = false, bool borderless = true, string title = "SCYTHE", bool isSplash = false, params ConfigFlags[] flags) {

        // Close old window
        if (IsWindowReady()) CloseWindow();

        // Flags
        Flags.Set(flags);

        // New window     
        InitWindow(width, height, title);
        SetWindowMonitor(0);
        SetExitKey(KeyboardKey.Null);

        // Fullscreen & maximizing 
        if (fullscreen) {

            if (borderless) {

                Flags.Add(ConfigFlags.UndecoratedWindow);
                SetWindowSize(GetMonitorWidth(GetCurrentMonitor()), GetMonitorHeight(GetCurrentMonitor()));

            } else if (!IsWindowFullscreen()) ToggleFullscreen();

        } else if (maximize) MaximizeWindow();

        // Window icon
        if (PathUtil.GetPath("Images/Icon.png", out var iconPath)) {

            var img = LoadImage(iconPath);
            SetWindowIcon(img);
            UnloadImage(img);
        }

        // Initialize Audio
        if (!IsAudioDeviceReady()) InitAudioDevice();
    }
}