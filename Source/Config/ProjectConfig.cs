using Newtonsoft.Json;

internal class ProjectConfig {

    [JsonIgnore] public static ProjectConfig Current = new();

    public string Name = "SCYTHE";
    public string StartupLevel = "";
    public string StartupLevelPath = "";

    public static string GetPath() => Path.Combine(ScytheConfig.Current.Project, "Project.json");

    public void Save() {

        var path = GetPath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

        File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
    }
}
