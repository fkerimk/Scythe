using System.Reflection;
using Raylib_cs;

internal static class BackgroundScripts {

    private sealed class RuntimeEntry {
        public string Guid = "";
        public string Path = "";
        public ScriptAsset? Asset;
        public ScytheScript? Instance;
        public ScytheScript? HotReloadInstance;
        public bool WasStarted;

        public bool Matches(string guid, string path) =>
            string.Equals(Guid, guid, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path, path, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly List<RuntimeEntry> Entries = [];
    private static Obj? _boundRoot;
    private static Obj? _pendingLevelRoot;

    public static void Initialize() {

        Entries.Clear();
        _boundRoot = null;
        _pendingLevelRoot = null;
        RebuildEntries();
        ScheduleLevelLoad(Core.ActiveLevel?.Root);
    }

    public static void Shutdown() {

        foreach (var entry in Entries)
            entry.Instance = null;

        Entries.Clear();
        _boundRoot = null;
        _pendingLevelRoot = null;
    }

    public static void ScheduleLevelLoad(Obj? root) => _pendingLevelRoot = root;

    public static void HandlePendingLevelLoad() {

        if (_pendingLevelRoot == null) return;
        if (_pendingLevelRoot == _boundRoot) {
            _pendingLevelRoot = null;
            return;
        }

        RebuildEntries();
        _boundRoot = _pendingLevelRoot;
        _pendingLevelRoot = null;

        foreach (var entry in Entries) {
            if (!EnsureInstance(entry)) continue;
            if (entry.Instance == null || _boundRoot == null) continue;

            entry.Instance.Obj = _boundRoot;
            entry.Instance.Start();
            entry.WasStarted = true;
        }
    }

    public static void Logic(float dt) {

        if ((!CommandLine.Runtime && !Core.IsPlaying) || _boundRoot == null) return;

        var boundRoot = _boundRoot;
        var entries = Entries.ToArray();

        foreach (var entry in entries) {
            if (_boundRoot != boundRoot) break;
            if (!EnsureInstance(entry)) continue;
            if (entry.Instance == null) continue;

            entry.Instance.Obj = boundRoot;
            entry.Instance.Loop(dt);
        }
    }

    public static void PrepareForHotReload() {

        foreach (var entry in Entries) {
            entry.HotReloadInstance = entry.Instance;
            entry.Instance = null;
        }
    }

    public static void RefreshAfterHotReload() {

        foreach (var entry in Entries)
            EnsureInstance(entry);
    }

    public static void ApplyConfigToScripts(ScriptAsset asset) {

        foreach (var entry in Entries) {
            if (entry.Asset?.ScriptType == null || asset.ScriptType == null) continue;
            if (!string.Equals(entry.Asset.GUID, asset.GUID, StringComparison.OrdinalIgnoreCase)
                && !entry.Asset.ScriptType.IsSubclassOf(asset.ScriptType))
                continue;
            if (entry.Instance == null || asset.ScriptType == null) continue;

            ApplyConfigValues(entry.Instance, entry.Asset);
        }
    }

    private static void RebuildEntries() {

        var guids = ProjectConfig.Current.BackgroundScripts ?? [];
        var paths = ProjectConfig.Current.BackgroundScriptPaths ?? [];
        var rebuilt = new List<RuntimeEntry>();

        for (var i = 0; i < guids.Length; i++) {
            var guid = guids[i] ?? "";
            var path = i < paths.Length ? paths[i] ?? "" : "";
            if (string.IsNullOrWhiteSpace(guid) && string.IsNullOrWhiteSpace(path)) continue;

            var resolvedGuid = guid;
            var resolvedPath = path;
            var asset = AssetManager.ResolveReference<ScriptAsset>(ref resolvedGuid, ref resolvedPath)
                ?? AssetManager.Get<ScriptAsset>(guid)
                ?? AssetManager.Get<ScriptAsset>(path)
                ?? AssetManager.GetOrImport<ScriptAsset>(path);
            if (asset == null || !asset.IsLoaded || asset.ScriptType == null) continue;

            var entry = Entries.FirstOrDefault(existing => existing.Matches(resolvedGuid, resolvedPath))
                ?? Entries.FirstOrDefault(existing => existing.Asset != null && string.Equals(existing.Asset.GUID, asset.GUID, StringComparison.OrdinalIgnoreCase))
                ?? new RuntimeEntry();

            entry.Guid = resolvedGuid;
            entry.Path = resolvedPath;
            entry.Asset = asset;
            rebuilt.Add(entry);
        }

        Entries.Clear();
        Entries.AddRange(rebuilt);
    }

    private static bool EnsureInstance(RuntimeEntry entry) {

        if (entry.Asset == null || !entry.Asset.IsLoaded || entry.Asset.ScriptType == null) return false;
        if (entry.Instance != null) return true;

        entry.Instance = Activator.CreateInstance(entry.Asset.ScriptType) as ScytheScript;
        if (entry.Instance == null) return false;

        if (entry.HotReloadInstance != null) {
            CopyScriptFields(entry.HotReloadInstance, entry.Instance);
            entry.WasStarted = true;
            entry.HotReloadInstance = null;
        } else {
            ApplyConfigValues(entry.Instance, entry.Asset);
        }

        if (_boundRoot != null)
            entry.Instance.Obj = _boundRoot;

        return true;
    }

    private static void ApplyConfigValues(ScytheScript instance, ScriptAsset asset) {

        foreach (var field in ScriptFieldUtility.GetFields(asset.ScriptType!, ScriptFieldStorageKind.Config))
            field.SetValue(instance, asset.GetConfigFieldValue(field));
    }

    private static void CopyScriptFields(object source, object target) {

        var sourceFields = source.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .ToDictionary(field => field.Name, StringComparer.Ordinal);
        var targetFields = target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        foreach (var targetField in targetFields) {
            if (targetField.IsInitOnly) continue;
            if (!sourceFields.TryGetValue(targetField.Name, out var sourceField)) continue;
            if (!string.Equals(sourceField.FieldType.FullName, targetField.FieldType.FullName, StringComparison.Ordinal)) continue;

            var sourceValue = sourceField.GetValue(source);
            if (!CanAssignHotReloadValue(targetField.FieldType, sourceValue)) continue;

            targetField.SetValue(target, sourceValue);
        }
    }

    private static bool CanAssignHotReloadValue(Type targetType, object? value) {

        if (value == null) return !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null;

        return targetType.IsInstanceOfType(value);
    }
}
