using System.Collections.Concurrent;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
#if !SCYTHE_RUNTIME_BUILD
using Nito.AsyncEx;
using System.Reactive.Linq;
using System.Reactive.Subjects;
#endif
using static Raylib_cs.Raylib;

internal static class AssetManager {
    private sealed class ImportedAnimationTrackCacheEntry {
        public string Signature = "";
        public List<AnimationClip> Tracks = [];
    }

    private const string BuiltInStoredPrefix = "Built In";

    private static readonly Dictionary<string, Asset>     Assets         = new();
    private static readonly Dictionary<string, Asset>     GuidLookup     = new();
    private static readonly List<FileSystemWatcher>       Watchers       = [];
    private static readonly Dictionary<string, Asset>     PathLookup     = new();
    private static readonly Dictionary<Type, List<Asset>> TypeCache      = new();
    private static readonly Dictionary<string, ImportedAnimationTrackCacheEntry> ImportedAnimationTrackCache = new();
    private static readonly HashSet<string>               FailedImports   = [];
    private static readonly ConcurrentQueue<Action>       PendingActions = new();
#if !SCYTHE_RUNTIME_BUILD
    private static readonly Subject<string>               ImportRequests = new();
#endif
    private static readonly ImportBinding[]               ImportBindings = CreateImportBindings();
    private static readonly Dictionary<string, PickerBinding> PickerBindings = CreatePickerBindings();
    private static readonly HashSet<string>               _textureImportsInProgress = [];
    private static readonly List<string>                  _pendingFiles  = new();
    private static readonly Dictionary<string, int>       _ignoredChanges = new();
#if !SCYTHE_RUNTIME_BUILD
    private static IDisposable?                           _importRequestSubscription;
#endif
    private static BackgroundTask?                        _importTask;
    private static bool                                   _isInitializing;

    public static bool IsInitializing => _isInitializing;

    public static void Update() {
        while (PendingActions.TryDequeue(out var action)) action();

        if (_importTask != null && _importTask.IsDone) _importTask = null;

        if (_importTask == null && _pendingFiles.Count > 0)
            StartImportTask();
    }

    public static void Init() {

        _isInitializing = true;

        try {
            EnsureImportPipeline();

            foreach (var watcher in Watchers) watcher.Dispose();
            Watchers.Clear();
            PathLookup.Clear();
            GuidLookup.Clear();
            Assets.Clear();
            TypeCache.Clear();
            ImportedAnimationTrackCache.Clear();
            FailedImports.Clear();

            var builtInPath = "";
            bool hasBuiltIn = PathUtil.GetPath("Collection", out builtInPath);
            var builtInFiles = hasBuiltIn ? Directory.GetFiles(builtInPath, "*.*", SearchOption.AllDirectories).Where(IsVisibleAssetSourcePath).ToList() : new List<string>();

            var modPath = ScytheConfig.Current.Project;
            EnsureImportsRoot();
            var modFiles = Directory.Exists(modPath)
                ? Directory.GetFiles(modPath, "*.*", SearchOption.AllDirectories)
                           .Where(IsVisibleAssetSourcePath)
                           .Where(f => !f.Contains("/Assembly/") && !f.Contains("\\Assembly\\"))
                           .Where(f => !IsImportsPath(f))
                           .ToList()
                : new List<string>();

            var totalFiles = builtInFiles.Concat(modFiles).ToList();
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

            if (hasBuiltIn) CreateWatcher(builtInPath, "*.*", HandleFileChange, HandleFileDelete);
            if (Directory.Exists(modPath)) CreateWatcher(modPath, "*.*", HandleFileChange, HandleFileDelete);

        } finally {
            _isInitializing = false;
        }
    }

    private static void ScanDirectory(string path) { }

    private static string GetImportsRoot() => Path.Combine(ScytheConfig.Current.Project, "Imports");

    private static void EnsureImportsRoot() {

        var projectPath = ScytheConfig.Current.Project;
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath)) return;

        Directory.CreateDirectory(GetImportsRoot());
        Directory.CreateDirectory(Path.Combine(GetImportsRoot(), "Textures"));
        Directory.CreateDirectory(Path.Combine(GetImportsRoot(), "Models"));
    }

    private static bool IsImportsPath(string path) {

        var importsRoot = GetImportsRoot();
        if (string.IsNullOrWhiteSpace(importsRoot)) return false;

        var full = Path.GetFullPath(path).Replace('\\', '/');
        var importsFull = Path.GetFullPath(importsRoot).Replace('\\', '/') + "/";

        return full.StartsWith(importsFull, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeWatchPath(string path) =>
        Path.GetFullPath(path).Replace('\\', '/').ToLowerInvariant();

    private static List<KeyValuePair<string, Asset>> GetAssetsWatchingPath(string path) {
        var normalized = NormalizeWatchPath(path);
        return Assets.Where(kvp => kvp.Value.GetWatchedFiles().Any(watched => NormalizeWatchPath(watched) == normalized)).ToList();
    }

    private static void ReloadTrackedAsset(Asset asset) {
        asset.Unload();
        RefreshDependentAssets(asset);
        ReloadDependentComponents(asset);
    }

    private static void EnsureImportPipeline() {
#if !SCYTHE_RUNTIME_BUILD
        if (_importRequestSubscription != null) return;

        _importRequestSubscription = ImportRequests
            .Synchronize()
            .Buffer(TimeSpan.FromMilliseconds(250))
            .Where(batch => batch.Count > 0)
            .Subscribe(batch => EnqueueWatcherAction(() => QueueImportTargets(batch)));
#endif
    }

    private static void QueueImportTargets(IEnumerable<string> files) {
        lock (_pendingFiles) {
            foreach (var file in files)
            foreach (var importTarget in GetImportTargets(file))
                if (!_pendingFiles.Any(pending => string.Equals(pending, importTarget, StringComparison.OrdinalIgnoreCase)))
                    _pendingFiles.Add(importTarget);
        }
    }

    private static List<string> DequeuePendingFiles() {
        lock (_pendingFiles) {
            var filesToImport = new List<string>(_pendingFiles);
            _pendingFiles.Clear();
            return filesToImport;
        }
    }

    private static void EnqueueWatcherAction(Action action) =>
        PendingActions.Enqueue(() => SafeExec.Try(action));

    private static string GetTypePrefix(Type type) => type.Name + "::";

    private static string NormalizeLookupValue(string value) =>
        value.Replace('\\', '/').ToLowerInvariant();

    private static string NormalizeLookupPath(string path) =>
        NormalizeLookupValue(Path.GetFullPath(path));

    private static string NormalizeLookupName(string path) =>
        Path.GetFileNameWithoutExtension(path).ToLowerInvariant();

    private static string GetBuiltInLookupPath(string path) =>
        NormalizeLookupPath(Path.Combine(PathUtil.GetBuiltInCollectionRoot(), path));

    private static string GetProjectLookupPath(string path) =>
        Path.GetRelativePath(ScytheConfig.Current.Project, path).Replace('\\', '/').ToLowerInvariant();

    private static string GetProjectCollectionLookupPath(string path) =>
        Path.GetRelativePath(Path.Combine(ScytheConfig.Current.Project, "Collections"), path).Replace('\\', '/').ToLowerInvariant();

    private static string GetBuiltInStoredPath(string fullPath) =>
        $"{BuiltInStoredPrefix}/{Path.GetRelativePath(PathUtil.GetBuiltInCollectionRoot(), fullPath).Replace('\\', '/')}";

    private static string BuildLookupKey(Type type, string value) =>
        GetTypePrefix(type) + value;

    private static void AddPathLookup(Type type, string value, Asset asset) =>
        PathLookup[BuildLookupKey(type, value)] = asset;

    private static void AddGuidLookup(Type type, string guid, Asset asset) {
        if (!string.IsNullOrWhiteSpace(guid))
            GuidLookup[BuildLookupKey(type, guid.ToLowerInvariant())] = asset;
    }

    private static T? GetLoadedLookupAsset<T>(Dictionary<string, Asset> source, string key) where T : Asset =>
        source.TryGetValue(key, out var asset) && asset is T { IsLoaded: true } typedAsset ? typedAsset : null;

    private static ImportBinding[] CreateImportBindings() => [
        CreateImportBinding<LevelAsset>(AssetPaths.IsLevel, ImportLevel),
        CreateImportBinding<PrefabAsset>(AssetPaths.IsPrefab, ImportPrefab),
        CreateImportBinding<MaterialAsset>(AssetPaths.IsMaterial, ImportMaterial),
        CreateImportBinding<TextureAsset>(AssetFilePatterns.IsTexture, ImportTexture),
        CreateImportBinding<ModelAsset>(AssetFilePatterns.IsModel, ImportModel),
        CreateImportBinding<ScriptAsset>(AssetFilePatterns.IsScript, ImportScript),
        CreateImportBinding<ShaderAsset>(AssetFilePatterns.IsShader, ImportShader)
    ];

    private static ImportBinding CreateImportBinding<T>(Func<string, bool> matches, Action<string> import) where T : Asset =>
        new((path, importIfMissingOnly) => TryEnsureImported<T>(path, matches, import, importIfMissingOnly));

    private static Dictionary<string, PickerBinding> CreatePickerBindings() =>
        new(StringComparer.Ordinal) {
            ["ShaderAsset"] = CreatePickerBinding<ShaderAsset>(),
            ["TextureAsset"] = CreatePickerBinding<TextureAsset>(),
            ["ModelAsset"] = CreatePickerBinding<ModelAsset>(),
            ["AnimationAsset"] = CreatePickerBinding<AnimationAsset>(),
            ["MaterialAsset"] = CreatePickerBinding<MaterialAsset>(),
            ["ScriptAsset"] = CreatePickerBinding<ScriptAsset>(),
            ["PrefabAsset"] = CreatePickerBinding<PrefabAsset>(),
            ["LevelAsset"] = CreatePickerBinding<LevelAsset>()
        };

    private static PickerBinding CreatePickerBinding<T>() where T : Asset =>
        new(() => GetNames<T>(), path => GetOrImport<T>(path)?.GUID ?? "");

    private static void HandleFileChange(string file) {

        if (IsImportsPath(file)) return;
        if (!IsVisibleAssetSourcePath(file)) return;

        ClearTransientImportState(file);

        var normalized = NormalizeWatchPath(file);
        if (ShouldIgnoreChange(normalized)) return;

        foreach (var (_, asset) in GetAssetsWatchingPath(file))
            ReloadTrackedAsset(asset);

#if !SCYTHE_RUNTIME_BUILD
        ImportRequests.OnNext(file);
#endif
    }

    private static void StartImportTask() {

        var filesToImport = DequeuePendingFiles();
        if (filesToImport.Count == 0) return;

        _importTask = Tasks.Run("Importing Assets", task => {

            int current = 0;
            foreach (var file in filesToImport) {

#if !SCYTHE_RUNTIME_BUILD
                var done = new AsyncManualResetEvent(false);
#else
                var done = new ManualResetEvent(false);
#endif
                Tasks.RunOnMainThread(() => {
                    try { ImportFile(file); }
                    finally { done.Set(); }
                });
#if !SCYTHE_RUNTIME_BUILD
                done.Wait();
#else
                done.WaitOne();
#endif
                current++;
                task.Progress = (float)current / filesToImport.Count;
                task.Status = Path.GetFileName(file);
            }

            task.Progress = 1f;
            task.Status = "Success";
        });
    }

    private static void HandleFileDelete(string file) {

        if (IsImportsPath(file)) return;
        if (!IsVisibleAssetSourcePath(file)) return;

        ClearTransientImportState(file);
        UnloadAsset(file);
    }

    private static void ImportFile(string file) {

        file = Path.GetFullPath(file);

        if (!File.Exists(file) && !AssetPaths.IsMaterial(file)) return;
        if (!IsVisibleAssetSourcePath(file)) return;
        if (TryImportKnownAsset(file)) return;

        if (AssetPaths.IsJson(file)) {
            var assetFile = file[..^5];
            if (File.Exists(assetFile)) ImportFile(assetFile);
        }
    }

    private static void UnloadAsset(string file) {

        var toRemove = GetAssetsWatchingPath(file);

        foreach (var kvp in toRemove) {
            ReloadTrackedAsset(kvp.Value);
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

        if (AssetPaths.IsMaterial(file)) {

            yield return file;
            yield break;
        }

        if (AssetPaths.IsJson(file)) {

            var owner = file[..^5];
            if (File.Exists(owner)) yield return owner;
            yield break;
        }

        yield return file;
    }

    private static bool IsVisibleAssetSourcePath(string path) {

        var fullPath = Path.GetFullPath(path);
        var relativePath = fullPath;

        var projectRoot = string.IsNullOrWhiteSpace(ScytheConfig.Current.Project) ? "" : Path.GetFullPath(ScytheConfig.Current.Project);
        var builtInRoot = Path.GetFullPath(PathUtil.GetBuiltInCollectionRoot());

        if (!string.IsNullOrWhiteSpace(projectRoot) && fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            relativePath = Path.GetRelativePath(projectRoot, fullPath);
        else if (fullPath.StartsWith(builtInRoot, StringComparison.OrdinalIgnoreCase))
            relativePath = Path.GetRelativePath(builtInRoot, fullPath);

        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (var part in parts) {

            if (string.IsNullOrWhiteSpace(part)) continue;
            if (part.StartsWith(".", StringComparison.Ordinal)) return false;
        }

        return true;
    }

    private static void CreateWatcher(string path, string filter, Action<string> onImport, Action<string> onUnload) {

        var watcher = new FileSystemWatcher(path, filter) { IncludeSubdirectories = true };

        watcher.NotifyFilter =  NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime;
        watcher.Changed += (_, e) => EnqueueWatcherAction(() => onImport(e.FullPath));
        watcher.Created += (_, e) => EnqueueWatcherAction(() => onImport(e.FullPath));
        watcher.Deleted += (_, e) => EnqueueWatcherAction(() => onUnload(e.FullPath));
        watcher.Renamed += (_, e) => EnqueueWatcherAction(() => {
            onUnload(e.OldFullPath);
            onImport(e.FullPath);
        });
        watcher.EnableRaisingEvents = true;

        Watchers.Add(watcher);
    }

    private static void ImportModel(string file) {

        var oldJson = Path.Combine(Path.GetDirectoryName(file)!, Path.GetFileNameWithoutExtension(file) + ".json");
        var newJson = file + ".json";

        if (File.Exists(oldJson) && !File.Exists(newJson) && oldJson != newJson) SafeExec.Try(() => File.Move(oldJson, newJson));

        if (!File.Exists(newJson)) SafeExec.Try(() => JsonFile.WriteIndented(newJson, new ModelAsset.ModelSettings()));

        GetOrLoad<ModelAsset>(file);

        var model = Get<ModelAsset>(file);
        if (model is { Animations.Count: > 0 })
            GetOrLoad<AnimationAsset>(file);
    }

    private static void ImportScript(string file) => GetOrLoad<ScriptAsset>(file);

    private static void ImportMaterial(string file) {

        if (!File.Exists(file) || new FileInfo(file).Length < 5) SafeExec.Try(() => JsonFile.WriteIndented(file, new MaterialAsset.MaterialData()));

        GetOrLoad<MaterialAsset>(file);
    }

    private static void ImportLevel(string file) => GetOrLoad<LevelAsset>(file);
    private static void ImportPrefab(string file) => GetOrLoad<PrefabAsset>(file);

    private static void ImportTexture(string file) => GetOrLoad<TextureAsset>(file);

    private static void ImportShader(string file) {
        GetOrLoad<ShaderAsset>(file);
    }

    public static List<AnimationClip> GetImportedAnimationTracks(string file) {

        if (!File.Exists(file)) return [];

        var settings = JsonFile.ReadOrDefault(file + ".json", new ModelAsset.ModelSettings());
        if (string.IsNullOrWhiteSpace(settings.GUID))
            settings.GUID = Guid.NewGuid().ToString("N");

        var importedFile = GetImportedModelFile(file, settings.GUID);
        var cacheKey = Path.GetFullPath(file).Replace('\\', '/');
        var signature = BuildImportedAnimationTrackSignature(file, importedFile);

        if (ImportedAnimationTrackCache.TryGetValue(cacheKey, out var cached) && string.Equals(cached.Signature, signature, StringComparison.Ordinal))
            return cached.Tracks;

        List<AnimationClip> tracks;
        try {
            if (File.Exists(importedFile) && CompiledAssetCache.LoadModel(importedFile, out _, out _, out _, out _, out var compiledAnimations))
                tracks = compiledAnimations;
            else {

#if !SCYTHE_RUNTIME_BUILD
                tracks = AssimpLoader.Load(file).Animations;
#else
                tracks = [];
#endif
            }
        } catch (Exception e) {
            TraceLog(Raylib_cs.TraceLogLevel.Error, $"Failed to load animation tracks {file}: {e.Message}");
            tracks = [];
        }

        ImportedAnimationTrackCache[cacheKey] = new ImportedAnimationTrackCacheEntry {
            Signature = signature,
            Tracks = tracks
        };

        return tracks;
    }

    private static string BuildImportedAnimationTrackSignature(string sourceFile, string importedFile) {

        var jsonFile = sourceFile + ".json";
        var sourceTicks = File.Exists(sourceFile) ? File.GetLastWriteTimeUtc(sourceFile).Ticks : 0;
        var importedTicks = File.Exists(importedFile) ? File.GetLastWriteTimeUtc(importedFile).Ticks : 0;
        var jsonTicks = File.Exists(jsonFile) ? File.GetLastWriteTimeUtc(jsonFile).Ticks : 0;
        return $"{sourceTicks}:{importedTicks}:{jsonTicks}";
    }

    private static string BuildFailedImportKey(string file, Type type) =>
        $"{NormalizeLookupPath(file)}::{type.Name}";

    private static void ClearTransientImportState(string file) {

        ImportedAnimationTrackCache.Remove(Path.GetFullPath(file).Replace('\\', '/'));

        var normalizedPath = NormalizeLookupPath(file);
        var keysToRemove = FailedImports.Where(key => key.StartsWith(normalizedPath + "::", StringComparison.Ordinal)).ToList();
        foreach (var key in keysToRemove) FailedImports.Remove(key);
    }

    private static void GetOrLoad<T>(string file) where T : Asset, new() {

        var loadKey = BuildFailedImportKey(file, typeof(T));
        if (FailedImports.Contains(loadKey)) return;

        var key = $"{file.ToLowerInvariant()}::{typeof(T).Name}";
        var isNew = false;

        if (!Assets.TryGetValue(key, out var asset)) {

            asset       = new T { File = file };
            Assets[key] = asset;
            isNew = true;
        }

        if (!asset.IsLoaded && !asset.Load()) {
            FailedImports.Add(loadKey);
            return;
        }

        FailedImports.Remove(loadKey);

        AddToMaps<T>(file, asset);
        NormalizeInternalReferences(asset);
        SyncDependentComponentReferences(asset);
        RefreshDependentAssets(asset);

        if (!isNew) ReloadDependentComponents(asset);
    }

    private static void AddToMaps<T>(string file, Asset asset) {

        RemoveFromMaps(asset);

        var assetType = typeof(T);
        var full = NormalizeLookupPath(file);
        var name = NormalizeLookupName(file);

        AddPathLookup(assetType, full, asset);
        AddPathLookup(assetType, name, asset);
        AddGuidLookup(assetType, asset.GUID, asset);

        if (full.Contains("/collection/", StringComparison.InvariantCultureIgnoreCase)) {

            var idx = full.IndexOf("/collection/", StringComparison.InvariantCultureIgnoreCase);
            var relBuiltIn = full[(idx + 1)..];
            AddPathLookup(assetType, relBuiltIn, asset);
            AddPathLookup(assetType, NormalizeLookupValue(GetBuiltInStoredPath(file)), asset);
        }

        if (full.Contains(ScytheConfig.Current.Project.Replace('\\', '/'), StringComparison.InvariantCultureIgnoreCase)) {

            AddPathLookup(assetType, GetProjectLookupPath(file), asset);

            var collectionsRoot = Path.GetFullPath(Path.Combine(ScytheConfig.Current.Project, "Collections"));
            if (Path.GetFullPath(file).StartsWith(collectionsRoot, StringComparison.OrdinalIgnoreCase))
                AddPathLookup(assetType, GetProjectCollectionLookupPath(file), asset);
        }

        if (!TypeCache.TryGetValue(assetType, out var list)) {

            list                 = [];
            TypeCache[assetType] = list;
        }

        if (!list.Contains(asset)) list.Add(asset);
    }

    public static T? Get<T>(string? name) where T : Asset {

        if (string.IsNullOrEmpty(name)) return null;

        var assetType = typeof(T);
        var req = NormalizeLookupValue(name);

        var guidAsset = GetLoadedLookupAsset<T>(GuidLookup, BuildLookupKey(assetType, req));
        if (guidAsset != null) return guidAsset;

        var pathAsset = GetLoadedLookupAsset<T>(PathLookup, BuildLookupKey(assetType, req));
        if (pathAsset != null) return pathAsset;

        if (req.Contains(':') || req.StartsWith('/')) return null;

        return GetLoadedLookupAsset<T>(PathLookup, BuildLookupKey(assetType, GetBuiltInLookupPath(name)));
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
        EnsureImported(fullPath, importIfMissingOnly: true);
    }

    private static bool TryImportKnownAsset(string file) =>
        EnsureImported(file, importIfMissingOnly: false);

    private static bool EnsureImported(string fullPath, bool importIfMissingOnly) {

        foreach (var binding in ImportBindings)
            if (binding.Ensure(fullPath, importIfMissingOnly))
                return true;

        return false;
    }

    private static bool TryEnsureImported<T>(string fullPath, Func<string, bool> matches, Action<string> import, bool importIfMissingOnly = true) where T : Asset {

        if (!matches(fullPath)) return false;
        if (!importIfMissingOnly || Get<T>(fullPath) == null) import(fullPath);
        return true;
    }

    public static string GetImportedTextureFile(string sourceFile, string guid, AssetSidecarData.TextureImportSettings? settings = null) =>
        EnsureImportedCache(sourceFile, guid, "TextureAsset", settings);

    public static string GetImportedModelFile(string sourceFile, string guid) =>
        EnsureImportedCache(sourceFile, guid, "ModelAsset");

    private static string EnsureImportedCache(string sourceFile, string guid, string type, AssetSidecarData.TextureImportSettings? textureSettings = null) {

        sourceFile = Path.GetFullPath(sourceFile);

        if (string.IsNullOrWhiteSpace(guid) || !File.Exists(sourceFile) || IsImportsPath(sourceFile)) return sourceFile;

        var isBuiltInFile = sourceFile.Contains("/collection/", StringComparison.OrdinalIgnoreCase) || sourceFile.Contains("\\collection\\", StringComparison.OrdinalIgnoreCase);
        if (isBuiltInFile && type != "ModelAsset") return sourceFile;

        EnsureImportsRoot();

        try {

            return type switch {
                "TextureAsset" => EnsureImportedTextureCache(sourceFile, guid, textureSettings ?? new AssetSidecarData.TextureImportSettings()),
                "ModelAsset" => EnsureImportedModelCache(sourceFile, guid),
                _ => sourceFile
            };

        } catch {

            return sourceFile;
        }
    }

    private static string EnsureImportedTextureCache(string sourceFile, string guid, AssetSidecarData.TextureImportSettings settings) {

        var folder = Path.Combine(GetImportsRoot(), "Textures");
        var importedPath = Path.Combine(folder, guid + ".stex");

        if (CommandLine.Runtime) return importedPath;

        if (IsTextureImportInProgress(guid))
            return File.Exists(importedPath) ? importedPath : sourceFile;

        DeleteLegacyTextureImports(folder, guid, importedPath);
        if (IsTextureCacheCurrent(sourceFile, importedPath, settings)) return importedPath;
        RegisterInternalWrite(importedPath);
        importedPath = CompiledAssetCache.EnsureTextureCache(sourceFile, importedPath, settings);

        return File.Exists(importedPath) ? importedPath : sourceFile;
    }

    private static string EnsureImportedModelCache(string sourceFile, string guid) {

        var importedPath = Path.Combine(GetImportsRoot(), "Models", guid + ".scymodel");

        if (CommandLine.Runtime) return importedPath;

        DeleteLegacyModelImports(guid);
        RegisterInternalWrite(importedPath);
#if !SCYTHE_RUNTIME_BUILD
        importedPath = CompiledAssetCache.EnsureModelCache(sourceFile, importedPath);
#endif
        return File.Exists(importedPath) ? importedPath : sourceFile;
    }

    private static void DeleteLegacyTextureImports(string folder, string guid, string keepPath) {

        foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".webp", ".avif", ".dds", ".stex", ".runtime.png", ".import.json" }) {

            var path = Path.Combine(folder, guid + ext);
            if (!File.Exists(path)) continue;
            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(keepPath), StringComparison.OrdinalIgnoreCase)) continue;

            RegisterInternalWrite(path);
            File.Delete(path);
        }
    }

    private static void DeleteLegacyModelImports(string guid) {

        var modelsRoot = Path.Combine(GetImportsRoot(), "Models");
        var legacyMeta = Path.Combine(modelsRoot, guid + ".import.json");
        if (File.Exists(legacyMeta)) {

            RegisterInternalWrite(legacyMeta);
            File.Delete(legacyMeta);
        }

        var legacyFolder = Path.Combine(modelsRoot, guid);
        if (!Directory.Exists(legacyFolder)) return;

        foreach (var file in Directory.GetFiles(legacyFolder, "*", SearchOption.AllDirectories)) RegisterInternalWrite(file);
        Directory.Delete(legacyFolder, true);
    }

    private static bool IsTextureCacheCurrent(string sourceFile, string importedPath, AssetSidecarData.TextureImportSettings settings) {

        return CompiledAssetCache.IsTextureCacheCurrent(sourceFile, importedPath, settings);
    }

    private static IEnumerable<string> GetModelDependencyFiles(string sourceFile) {

        var ext = Path.GetExtension(sourceFile).ToLowerInvariant();

        switch (ext) {

            case ".obj":
                foreach (var file in GetObjDependencyFiles(sourceFile)) yield return file;
                break;

            case ".gltf":
                foreach (var file in GetGltfDependencyFiles(sourceFile)) yield return file;
                break;
        }
    }

    private static IEnumerable<string> GetObjDependencyFiles(string sourceFile) {

        var sourceDir = Path.GetDirectoryName(sourceFile);
        if (string.IsNullOrWhiteSpace(sourceDir) || !File.Exists(sourceFile)) yield break;

        foreach (var rawLine in File.ReadLines(sourceFile)) {

            var line = rawLine.Trim();
            if (!line.StartsWith("mtllib ", StringComparison.OrdinalIgnoreCase)) continue;

            foreach (var mtlRef in line["mtllib ".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {

                var full = Path.GetFullPath(Path.Combine(sourceDir, mtlRef));
                if (!File.Exists(full)) continue;

                yield return full;

                foreach (var texture in GetMtlTextureDependencies(full)) yield return texture;
            }
        }
    }

    private static IEnumerable<string> GetMtlTextureDependencies(string mtlFile) {

        var dir = Path.GetDirectoryName(mtlFile);
        if (string.IsNullOrWhiteSpace(dir) || !File.Exists(mtlFile)) yield break;

        foreach (var rawLine in File.ReadLines(mtlFile)) {

            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;

            var firstSpace = line.IndexOf(' ');
            if (firstSpace <= 0) continue;

            var keyword = line[..firstSpace];
            if (!keyword.StartsWith("map_", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(keyword, "bump", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(keyword, "disp", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(keyword, "decal", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(keyword, "refl", StringComparison.OrdinalIgnoreCase)) continue;

            var tokens = line[(firstSpace + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length == 0) continue;

            var candidate = tokens[^1];
            var full = Path.GetFullPath(Path.Combine(dir, candidate));
            if (File.Exists(full)) yield return full;
        }
    }

    private static IEnumerable<string> GetGltfDependencyFiles(string sourceFile) {

        var dir = Path.GetDirectoryName(sourceFile);
        if (string.IsNullOrWhiteSpace(dir) || !File.Exists(sourceFile)) yield break;

        JObject? gltf;

        try {

            gltf = JsonConvert.DeserializeObject<JObject>(File.ReadAllText(sourceFile));

        } catch {

            yield break;
        }

        if (gltf == null) yield break;

        foreach (var token in gltf.SelectTokens("$.buffers[*].uri").Concat(gltf.SelectTokens("$.images[*].uri"))) {

            var uri = token.Value<string>();
            if (string.IsNullOrWhiteSpace(uri) || uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || uri.Contains("://", StringComparison.Ordinal)) continue;

            var full = Path.GetFullPath(Path.Combine(dir, uri));
            if (File.Exists(full)) yield return full;
        }
    }

    public static void RegisterInternalWrite(string path, int ignoredEvents = 4) {

        var normalized = NormalizeWatchPath(path);
        _ignoredChanges[normalized] = _ignoredChanges.GetValueOrDefault(normalized, 0) + ignoredEvents;
    }

    public static string GetStoredPath(string? file) {

        if (string.IsNullOrWhiteSpace(file)) return "";

        var full = Path.GetFullPath(file);
        var modPath = ScytheConfig.Current.Project;
        var projectCollectionsPath = Path.Combine(modPath, "Collections");
        var builtInRoot = PathUtil.GetBuiltInCollectionRoot();

        if (full.StartsWith(projectCollectionsPath, StringComparison.OrdinalIgnoreCase))
            return Path.GetRelativePath(projectCollectionsPath, full).Replace('\\', '/');

        if (full.StartsWith(modPath, StringComparison.OrdinalIgnoreCase))
            return Path.GetRelativePath(modPath, full).Replace('\\', '/');

        if (full.StartsWith(builtInRoot, StringComparison.OrdinalIgnoreCase))
            return GetBuiltInStoredPath(full);

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
        var builtInRoot = PathUtil.GetBuiltInCollectionRoot();

        return list.Cast<T>()
                   .Select(a => {

                           var full = Path.GetFullPath(a.File);
                           var rel  = full;

                           if (full.StartsWith(Path.Combine(modPath, "Collections"), StringComparison.OrdinalIgnoreCase))
                               rel = Path.GetRelativePath(Path.Combine(modPath, "Collections"), full);
                           else if (full.StartsWith(modPath, StringComparison.OrdinalIgnoreCase))
                               rel = Path.GetRelativePath(modPath, full);
                           else if (full.StartsWith(builtInRoot, StringComparison.OrdinalIgnoreCase))
                               rel = GetBuiltInStoredPath(full);

                           return (Path.GetFileNameWithoutExtension(a.File), rel.Replace('\\', '/'), a.GUID);

                       }
                   )
                   .OrderBy(n => n.Item1)
                   .ToList();
    }

    public static List<(string Name, string Path, string GUID)> GetNames(string pickerType) =>
        PickerBindings.TryGetValue(pickerType, out var binding) ? binding.GetNames() : [];

    public static string GetGuidForPickerType(string path, string pickerType) =>
        PickerBindings.TryGetValue(pickerType, out var binding) ? binding.GetGuid(path) : "";

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

    public static void ReloadAsset(Asset asset) {

        if (asset == null) return;

        asset.Unload();
        if (!asset.Load()) return;

        NormalizeInternalReferences(asset);
        SyncDependentComponentReferences(asset);
        RefreshDependentAssets(asset);
        ReloadDependentComponents(asset);
    }

    public static bool IsTextureImportInProgress(string? guid) =>
        !string.IsNullOrWhiteSpace(guid) && _textureImportsInProgress.Contains(guid);

    public static void ReimportTextureAsync(TextureAsset texture) {

        if (texture == null || string.IsNullOrWhiteSpace(texture.GUID)) return;
        if (!_textureImportsInProgress.Add(texture.GUID)) return;

        var sourceFile = Path.GetFullPath(texture.File);
        var guid = texture.GUID;
        var settings = (AssetSidecarData.TextureImportSettings)texture.ImportSettings.Clone();
        var importedFolder = Path.Combine(GetImportsRoot(), "Textures");
        var importedPath = Path.Combine(importedFolder, guid + ".stex");

        Tasks.Run($"Import Texture {Path.GetFileName(texture.File)}", task => {
            try {
                task.Status = "Compressing...";
                task.Progress = 0.15f;

                DeleteLegacyTextureImports(importedFolder, guid, importedPath);
                RegisterInternalWrite(importedPath);

                importedPath = CompiledAssetCache.EnsureTextureCache(sourceFile, importedPath, settings);
                if (!File.Exists(importedPath)) {
                    task.Status = "Fail: texture import failed";
                    return;
                }

                task.Progress = 0.85f;
                task.Status = "Reloading...";

                Tasks.RunOnMainThread(() => {
                    try {
                        ReloadAsset(texture);
                    } finally {
                        _textureImportsInProgress.Remove(guid);
                    }
                });

                task.Progress = 1f;
                task.Status = "Success";

            } catch (Exception e) {
                _textureImportsInProgress.Remove(guid);
                task.Status = "Fail: " + e.Message;
            }
        });
    }

    public static void ApplyTextureFilterAsync(TextureAsset texture) {

        if (texture == null || string.IsNullOrWhiteSpace(texture.GUID)) return;
        if (!_textureImportsInProgress.Add(texture.GUID)) return;

        var guid = texture.GUID;
        Tasks.Run($"Update Texture Filter {Path.GetFileName(texture.File)}", task => {
            try {
                task.Status = "Applying...";
                task.Progress = 0.3f;

                Tasks.RunOnMainThread(() => {
                    try {
                        texture.ApplyTextureFilter();
                    } finally {
                        _textureImportsInProgress.Remove(guid);
                    }
                });

                task.Progress = 1f;
                task.Status = "Success";

            } catch (Exception e) {
                _textureImportsInProgress.Remove(guid);
                task.Status = "Fail: " + e.Message;
            }
        });
    }

    public static void DeleteImportedCache(Asset asset) {

        if (asset == null || string.IsNullOrWhiteSpace(asset.ImportedFile) || !File.Exists(asset.ImportedFile)) return;
        if (string.Equals(Path.GetFullPath(asset.ImportedFile), Path.GetFullPath(asset.File), StringComparison.OrdinalIgnoreCase)) return;
        if (!IsImportsPath(asset.ImportedFile)) return;

        RegisterInternalWrite(asset.ImportedFile);
        File.Delete(asset.ImportedFile);
    }

    private static void SyncDependentComponentReferences(Asset asset) {

        if (string.IsNullOrWhiteSpace(asset.GUID)) return;

        foreach (var level in Core.OpenLevels)
            SyncDependentComponentReferences(level.Root, level, asset);
    }

    private static void SyncDependentComponentReferences(Obj obj, Level level, Asset asset) {

        foreach (var component in obj.ComponentEntries.Values) {

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

        foreach (var child in obj.ChildEntries.Values) SyncDependentComponentReferences(child, level, asset);
    }

    private static void ReloadDependentComponents(Obj obj, Asset asset) {

        foreach (var component in obj.ComponentEntries.Values) {

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

        foreach (var child in obj.ChildEntries.Values) ReloadDependentComponents(child, asset);
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

            case PrefabAsset prefab:
                PrefabUtility.RefreshOpenPrefabInstances(prefab.File);
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
        "PrefabAsset" => NormalizeReference<PrefabAsset>(value),
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

    private readonly record struct ImportBinding(Func<string, bool, bool> Ensure);
    private readonly record struct PickerBinding(Func<List<(string Name, string Path, string GUID)>> GetNames, Func<string, string> GetGuid);
}
