using System.Collections.Concurrent;
using System.Threading;
using Newtonsoft.Json;

internal static class AssetManager {

    private static readonly Dictionary<string, Asset>     Assets         = new();
    private static readonly Dictionary<string, Asset>     GuidLookup     = new();
    private static readonly List<FileSystemWatcher>       Watchers       = [];
    private static readonly Dictionary<string, Asset>     PathLookup     = new();
    private static readonly Dictionary<Type, List<Asset>> TypeCache      = new();
    private static readonly ConcurrentQueue<Action>       PendingActions = new();
    private static readonly List<string>                  _pendingFiles  = new();
    private static readonly Dictionary<string, int>       _ignoredChanges = new();
    private static DateTime                               _debounceTime  = DateTime.MinValue;
    private static BackgroundTask?                        _importTask;
    private static bool                                   _isInitializing;

    public static bool IsInitializing => _isInitializing;

    public static void Update() {
        while (PendingActions.TryDequeue(out var action)) action();

        if (_importTask != null && _importTask.IsDone) _importTask = null;

        if (_importTask == null && _pendingFiles.Count > 0 && DateTime.Now > _debounceTime)
            StartImportTask();
    }

    public static void Init() {

        _isInitializing = true;

        try {

            foreach (var watcher in Watchers) watcher.Dispose();
            Watchers.Clear();
            PathLookup.Clear();
            GuidLookup.Clear();
            Assets.Clear();
            TypeCache.Clear();

            var resourcesPath = "";
            bool hasRes = PathUtil.GetPath("Resources", out resourcesPath);
            var resFiles = hasRes ? Directory.GetFiles(resourcesPath, "*.*", SearchOption.AllDirectories).ToList() : new List<string>();

            var modPath = ScytheConfig.Current.Project;
            var modFiles = Directory.Exists(modPath) ? Directory.GetFiles(modPath, "*.*", SearchOption.AllDirectories).Where(f => !f.Contains("/Assembly/") && !f.Contains("\\Assembly\\")).ToList() : new List<string>();

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

            FinalizeAssetGraph();

            if (hasRes) CreateWatcher(resourcesPath, "*.*", HandleFileChange, HandleFileDelete);
            if (Directory.Exists(modPath)) CreateWatcher(modPath, "*.*", HandleFileChange, HandleFileDelete);

        } finally {
            _isInitializing = false;
        }
    }

    private static void ScanDirectory(string path) { }

    private static void HandleFileChange(string file) {

        var path = file.Replace('\\', '/').ToLowerInvariant();
        if (ShouldIgnoreChange(path)) return;

        var toReload = Assets.Where(kvp => kvp.Value.GetWatchedFiles().Any(watched => watched.Replace('\\', '/').ToLowerInvariant() == path)).ToList();

        foreach (var kvp in toReload) {

            kvp.Value.Unload();
            RefreshDependentAssets(kvp.Value);
            ReloadDependentComponents(kvp.Value);
        }

        lock (_pendingFiles) {
            foreach (var importTarget in GetImportTargets(file))
                if (!_pendingFiles.Contains(importTarget))
                    _pendingFiles.Add(importTarget);
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

        file = Path.GetFullPath(file);

        if (!File.Exists(file) && !file.EndsWith(".material.json", StringComparison.OrdinalIgnoreCase)) return;

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
        var toRemove = Assets.Where(kvp => kvp.Value.GetWatchedFiles().Any(watched => watched.Replace('\\', '/').ToLowerInvariant() == path)).ToList();

        foreach (var kvp in toRemove) {
            kvp.Value.Unload();
            RefreshDependentAssets(kvp.Value);
            ReloadDependentComponents(kvp.Value);
            Assets.Remove(kvp.Key);
            RemoveFromMaps(kvp.Value);
        }
    }

    private static void RemoveFromMaps(Asset asset) {

        var keysToRemove = PathLookup.Where(kvp => kvp.Value == asset).Select(kvp => kvp.Key).ToList();
        foreach (var k in keysToRemove) PathLookup.Remove(k);
        var guidKeysToRemove = GuidLookup.Where(kvp => kvp.Value == asset).Select(kvp => kvp.Key).ToList();
        foreach (var k in guidKeysToRemove) GuidLookup.Remove(k);
        if (TypeCache.TryGetValue(asset.GetType(), out var list)) list.Remove(asset);
    }

    private static IEnumerable<string> GetImportTargets(string file) {

        file = Path.GetFullPath(file);

        if (file.EndsWith(".material.json", StringComparison.OrdinalIgnoreCase)) {

            yield return file;
            yield break;
        }

        if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) {

            var owner = file[..^5];
            if (File.Exists(owner)) yield return owner;
            yield break;
        }

        if (file.EndsWith(".fs", StringComparison.OrdinalIgnoreCase)) {

            var vs = Path.ChangeExtension(file, ".vs");
            if (File.Exists(vs)) {

                yield return vs;
                yield break;
            }
        }

        yield return file;
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
        var isNew = false;

        if (!Assets.TryGetValue(key, out var asset)) {

            asset       = new T { File = file };
            Assets[key] = asset;
            isNew = true;
        }

        if (!asset.IsLoaded && !asset.Load()) return;

        AddToMaps<T>(file, asset);
        NormalizeInternalReferences(asset);
        SyncDependentComponentReferences(asset);
        RefreshDependentAssets(asset);

        if (!isNew) ReloadDependentComponents(asset);
    }

    private static void AddToMaps<T>(string file, Asset asset) {

        RemoveFromMaps(asset);

        var typePrefix = typeof(T).Name + "::";
        var full       = Path.GetFullPath(file).Replace('\\', '/').ToLowerInvariant();
        var name       = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();

        PathLookup[typePrefix + full] = asset;
        PathLookup[typePrefix + name] = asset;
        if (!string.IsNullOrWhiteSpace(asset.GUID)) GuidLookup[typePrefix + asset.GUID.ToLowerInvariant()] = asset;

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

        if (GuidLookup.TryGetValue(prefix + req, out var guidAsset) && guidAsset is T { IsLoaded: true } typedGuidAsset) return typedGuidAsset;
        if (PathLookup.TryGetValue(prefix + req, out var asset) && asset is T { IsLoaded: true } tAsset) return tAsset;

        if (req.Contains(':') || req.StartsWith('/')) return null;

        var res = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", name)).Replace('\\', '/').ToLowerInvariant();

        if (PathLookup.TryGetValue(prefix + res, out var rAsset) && rAsset is T { IsLoaded: true } rtAsset) return rtAsset;

        return null;
    }

    public static string NormalizeReference<T>(string? value) where T : Asset {

        if (string.IsNullOrWhiteSpace(value)) return "";

        var asset = Get<T>(value);
        return asset?.GUID ?? value;
    }

    public static string? GetGuid<T>(string? value) where T : Asset => Get<T>(value)?.GUID;

    public static string? GetPath<T>(string? value) where T : Asset => Get<T>(value)?.File;

    public static T? GetOrImport<T>(string? path) where T : Asset {

        if (string.IsNullOrWhiteSpace(path)) return null;

        var asset = Get<T>(path);
        asset ??= FindMovedAssetFallback<T>(path);
        if (asset != null) return asset;

        EnsureImported(path);
        asset = Get<T>(path);
        asset ??= FindMovedAssetFallback<T>(path);

        if (asset is { ThumbnailDirty: true } && asset is TextureAsset or MaterialAsset or ModelAsset)
            Preview.UpdateThumbnail(asset);

        return asset;
    }

    public static void EnsureImported(string? path) {

        if (string.IsNullOrWhiteSpace(path)) return;

        if (!PathUtil.GetPath(path, out var fullPath) && !File.Exists(path)) return;
        fullPath = Path.GetFullPath(PathUtil.GetPath(path, out var resolvedPath) ? resolvedPath : path);

        var normalized = fullPath.Replace('\\', '/').ToLowerInvariant();
        var ext = Path.GetExtension(fullPath).ToLowerInvariant();

        if (ext is ".png" or ".jpg" or ".jpeg" or ".tga" or ".bmp") {

            if (Get<TextureAsset>(fullPath) == null) ImportTexture(fullPath);
            return;
        }

        if (fullPath.EndsWith(".material.json", StringComparison.OrdinalIgnoreCase)) {

            if (Get<MaterialAsset>(fullPath) == null) ImportMaterial(fullPath);
            return;
        }

        if (ext is ".fbx" or ".obj" or ".gltf") {

            if (Get<ModelAsset>(fullPath) == null) ImportModel(fullPath);
            return;
        }

        if (ext == ".cs") {

            if (Get<ScriptAsset>(fullPath) == null) ImportScript(fullPath);
            return;
        }

        if (ext is ".vs" or ".fs") {

            if (Get<ShaderAsset>(fullPath) == null) ImportShader(fullPath);
        }
    }

    public static void RegisterInternalWrite(string path, int ignoredEvents = 4) {

        var normalized = Path.GetFullPath(path).Replace('\\', '/').ToLowerInvariant();
        _ignoredChanges[normalized] = _ignoredChanges.GetValueOrDefault(normalized, 0) + ignoredEvents;
    }

    public static string GetStoredPath(string? file) {

        if (string.IsNullOrWhiteSpace(file)) return "";

        var full = Path.GetFullPath(file);
        var modPath = ScytheConfig.Current.Project;
        var resPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources");

        if (full.StartsWith(modPath, StringComparison.OrdinalIgnoreCase))
            return Path.GetRelativePath(modPath, full).Replace('\\', '/');

        if (full.StartsWith(resPath, StringComparison.OrdinalIgnoreCase))
            return Path.GetRelativePath(AppDomain.CurrentDomain.BaseDirectory, full).Replace('\\', '/');

        return full.Replace('\\', '/');
    }

    public static T? ResolveReference<T>(ref string guid, ref string path) where T : Asset {

        var asset = Get<T>(guid) ?? Get<T>(path);
        asset ??= FindMovedAssetFallback<T>(path);
        if (asset == null) return null;

        guid = asset.GUID;
        path = GetStoredPath(asset.File);
        return asset;
    }

    public static List<(string Name, string Path, string GUID)> GetNames<T>() where T : Asset {

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

                           return (Path.GetFileNameWithoutExtension(a.File), rel.Replace('\\', '/'), a.GUID);

                       }
                   )
                   .OrderBy(n => n.Item1)
                   .ToList();
    }

    public static IEnumerable<T> GetAll<T>() where T : Asset => !TypeCache.TryGetValue(typeof(T), out var list) ? [] : list.Cast<T>();

    private static T? FindMovedAssetFallback<T>(string? path) where T : Asset {

        if (string.IsNullOrWhiteSpace(path) || !TypeCache.TryGetValue(typeof(T), out var list)) return null;

        var fileName = Path.GetFileName(path.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        var matches = list.Cast<T>()
                          .Where(asset => asset.IsLoaded && string.Equals(Path.GetFileName(asset.File), fileName, StringComparison.OrdinalIgnoreCase))
                          .Take(2)
                          .ToList();

        return matches.Count == 1 ? matches[0] : null;
    }

    public static void UnloadAll() {

        foreach (var watcher in Watchers) watcher.Dispose();

        Watchers.Clear();

        foreach (var asset in Assets.Values) asset.Unload();

        Assets.Clear();
        GuidLookup.Clear();
        PathLookup.Clear();
        TypeCache.Clear();
    }

    private static void NormalizeInternalReferences(Asset asset) {

        switch (asset) {

            case MaterialAsset material:
                if (material.NormalizeReferences()) material.Save();

                break;

            case ModelAsset model:
                if (model.NormalizeReferences()) model.SaveSettings();

                break;
        }
    }

    private static void ReloadDependentComponents(Asset asset) {

        if (string.IsNullOrWhiteSpace(asset.GUID)) return;

        foreach (var level in Core.OpenLevels)
            ReloadDependentComponents(level.Root, asset);
    }

    public static void ReloadComponentsUsing(Asset asset) => ReloadDependentComponents(asset);

    private static void SyncDependentComponentReferences(Asset asset) {

        if (string.IsNullOrWhiteSpace(asset.GUID)) return;

        foreach (var level in Core.OpenLevels)
            SyncDependentComponentReferences(level.Root, level, asset);
    }

    private static void SyncDependentComponentReferences(Obj obj, Level level, Asset asset) {

        foreach (var component in obj.Components.Values) {

            var props = component.GetType().GetProperties().Where(prop => Attribute.IsDefined(prop, typeof(FindAssetAttribute)));

            foreach (var prop in props) {

                if (prop.GetCustomAttributes(typeof(FindAssetAttribute), true).FirstOrDefault() is not FindAssetAttribute attr) continue;
                if (attr.TypeName != asset.GetType().Name) continue;

                var pathProp = component.GetType().GetProperty("Path");
                var guidValue = prop.GetValue(component) as string ?? "";
                var pathValue = pathProp?.GetValue(component) as string ?? "";
                var usesAssetByGuid = string.Equals(NormalizeReferenceByType(attr.TypeName, guidValue), asset.GUID, StringComparison.OrdinalIgnoreCase);
                var usesAssetByPath = string.Equals(NormalizeReferenceByType(attr.TypeName, pathValue), asset.GUID, StringComparison.OrdinalIgnoreCase);

                if (!usesAssetByGuid && !usesAssetByPath) continue;

                var storedPath = GetStoredPath(asset.File);
                var changed = false;

                if (!string.Equals(guidValue, asset.GUID, StringComparison.OrdinalIgnoreCase)) {
                    prop.SetValue(component, asset.GUID);
                    changed = true;
                }

                if (pathProp is { CanWrite: true } && pathProp.PropertyType == typeof(string) &&
                    !string.Equals(pathValue, storedPath, StringComparison.OrdinalIgnoreCase)) {
                    pathProp.SetValue(component, storedPath);
                    changed = true;
                }

                if (changed) level.IsDirty = true;
            }
        }

        foreach (var child in obj.Children.Values) SyncDependentComponentReferences(child, level, asset);
    }

    private static void ReloadDependentComponents(Obj obj, Asset asset) {

        foreach (var component in obj.Components.Values) {

            var props = component.GetType().GetProperties().Where(prop => Attribute.IsDefined(prop, typeof(FindAssetAttribute)));

            foreach (var prop in props) {

                if (prop.GetCustomAttributes(typeof(FindAssetAttribute), true).FirstOrDefault() is not FindAssetAttribute attr) continue;
                if (attr.TypeName != asset.GetType().Name) continue;
                var guidValue = prop.GetValue(component) as string ?? "";
                var pathValue = component.GetType().GetProperty("Path")?.GetValue(component) as string ?? "";
                var usesAssetByGuid = string.Equals(NormalizeReferenceByType(attr.TypeName, guidValue), asset.GUID, StringComparison.OrdinalIgnoreCase);
                var usesAssetByPath = string.Equals(NormalizeReferenceByType(attr.TypeName, pathValue), asset.GUID, StringComparison.OrdinalIgnoreCase);
                if (!usesAssetByGuid && !usesAssetByPath) continue;

                component.UnloadAndQuit();
                break;
            }
        }

        foreach (var child in obj.Children.Values) ReloadDependentComponents(child, asset);
    }

    private static void RefreshDependentAssets(Asset asset) {

        if (_isInitializing) return;

        switch (asset) {

            case TextureAsset texture:
                RefreshMaterialsUsingTexture(texture.GUID);
                break;

            case ShaderAsset shader:
                RefreshMaterialsUsingShader(shader.GUID);
                break;

            case MaterialAsset material:
                RefreshModelsUsingMaterial(material.GUID);
                Preview.UpdateThumbnail(material);
                break;

            case ModelAsset model:
                Preview.UpdateThumbnail(model);
                break;
        }
    }

    private static void FinalizeAssetGraph() {

        foreach (var texture in GetAll<TextureAsset>().ToList()) {

            texture.InvalidateThumbnail();
            Preview.UpdateThumbnail(texture);
        }

        foreach (var material in GetAll<MaterialAsset>().ToList()) {

            if (material.NormalizeReferences()) material.Save();
            material.ApplyChanges(updateThumbnail: false);
            material.InvalidateThumbnail();
            Preview.UpdateThumbnail(material);
        }

        foreach (var model in GetAll<ModelAsset>().ToList()) {

            if (model.NormalizeReferences()) model.SaveSettings();
            model.ApplySettings();
            model.InvalidateThumbnail();
            Preview.UpdateThumbnail(model);
        }
    }

    private static void RefreshMaterialsUsingTexture(string guid) {

        if (string.IsNullOrWhiteSpace(guid)) return;

        foreach (var material in GetAll<MaterialAsset>().ToList()) {

            if (!material.Data.Textures.Values.Any(value => string.Equals(NormalizeReference< TextureAsset >(value), guid, StringComparison.OrdinalIgnoreCase))) continue;

            material.ApplyChanges();
            material.InvalidateThumbnail();
            Preview.UpdateThumbnail(material);
            RefreshModelsUsingMaterial(material.GUID);
        }
    }

    private static void RefreshMaterialsUsingShader(string guid) {

        if (string.IsNullOrWhiteSpace(guid)) return;

        foreach (var material in GetAll<MaterialAsset>().ToList()) {

            if (!string.Equals(NormalizeReference<ShaderAsset>(material.Data.Shader), guid, StringComparison.OrdinalIgnoreCase)) continue;

            material.ApplyChanges();
            material.InvalidateThumbnail();
            Preview.UpdateThumbnail(material);
            RefreshModelsUsingMaterial(material.GUID);
        }
    }

    private static void RefreshModelsUsingMaterial(string guid) {

        if (string.IsNullOrWhiteSpace(guid)) return;

        foreach (var model in GetAll<ModelAsset>().ToList()) {

            var usesMaterial = model.MaterialPaths.Any(value => string.Equals(NormalizeReference<MaterialAsset>(value), guid, StringComparison.OrdinalIgnoreCase));
            if (!usesMaterial) continue;

            model.ApplySettings();
            model.InvalidateThumbnail();
            Preview.UpdateThumbnail(model);
        }
    }

    private static string NormalizeReferenceByType(string typeName, string value) => typeName switch {
        "ShaderAsset" => NormalizeReference<ShaderAsset>(value),
        "TextureAsset" => NormalizeReference<TextureAsset>(value),
        "ModelAsset" => NormalizeReference<ModelAsset>(value),
        "AnimationAsset" => NormalizeReference<AnimationAsset>(value),
        "MaterialAsset" => NormalizeReference<MaterialAsset>(value),
        "ScriptAsset" => NormalizeReference<ScriptAsset>(value),
        _ => value
    };

    private static bool ShouldIgnoreChange(string normalizedPath) {

        if (!_ignoredChanges.TryGetValue(normalizedPath, out var remaining) || remaining <= 0) return false;

        if (remaining == 1)
            _ignoredChanges.Remove(normalizedPath);
        else
            _ignoredChanges[normalizedPath] = remaining - 1;

        return true;
    }
}
