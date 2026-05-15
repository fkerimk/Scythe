using System.Numerics;
using System.Diagnostics;
using System.Threading;
using Raylib_cs;
using static Raylib_cs.Raylib;

internal static class Splash {

    private const float Duration = 2f;
    private static Image _image;
    private static Texture2D _tex;
    private static int _animFrames;
    private static int _currentFrame;
    private static float _frameTimer;
    private static Process? _helperProcess;
    private static string _helperSignalPath = "";
    private static string _helperReadyPath = "";
    private static ProcessPriorityClass? _previousPriorityClass;

    public static bool HasExternalHelper {
        get {
            try {
                return _helperProcess is { HasExited: false };
            } catch {
                return false;
            }
        }
    }

    public static unsafe void Init() {
        if (_tex.Id != 0 || _image.Data != null) return;
        if (!PathUtil.GetPath("Collection/Splash.gif", out var splashPath)) return;

        _image = LoadImageAnim(splashPath, out _animFrames);
        if (_image.Data == null) return;

        _tex = LoadTextureFromImage(_image);
    }

    public static bool IsLoading => !Tasks.MainThreadQueue.IsEmpty || Tasks.ActiveTasks.Any(t => t.Name == "Importing Assets" && !t.IsDone);

    public static void StartExternalHelper() {

        if (CommandLine.NoSplash || CommandLine.SplashHelper || _helperProcess != null) return;

        _helperSignalPath = Path.Combine(Path.GetTempPath(), $"scythe-splash-{Guid.NewGuid():N}.sig");
        _helperReadyPath = Path.Combine(Path.GetTempPath(), $"scythe-splash-ready-{Guid.NewGuid():N}.sig");
        File.WriteAllText(_helperSignalPath, "loading");

        var entryAssemblyPath = Path.Combine(AppContext.BaseDirectory, "Scythe.dll");
        var processPath = Environment.ProcessPath ?? "";
        if (string.IsNullOrWhiteSpace(processPath)) return;

        var launchViaDotnet = string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetExtension(entryAssemblyPath), ".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(entryAssemblyPath);

        var args = launchViaDotnet
            ? $"\"{entryAssemblyPath}\" splashhelper splashsignal \"{_helperSignalPath}\" splashready \"{_helperReadyPath}\""
            : $"splashhelper splashsignal \"{_helperSignalPath}\" splashready \"{_helperReadyPath}\"";

        var startInfo = new ProcessStartInfo {
            FileName = launchViaDotnet ? "dotnet" : processPath,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = Directory.GetCurrentDirectory()
        };

        _helperProcess = Process.Start(startInfo);
        if (_helperProcess != null) {
            try {
                _helperProcess.PriorityClass = ProcessPriorityClass.High;
            } catch {
            }
        }

        try {
            var currentProcess = Process.GetCurrentProcess();
            _previousPriorityClass = currentProcess.PriorityClass;
            currentProcess.PriorityClass = ProcessPriorityClass.AboveNormal;
        } catch {
            _previousPriorityClass = null;
        }

        var helperReady = false;

        for (var i = 0; i < 200; i++) {
            try {
                if (_helperProcess?.HasExited == true) break;
            } catch {
                break;
            }

            if (File.Exists(_helperReadyPath)) {
                helperReady = true;
                break;
            }

            Thread.Sleep(10);
        }

        if (!helperReady) {
            StopExternalHelper();
            return;
        }

        if (IsWindowReady()) MinimizeWindow();
    }

    public static void StopExternalHelper() {

        if (!string.IsNullOrWhiteSpace(_helperSignalPath)) {
            try {
                if (File.Exists(_helperSignalPath)) File.Delete(_helperSignalPath);
            } catch {
            }
        }

        if (!string.IsNullOrWhiteSpace(_helperReadyPath)) {
            try {
                if (File.Exists(_helperReadyPath)) File.Delete(_helperReadyPath);
            } catch {
            }
        }

        try {
            _helperProcess?.WaitForExit(2000);
        } catch {
        }

        _helperProcess?.Dispose();
        _helperProcess = null;
        _helperSignalPath = "";
        _helperReadyPath = "";

        if (_previousPriorityClass.HasValue) {
            try {
                Process.GetCurrentProcess().PriorityClass = _previousPriorityClass.Value;
            } catch {
            }

            _previousPriorityClass = null;
        }

        if (IsWindowReady()) RestoreWindow();
    }

    public static void ShowWhileLoading() {
        
        Init();
        if (_tex.Id == 0) return;

        double startTime = GetTime();

        while (!WindowShouldClose()) {
            
            Window.UpdateFps();

            // Give AssetManager exactly 16ms of this frame to securely load Raylib/Assimp chunks completely synchronously!
            Tasks.Update(16);

            RenderSingleFrame();
            
            // Exit when the minimum 2s Duration passed AND Background tasks (Importing Assets) finished entirely
            if (!IsLoading && (GetTime() - startTime) >= Duration) break;
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

    public static void RunExternalHelperLoop() {

        Init();

        Window.Show(width: 640, height: 360, maximize: false, borderless: false, title: "Scythe", isSplash: true, flags: [ConfigFlags.ResizableWindow]);
        SetTargetFPS(Math.Max(Screen.RefreshRate, 60));

        if (_tex.Id == 0) return;

        if (!string.IsNullOrWhiteSpace(CommandLine.SplashReadyPath)) {
            try {
                File.WriteAllText(CommandLine.SplashReadyPath, "ready");
            } catch {
            }
        }

        try {
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
        } catch {
        }

        try {
            Thread.CurrentThread.Priority = ThreadPriority.Highest;
        } catch {
        }

        while (!WindowShouldClose()) {
            RenderSingleFrame();

            if (!string.IsNullOrWhiteSpace(CommandLine.SplashSignalPath) && !File.Exists(CommandLine.SplashSignalPath))
                break;
        }

        Cleanup();
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
