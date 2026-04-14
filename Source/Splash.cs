using System.Numerics;
using Raylib_cs;
using static Raylib_cs.Raylib;

internal static class Splash {

    private const float Duration = 1;

    private static float _time;
    private static Texture2D _art;

    public static void Show() {

        if (!PathUtil.GetPath("Images/Splash.png", out var splashPath)) return;

        _art = LoadTexture(splashPath);

        while (!WindowShouldClose()) {
            
            Window.UpdateFps();
            
            BeginDrawing();
            DrawTexturePro(_art, new Rectangle(0, 0, _art.Width, _art.Height), new Rectangle(0, 0, GetScreenWidth(), GetScreenHeight()), Vector2.Zero, 0, Color.White);
            
            _time += GetFrameTime();
            if (_time >= Duration) break;
            
            EndDrawing();
        }

        UnloadTexture(_art);
    }
}