using Newtonsoft.Json;

CommandLine.Init();
NativeResolver.Init();

if (!CommandLine.Runtime && !LspInstaller.CheckLspFiles()) CommandLine.NoSplash = false;

if (!CommandLine.NoSplash) Splash.Show();

PathUtil.ValidateFile("Scythe.json", out var scytheJson, "{}");
JsonConvert.PopulateObject(File.ReadAllText(scytheJson), ScytheConfig.Current);

if (!string.IsNullOrEmpty(ScytheConfig.Current.Project)) {

    ScytheConfig.Current.Project = Path.GetFullPath(ScytheConfig.Current.Project);

    if (!Directory.Exists(ScytheConfig.Current.Project)) throw new DirectoryNotFoundException(Ansi.ErrorMessage("Project not found"));

    PathUtil.ValidateFile("Project.json", out var projectJson, "{}", true);
    JsonConvert.PopulateObject(File.ReadAllText(projectJson), ProjectConfig.Current);

    PathUtil.ValidateDir("Project", out _, true);

    Script.Register();

    if (!CommandLine.Runtime)
        Editor.Show();
    else
        Runtime.Show();

    return 0;
}

throw new NotImplementedException(Ansi.ErrorMessage("Welcome screen is not implemented yet!"));