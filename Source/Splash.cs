using System.Numerics;
using Raylib_cs;
using static Raylib_cs.Raylib;

internal static class Splash {

    private const float Duration = 1;

    private static float _time;
    private static Texture2D _art;

    public static void Show() {

        if (!PathUtil.GetPath("Images/Splash.png", out var splashPath)) return;

        Window.Show(width: 320, height: 190, flags: ConfigFlags.UndecoratedWindow, isSplash: true);

        _art = LoadTexture(splashPath);

        if (!CommandLine.Runtime) LspInstaller.Start();

        while (!WindowShouldClose()) {

            Window.UpdateFps();

            BeginDrawing();

            DrawTexturePro(_art, new Rectangle(0, 0, _art.Width, _art.Height), new Rectangle(0, 0, GetScreenWidth(), GetScreenHeight()), Vector2.Zero, 0, Color.White);

            _time += GetFrameTime();

            if (!CommandLine.Runtime && !LspInstaller.IsDone) {

                DrawRectangle(0, GetScreenHeight() - 40, GetScreenWidth(), 40, new Color(0, 0, 0, 200));

                DrawText(LspInstaller.Status, 10, GetScreenHeight() - 32, 10, Color.White);

                if (LspInstaller.Progress > 0) DrawRectangle(10, GetScreenHeight() - 14, (int)((GetScreenWidth() - 20) * LspInstaller.Progress), 4, Color.SkyBlue);

                var spinnerTime = (float)GetTime() * 10;
                var cx = GetScreenWidth() - 20;
                var cy = GetScreenHeight() - 20;

                for (var i = 0; i < 8; i++) {
                    var angle = spinnerTime + i * (360f / 8f);
                    var rad = angle * (Math.PI / 180f);
                    var x = cx + Math.Cos(rad) * 8;
                    var y = cy + Math.Sin(rad) * 8;
                    DrawCircle((int)x, (int)y, 2, Fade(Color.White, i / 8f));
                }

            } else if (_time >= Duration) break;

            EndDrawing();
        }

        UnloadTexture(_art);
        CloseWindow();
    }
}