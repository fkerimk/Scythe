using Raylib_cs;
using static Raylib_cs.Raylib;

internal static class Runtime {

    private static bool _scheduledQuit;

    public static void Show() {

        Window.Show(fullscreen: false, flags: [ConfigFlags.Msaa4xHint, ConfigFlags.ResizableWindow], title: ProjectConfig.Current.Name);

        // Setup core
        Core.Init();

        while (!WindowShouldClose()) {

            if (Core.ActiveCamera == null) {

                TraceLog(TraceLogLevel.Error, "RUNTIME: No active camera found, exiting loop.");

                break;
            }

            Window.UpdateFps();
            Core.Load();

            BeginDrawing();
            ClearBackground(Colors.Game);

            Core.Step();

            var fpsText = GetFPS().ToString();
            var textSize = MeasureTextEx(Fonts.RlMontserratRegular, fpsText, 20, 1);
            Window.DrawFps(new System.Numerics.Vector2(GetScreenWidth() - textSize.X - 15, 15));
            EndDrawing();

            if (_scheduledQuit) break;
        }

        Core.Quit();
    }

    public static void Quit() => _scheduledQuit = true;
}