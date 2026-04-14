using System.Numerics;
using Raylib_cs;
using static Raylib_cs.Raylib;

internal static class Splash {

    private const float Duration = 2f;
    private static Image _image;
    private static Texture2D _tex;
    private static int _animFrames;
    private static int _currentFrame;
    private static float _frameTimer;

    public static unsafe void Init() {
        if (_tex.Id != 0 || _image.Data != null) return;
        if (!PathUtil.GetPath("Images/Splash.gif", out var splashPath)) return;

        _image = LoadImageAnim(splashPath, out _animFrames);
        if (_image.Data == null) return;

        _tex = LoadTextureFromImage(_image);
    }

    public static bool IsLoading => !Tasks.MainThreadQueue.IsEmpty || Tasks.ActiveTasks.Any(t => t.Name == "Importing Assets" && !t.IsDone);

    public static void ShowWhileLoading() {
        
        Init();
        if (_tex.Id == 0) return;

        float time = 0;

        while (!WindowShouldClose()) {
            
            Window.UpdateFps();

            // Give AssetManager exactly 16ms of this frame to securely load Raylib/Assimp chunks completely synchronously!
            Tasks.Update(16);

            RenderSingleFrame();
            
            time += GetFrameTime();

            // Exit when the minimum 2s Duration passed AND Background tasks (Importing Assets) finished entirely
            if (!IsLoading && time >= Duration) break;
        }
    }

    public static unsafe void RenderSingleFrame() {
        
        Init(); 
        if (_tex.Id == 0) return;
        
        bool frameChanged = false;
        _frameTimer += GetFrameTime();
        
        while (_frameTimer >= 0.02f) {
            _currentFrame++;
            _frameTimer -= 0.02f;
            frameChanged = true;
        }

        if (frameChanged) {
            _currentFrame %= _animFrames;
            var nextFrameDataOffset = _image.Width * _image.Height * 4 * _currentFrame;
            UpdateTexture(_tex, (byte*)_image.Data + nextFrameDataOffset);
        }

        BeginDrawing();
        ClearBackground(Color.Black);
        
        var posX = (GetScreenWidth() - _tex.Width) / 2;
        var posY = (GetScreenHeight() - _tex.Height) / 2;
        DrawTexture(_tex, posX, posY, Color.White);
        
        EndDrawing();
    }

    public static void Cleanup() {
        
        if (_tex.Id != 0) {
            UnloadTexture(_tex);
            UnloadImage(_image);
            
            _tex = default;
            _image = default;
        }
    }
}