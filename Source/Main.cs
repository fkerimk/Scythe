using Newtonsoft.Json;
using Raylib_cs;
using static Raylib_cs.Raylib;

CommandLine.Init();
NativeResolver.Init();
if (BundleRuntime.TryActivate()) CommandLine.Runtime = true;

if (CommandLine.SplashHelper) {
    SetTraceLogLevel(TraceLogLevel.Error);
    Splash.RunExternalHelperLoop();
    if (IsWindowReady()) CloseWindow();
    return 0;
}

// Initialize window
SetTraceLogLevel(TraceLogLevel.Error);
Window.Show(width: 1280, height: 720, maximize: false, flags: [ConfigFlags.ResizableWindow]);

if (!CommandLine.Runtime) CommandLine.NoSplash = false;

if (!BundleRuntime.IsActive) {
    PathUtil.ValidateFile("Scythe.json", out var scytheJson, "{}");
    JsonFile.PopulateInto(scytheJson, ScytheConfig.Current);

    // Resolve relative paths
    if (!string.IsNullOrWhiteSpace(ScytheConfig.Current.Project)) {
    
        if (!Directory.Exists(ScytheConfig.Current.Project)) {
        
            var altPath = Path.Combine(Directory.GetCurrentDirectory(), "Projects", ScytheConfig.Current.Project);
            if (Directory.Exists(altPath)) ScytheConfig.Current.Project = altPath;
        }
    }

    #if !SCYTHE_RUNTIME_BUILD
    var selected = Launcher.Show();
    if (string.IsNullOrEmpty(selected)) return 0;

    ScytheConfig.Current.Project = selected;
    #endif

    // Ensure full path and save back for next run
    ScytheConfig.Current.Project = Path.GetFullPath(ScytheConfig.Current.Project);
    JsonFile.WriteIndented(scytheJson, ScytheConfig.Current);
} else
    ScytheConfig.Current.Project = BundleRuntime.ProjectRoot;

if (!Directory.Exists(ScytheConfig.Current.Project)) throw new DirectoryNotFoundException(Ansi.ErrorMessage("Project not found"));

PathUtil.ValidateFile("Project.json", out var projectJson, "{}", true);
JsonFile.PopulateInto(projectJson, ProjectConfig.Current);

PathUtil.ValidateDir("Project", out _, true);

if (!CommandLine.NoSplash)
    Splash.StartExternalHelper();

#if SCYTHE_RUNTIME_BUILD
Runtime.Show();
#else
if (!CommandLine.Runtime)
    Editor.Show();
else Runtime.Show();
#endif

Splash.StopExternalHelper();
 
if (IsWindowReady()) CloseWindow();

return 0;
