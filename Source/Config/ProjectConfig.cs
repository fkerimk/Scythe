using Newtonsoft.Json;

internal class ProjectConfig {

    [JsonIgnore] public static ProjectConfig Current = new();

    [RecordHistory]
    public string Name = "SCYTHE";
    [RecordHistory]
    public string StartupLevel = "";
    [RecordHistory]
    public string StartupLevelPath = "";
    [RecordHistory]
    public string[] BackgroundScripts = [];
    [RecordHistory]
    public string[] BackgroundScriptPaths = [];

    public static string GetPath() => Path.Combine(ScytheConfig.Current.Project, "Project.json");

    public void Save() {
        JsonFile.WriteIndented(GetPath(), this);
    }
}
