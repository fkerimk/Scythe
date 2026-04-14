using Newtonsoft.Json;
using Raylib_cs;
using static Raylib_cs.Raylib;

CommandLine.Init();
NativeResolver.Init();

// Initialize window
Window.Show(width: 1600, height: 900, maximize: false, flags: [ConfigFlags.Msaa4xHint, ConfigFlags.ResizableWindow]);

if (!CommandLine.Runtime) CommandLine.NoSplash = false;
if (!CommandLine.NoSplash) Splash.Show();

PathUtil.ValidateFile("Scythe.json", out var scytheJson, "{}");
JsonConvert.PopulateObject(File.ReadAllText(scytheJson), ScytheConfig.Current);

// Resolve relative paths
if (!string.IsNullOrWhiteSpace(ScytheConfig.Current.Project)) {
    
    if (!Directory.Exists(ScytheConfig.Current.Project)) {
        
        var altPath = Path.Combine(Directory.GetCurrentDirectory(), "Projects", ScytheConfig.Current.Project);
        if (Directory.Exists(altPath)) ScytheConfig.Current.Project = altPath;
    }
}

// Skip launcher if project is valid
if (string.IsNullOrWhiteSpace(ScytheConfig.Current.Project) || !Directory.Exists(ScytheConfig.Current.Project)) {
    
    var selected = Launcher.Show();
    if (string.IsNullOrEmpty(selected)) return 0;
    
    ScytheConfig.Current.Project = selected;
}

// Ensure full path and save back for next run
ScytheConfig.Current.Project = Path.GetFullPath(ScytheConfig.Current.Project);
File.WriteAllText(scytheJson, JsonConvert.SerializeObject(ScytheConfig.Current, Formatting.Indented));

if (!Directory.Exists(ScytheConfig.Current.Project)) throw new DirectoryNotFoundException(Ansi.ErrorMessage("Project not found"));

PathUtil.ValidateFile("Project.json", out var projectJson, "{}", true);
JsonConvert.PopulateObject(File.ReadAllText(projectJson), ProjectConfig.Current);

PathUtil.ValidateDir("Project", out _, true);

if (!CommandLine.Runtime)
     Editor.Show();
else Runtime.Show();

if (IsWindowReady()) CloseWindow();

return 0;