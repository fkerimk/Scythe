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

        var settings = new Dictionary<string, bool>();

        foreach (var (field, viewport) in GetViewports()) settings[field.Name] = viewport.IsOpen;
        JsonFile.WriteIndented(path, settings);
    }

    public static void Load() {

        var path = GetPath();

        if (!File.Exists(path)) return;

        SafeExec.Try(() => {

                var settings = JsonFile.ReadOrDefault(path, new Dictionary<string, bool>());

                if (settings == null) return;

                foreach (var (field, viewport) in GetViewports()) {

                    if (settings.TryGetValue(field.Name, out var isOpen)) viewport.IsOpen = isOpen;
                }
            }
        );
    }
}
