using System.Collections.Concurrent;
using System.Threading;
using Newtonsoft.Json;

internal static class AssetManager {

    private static readonly Dictionary<string, Asset>     Assets         = new();
    private static readonly List<FileSystemWatcher>       Watchers       = [];
    private static readonly Dictionary<string, Asset>     PathLookup     = new();
    private static readonly Dictionary<Type, List<Asset>> TypeCache      = new();
    private static readonly ConcurrentQueue<Action>       PendingActions = new();
    private static readonly List<string>                  _pendingFiles  = new();
    private static DateTime                               _debounceTime  = DateTime.MinValue;
    private static BackgroundTask?                        _importTask;

    public static void Update() {
        while (PendingActions.TryDequeue(out var action)) action();

        if (_importTask != null && _importTask.IsDone) _importTask = null;

        if (_importTask == null && _pendingFiles.Count > 0 && DateTime.Now > _debounceTime)
            StartImportTask();
    }

    public static void Init() {

        PathLookup.Clear();
        Assets.Clear();
        TypeCache.Clear();

        var resourcesPath = "";
        bool hasRes = PathUtil.GetPath("Resources", out resourcesPath);
        var resFiles = hasRes ? Directory.GetFiles(resourcesPath, "*.*", SearchOption.AllDirectories).ToList() : new List<string>();

        var modPath = ScytheConfig.Current.Project;
        var modFiles = Directory.Exists(modPath) ? Directory.GetFiles(modPath, "*.*", SearchOption.AllDirectories).Where(f => !f.Contains("/Assembly/") && !f.Contains("\\Assembly\\")).ToList() : new List<string>();

        if (hasRes) CreateWatcher(resourcesPath, "*.*", HandleFileChange, HandleFileDelete);
        if (Directory.Exists(modPath)) CreateWatcher(modPath, "*.*", HandleFileChange, HandleFileDelete);

        var totalFiles = resFiles.Concat(modFiles).ToList();
        if (totalFiles.Count == 0) return;

        var task = new BackgroundTask { Name = "Importing Assets", Status = "Working...", Progress = 0f };
        lock (Tasks.ActiveTasks) Tasks.ActiveTasks.Add(task);
        Notifications.ShowTask(task);

        for (int i = 0; i < totalFiles.Count; i++) {
            ImportFile(Path.GetFullPath(totalFiles[i]));
            task.Progress = (float)(i + 1) / totalFiles.Count;
            task.Status = Path.GetFileName(totalFiles[i]);
        }

        task.Progress = 1f;
        task.Status = "Success";
        task.IsDone = true;
        task.EndTime = DateTime.Now;
    }

    private static void ScanDirectory(string path) { }

    private static void HandleFileChange(string file) {

        var path = file.Replace('\\', '/').ToLowerInvariant();
        var toReload = Assets.Where(kvp => kvp.Value.File.Replace('\\', '/').ToLowerInvariant() == path).ToList();

        foreach (var kvp in toReload) kvp.Value.Unload();

        lock (_pendingFiles) {
            if (!_pendingFiles.Contains(file))
                _pendingFiles.Add(file);
        }
        _debounceTime = DateTime.Now.AddMilliseconds(500);
    }

    private static void StartImportTask() {

        List<string> filesToImport;
        lock (_pendingFiles) {
            filesToImport = new List<string>(_pendingFiles);
            _pendingFiles.Clear();
        }

        if (filesToImport.Count == 0) return;

        _importTask = Tasks.Run("Importing Assets", task => {

            int current = 0;
            foreach (var file in filesToImport) {

                var done = new ManualResetEventSlim(false);
                Tasks.RunOnMainThread(() => {
                    try { ImportFile(file); }
                    finally { done.Set(); }
                });
                done.Wait();
                current++;
                task.Progress = (float)current / filesToImport.Count;
                task.Status = Path.GetFileName(file);
            }

            task.Progress = 1f;
            task.Status = "Success";
        });
    }

    private static void HandleFileDelete(string file) => UnloadAsset(file);

    private static void ImportFile(string file) {

        var ext = Path.GetExtension(file).ToLowerInvariant();

        switch (ext) {

            case ".fbx" or ".obj" or ".gltf":                     ImportModel(file); break;
            case ".cs":                                           ImportScript(file); break;
            case ".vs" or ".fs":                                  ImportShader(file); break;
            case ".png" or ".jpg" or ".jpeg" or ".tga" or ".bmp": ImportTexture(file); break;

            default: {

                if (file.EndsWith(".material.json", StringComparison.OrdinalIgnoreCase))
                    ImportMaterial(file);

                else
                    switch (ext) {

                        case ".json": {

                            var assetFile = file[..^5];
                            if (File.Exists(assetFile)) ImportFile(assetFile);

                            break;
                        }
                    }

                break;
            }
        }
    }

    private static void UnloadAsset(string file) {

        var path     = file.Replace('\\', '/').ToLowerInvariant();
        var toRemove = Assets.Where(kvp => kvp.Value.File.Replace('\\', '/').ToLowerInvariant() == path).ToList();

        foreach (var kvp in toRemove) {
            kvp.Value.Unload();
            Assets.Remove(kvp.Key);
            RemoveFromMaps(kvp.Value);
        }
    }

    private static void RemoveFromMaps(Asset asset) {

        var keysToRemove = PathLookup.Where(kvp => kvp.Value == asset).Select(kvp => kvp.Key).ToList();
        foreach (var k in keysToRemove) PathLookup.Remove(k);
        if (TypeCache.TryGetValue(asset.GetType(), out var list)) list.Remove(asset);
    }

    private static void CreateWatcher(string path, string filter, Action<string> onImport, Action<string> onUnload) {

        var watcher = new FileSystemWatcher(path, filter) { IncludeSubdirectories = true };

        watcher.NotifyFilter =  NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime;
        watcher.Changed      += (_, e) => PendingActions.Enqueue(() => { SafeExec.Try(() => onImport(e.FullPath)); });
        watcher.Created      += (_, e) => PendingActions.Enqueue(() => { SafeExec.Try(() => onImport(e.FullPath)); });
        watcher.Deleted      += (_, e) => PendingActions.Enqueue(() => { SafeExec.Try(() => onUnload(e.FullPath)); });
        watcher.Renamed += (_, e) => PendingActions.Enqueue(() => {
                SafeExec.Try(() => {
                        onUnload(e.OldFullPath);
                        onImport(e.FullPath);
                    }
                );
            }
        );
        watcher.EnableRaisingEvents = true;

        Watchers.Add(watcher);
    }

    private static void ImportModel(string file) {

        var oldJson = Path.Combine(Path.GetDirectoryName(file)!, Path.GetFileNameWithoutExtension(file) + ".json");
        var newJson = file + ".json";

        if (File.Exists(oldJson) && !File.Exists(newJson) && oldJson != newJson) SafeExec.Try(() => File.Move(oldJson, newJson));

        if (!File.Exists(newJson)) SafeExec.Try(() => File.WriteAllText(newJson, JsonConvert.SerializeObject(new ModelAsset.ModelSettings(), Formatting.Indented)));

        GetOrLoad<ModelAsset>(file);
        GetOrLoad<AnimationAsset>(file);
    }

    private static void ImportScript(string file) => GetOrLoad<ScriptAsset>(file);

    private static void ImportMaterial(string file) {

        if (!File.Exists(file) || new FileInfo(file).Length < 5) SafeExec.Try(() => File.WriteAllText(file, JsonConvert.SerializeObject(new MaterialAsset.MaterialData(), Formatting.Indented)));

        GetOrLoad<MaterialAsset>(file);
    }

    private static void ImportTexture(string file) => GetOrLoad<TextureAsset>(file);

    private static void ImportShader(string file) {

        if (file.EndsWith(".fs", StringComparison.OrdinalIgnoreCase)) {

            var vs = Path.ChangeExtension(file, ".vs");

            if (File.Exists(vs)) return;
        }

        GetOrLoad<ShaderAsset>(file);
    }

    private static void GetOrLoad<T>(string file) where T : Asset, new() {

        var key = $"{file.ToLowerInvariant()}::{typeof(T).Name}";

        if (!Assets.TryGetValue(key, out var asset)) {

            asset       = new T { File = file };
            Assets[key] = asset;
            AddToMaps<T>(file, asset);
        }

        if (!asset.IsLoaded) asset.Load();
    }

    private static void AddToMaps<T>(string file, Asset asset) {

        var typePrefix = typeof(T).Name + "::";
        var full       = Path.GetFullPath(file).Replace('\\', '/').ToLowerInvariant();
        var name       = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();

        PathLookup[typePrefix + full] = asset;
        PathLookup[typePrefix + name] = asset;

        if (full.Contains("/resources/", StringComparison.InvariantCultureIgnoreCase)) {

            var idx    = full.IndexOf("/resources/", StringComparison.InvariantCultureIgnoreCase);
            var relRes = full[(idx + 1)..];
            PathLookup[typePrefix + relRes] = asset;
        }

        if (full.Contains(ScytheConfig.Current.Project.Replace('\\', '/'), StringComparison.InvariantCultureIgnoreCase)) {

            var rel = Path.GetRelativePath(ScytheConfig.Current.Project, file).Replace('\\', '/').ToLowerInvariant();
            PathLookup[typePrefix + rel] = asset;
        }

        if (!TypeCache.TryGetValue(typeof(T), out var list)) {

            list                 = [];
            TypeCache[typeof(T)] = list;
        }

        if (!list.Contains(asset)) list.Add(asset);
    }

    public static T? Get<T>(string? name) where T : Asset {

        if (string.IsNullOrEmpty(name)) return null;

        var req    = name.Replace('\\', '/').ToLowerInvariant();
        var prefix = typeof(T).Name + "::";

        if (PathLookup.TryGetValue(prefix + req, out var asset) && asset is T { IsLoaded: true } tAsset) return tAsset;

        if (req.Contains(':') || req.StartsWith('/')) return null;

        var res = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", name)).Replace('\\', '/').ToLowerInvariant();

        if (PathLookup.TryGetValue(prefix + res, out var rAsset) && rAsset is T { IsLoaded: true } rtAsset) return rtAsset;

        return null;
    }

    public static List<(string Name, string Path)> GetNames<T>() where T : Asset {

        if (!TypeCache.TryGetValue(typeof(T), out var list)) return [];

        var modPath = ScytheConfig.Current.Project;
        var resPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources");

        return list.Cast<T>()
                   .Select(a => {

                           var full = Path.GetFullPath(a.File);
                           var rel  = full;

                           if (full.StartsWith(modPath, StringComparison.OrdinalIgnoreCase))
                               rel                                                                    = Path.GetRelativePath(modPath,                               full);
                           else if (full.StartsWith(resPath, StringComparison.OrdinalIgnoreCase)) rel = Path.GetRelativePath(AppDomain.CurrentDomain.BaseDirectory, full);

                           return (Path.GetFileNameWithoutExtension(a.File), rel.Replace('\\', '/'));

                       }
                   )
                   .OrderBy(n => n.Item1)
                   .ToList();
    }

    public static IEnumerable<T> GetAll<T>() where T : Asset => !TypeCache.TryGetValue(typeof(T), out var list) ? [] : list.Cast<T>();

    public static void UnloadAll() {

        foreach (var watcher in Watchers) watcher.Dispose();

        Watchers.Clear();

        foreach (var asset in Assets.Values) asset.Unload();

        Assets.Clear();
        PathLookup.Clear();
        TypeCache.Clear();
    }
}
