using Newtonsoft.Json;

internal class ProjectConfig {

    [JsonIgnore] public static ProjectConfig Current = new();

    public string Name = "SCYTHE";
    public string StartupLevel = "";
    public string StartupLevelPath = "";

    public static string GetPath() => Path.Combine(ScytheConfig.Current.Project, "Project.json");

    public void Save() {
        JsonFile.WriteIndented(GetPath(), this);
    }
}
