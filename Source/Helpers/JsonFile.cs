using Newtonsoft.Json;

internal static class JsonFile {
    public static void PopulateInto(string path, object target) {
        if (!File.Exists(path)) return;
        JsonConvert.PopulateObject(File.ReadAllText(path), target);
    }

    public static T ReadOrDefault<T>(string path, T fallback) {
        if (!File.Exists(path)) return fallback;
        return JsonConvert.DeserializeObject<T>(File.ReadAllText(path)) ?? fallback;
    }

    public static void WriteIndented(string path, object value, bool ensureDirectory = true) {
        if (ensureDirectory) {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonConvert.SerializeObject(value, Formatting.Indented));
    }
}
