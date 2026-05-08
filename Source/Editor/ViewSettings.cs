using Newtonsoft.Json;
using System.Reflection;

internal static class ViewSettings {

    private static string GetPath() {

        PathUtil.ValidateFile(
            "Layouts/Viewports.json",
            out var path,
            """
            {
              "EditorRender": true,
              "LevelBrowser": true,
              "ObjectBrowser": true,
              "ScriptEditor": true,
              "Preview": true,
              "RuntimeRender": true
            }
            """
        );

        return path;
    }

    private static IEnumerable<(FieldInfo field, Viewport value)> GetViewports() { return typeof(Editor).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => typeof(Viewport).IsAssignableFrom(f.FieldType)).Select(f => (f, (Viewport)f.GetValue(null)!)); }

    public static void Save() {

        var path = GetPath();
        var dir = Path.GetDirectoryName(path);
        if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var settings = new Dictionary<string, bool>();

        foreach (var (field, viewport) in GetViewports()) settings[field.Name] = viewport.IsOpen;

        var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
        File.WriteAllText(path, json);
    }

    public static void Load() {

        var path = GetPath();

        if (!File.Exists(path)) return;

        SafeExec.Try(() => {

                var json = File.ReadAllText(path);
                var settings = JsonConvert.DeserializeObject<Dictionary<string, bool>>(json);

                if (settings == null) return;

                foreach (var (field, viewport) in GetViewports()) {

                    if (settings.TryGetValue(field.Name, out var isOpen)) viewport.IsOpen = isOpen;
                }
            }
        );
    }
}
