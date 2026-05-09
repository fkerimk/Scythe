using Newtonsoft.Json;

internal enum CollectionAssetKind {
    Collection,
    Level,
    Material,
    Model,
    Prefab,
    Script,
    Texture
}

internal sealed class CollectionDataSettings {
    public string TargetPath { get; set; } = "";
    public string Type { get; set; } = "Collection";
}

internal readonly record struct CollectionDataCategory(CollectionAssetKind Kind, string Name, string PickerType);

internal static class CollectionData {

    public const string BuiltInCollectionLabel = "Built In";

    public static readonly CollectionDataCategory[] Categories = [
        new(CollectionAssetKind.Level, "Levels", "LevelAsset"),
        new(CollectionAssetKind.Material, "Materials", "MaterialAsset"),
        new(CollectionAssetKind.Model, "Models", "ModelAsset"),
        new(CollectionAssetKind.Prefab, "Prefabs", "PrefabAsset"),
        new(CollectionAssetKind.Script, "Scripts", "ScriptAsset"),
        new(CollectionAssetKind.Texture, "Textures", "TextureAsset")
    ];

    public static string RootPath => Path.Combine(ScytheConfig.Current.Project, "Collections");
    public static string BuiltInRootPath => PathUtil.GetBuiltInCollectionRoot();

    public static bool IsBuiltInRoot(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Equals(Path.GetFullPath(BuiltInRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    public static bool IsProjectRoot(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Equals(Path.GetFullPath(RootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    public static bool IsUnderRoot(string path) {

        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootPath = Path.GetFullPath(RootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var builtInRootPath = Path.GetFullPath(BuiltInRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return fullPath.Equals(rootPath, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(rootPath + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || fullPath.Equals(builtInRootPath, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(builtInRootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(builtInRootPath + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRoot(string path) => IsProjectRoot(path) || IsBuiltInRoot(path);

    public static IEnumerable<string> EnumerateRootCollections(CollectionAssetKind kind) {

        if (kind == CollectionAssetKind.Collection && Directory.Exists(BuiltInRootPath))
            yield return BuiltInRootPath;

        if (!Directory.Exists(RootPath)) yield break;

        foreach (var path in EnumerateCollections(RootPath, kind))
            yield return path;
    }

    public static string GetCollectionDisplayName(string collectionPath) =>
        IsBuiltInRoot(collectionPath) ? BuiltInCollectionLabel : Path.GetFileName(collectionPath);

    public static void EnsureSettings(string collectionPath) {

        if (!Directory.Exists(collectionPath) || IsRoot(collectionPath)) return;

        var settingsPath = GetSettingsPath(collectionPath);
        if (File.Exists(settingsPath)) return;

        JsonFile.WriteIndented(settingsPath, new CollectionDataSettings());
    }

    public static string GetSettingsPath(string collectionPath) => Path.Combine(collectionPath, "Collection.json");

    public static CollectionDataSettings ReadSettings(string collectionPath) {

        var settingsPath = GetSettingsPath(collectionPath);
        if (!File.Exists(settingsPath)) return new CollectionDataSettings();

        return JsonFile.ReadOrDefault(settingsPath, new CollectionDataSettings());
    }

    public static void SaveSettings(string collectionPath, CollectionDataSettings settings) {

        EnsureSettings(collectionPath);
        JsonFile.WriteIndented(GetSettingsPath(collectionPath), settings);
    }

    public static CollectionAssetKind GetKind(string collectionPath) {

        if (IsRoot(collectionPath)) return CollectionAssetKind.Collection;
        return ParseKind(ReadSettings(collectionPath).Type);
    }

    public static void SetKind(string collectionPath, CollectionAssetKind kind) {

        var settings = ReadSettings(collectionPath);
        settings.Type = GetKindName(kind);
        SaveSettings(collectionPath, settings);
    }

    public static void SetTarget(string collectionPath, string targetPath) {

        var settings = ReadSettings(collectionPath);
        settings.TargetPath = Path.GetRelativePath(collectionPath, targetPath).Replace('\\', '/');
        SaveSettings(collectionPath, settings);
    }

    public static string? GetResolvedTargetPath(string collectionPath) {

        EnsureSettings(collectionPath);

        var settings = ReadSettings(collectionPath);
        if (string.IsNullOrWhiteSpace(settings.TargetPath)) return null;

        var targetPath = Path.GetFullPath(Path.Combine(collectionPath, settings.TargetPath));
        return File.Exists(targetPath) ? targetPath : null;
    }

    public static IEnumerable<string> EnumerateCollections(string parentPath, CollectionAssetKind kind) =>
        Directory.EnumerateDirectories(parentPath)
            .Select(path => {
                EnsureSettings(path);
                return path;
            })
            .Where(path => GetKind(path) == kind)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(Path.GetFileName, new NaturalStringComparer()!);

    public static IEnumerable<string> EnumerateAllCollections() {

        if (Directory.Exists(BuiltInRootPath))
            yield return BuiltInRootPath;

        var root = RootPath;
        if (!Directory.Exists(root)) yield break;

        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)) {
            EnsureSettings(dir);
            yield return dir;
        }
    }

    public static string GetLogicalCollectionPath(string collectionPath) {

        if (IsProjectRoot(collectionPath)) return "";
        if (IsBuiltInRoot(collectionPath)) return BuiltInCollectionLabel;

        var parent = Directory.GetParent(collectionPath)?.FullName;
        var prefix = "";

        if (!string.IsNullOrEmpty(parent) && IsUnderRoot(parent) && !IsProjectRoot(parent))
            prefix = GetLogicalCollectionPath(parent);

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(prefix)) parts.Add(prefix);

        var kind = GetKind(collectionPath);
        if (kind != CollectionAssetKind.Collection) parts.Add(GetKindName(kind));
        parts.Add(Path.GetFileName(collectionPath));

        return string.Join("/", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    public static bool TryGetCollectionSelectionValue(string collectionPath, string pickerType, out string value) {

        value = "";
        var targetPath = GetResolvedTargetPath(collectionPath);
        if (string.IsNullOrWhiteSpace(targetPath) || !IsPathCompatibleWithPicker(targetPath, pickerType)) return false;

        value = GetGuidForAssetPath(targetPath, pickerType);

        return !string.IsNullOrWhiteSpace(value);
    }

    public static bool ShouldHideAssetPath(string assetPath, string pickerType) {

        var fullAssetPath = Path.GetFullPath(assetPath);

        foreach (var collectionPath in EnumerateAllCollections()) {

            var targetPath = GetResolvedTargetPath(collectionPath);
            if (string.IsNullOrWhiteSpace(targetPath) || !IsPathCompatibleWithPicker(targetPath, pickerType)) continue;

            var fullCollectionPath = Path.GetFullPath(collectionPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                     + Path.DirectorySeparatorChar;

            if (fullAssetPath.StartsWith(fullCollectionPath, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    public static string GetPickerDisplayValue(string value, string pickerType) {

        if (string.IsNullOrWhiteSpace(value)) return "";

        if (TryGetSelectionCollectionInfo(value, pickerType, out var display, out _)) return display;

        if (pickerType == "LevelAsset") return AssetManager.Get<LevelAsset>(value) is { } levelAsset ? GetLevelDisplayName(levelAsset.File) : GetLevelDisplayName(value);
        return Path.GetFileNameWithoutExtension(value);
    }

    public static string GetPickerTooltip(string value, string pickerType) {

        if (string.IsNullOrWhiteSpace(value)) return "";

        if (TryGetSelectionCollectionInfo(value, pickerType, out _, out var tooltip)) return tooltip;

        return value;
    }

    public static bool TryGetSelectionCollectionInfo(string value, string pickerType, out string display, out string tooltip) {

        display = "";
        tooltip = "";

        foreach (var collectionPath in EnumerateAllCollections()) {
            if (!TryGetCollectionSelectionValue(collectionPath, pickerType, out var targetValue)) continue;
            if (!string.Equals(targetValue, value, StringComparison.OrdinalIgnoreCase)) continue;

            var targetPath = GetResolvedTargetPath(collectionPath);
            display = GetLogicalCollectionPath(collectionPath);
            tooltip = string.IsNullOrWhiteSpace(targetPath)
                ? display
                : $"{display} -> {AssetManager.GetStoredPath(targetPath)}";

            return true;
        }

        return false;
    }

    public static CollectionAssetKind ParseKind(string? type) => type switch {
        "Levels" => CollectionAssetKind.Level,
        "Materials" => CollectionAssetKind.Material,
        "Models" => CollectionAssetKind.Model,
        "Prefabs" => CollectionAssetKind.Prefab,
        "Scripts" => CollectionAssetKind.Script,
        "Textures" => CollectionAssetKind.Texture,
        _ => CollectionAssetKind.Collection
    };

    public static string GetKindName(CollectionAssetKind kind) => kind switch {
        CollectionAssetKind.Level => "Levels",
        CollectionAssetKind.Material => "Materials",
        CollectionAssetKind.Model => "Models",
        CollectionAssetKind.Prefab => "Prefabs",
        CollectionAssetKind.Script => "Scripts",
        CollectionAssetKind.Texture => "Textures",
        _ => "Collection"
    };

    public static CollectionAssetKind? GetKindForPickerType(string pickerType) => pickerType switch {
        "LevelAsset" => CollectionAssetKind.Level,
        "MaterialAsset" => CollectionAssetKind.Material,
        "ModelAsset" => CollectionAssetKind.Model,
        "PrefabAsset" => CollectionAssetKind.Prefab,
        "ScriptAsset" => CollectionAssetKind.Script,
        "TextureAsset" => CollectionAssetKind.Texture,
        _ => null
    };

    public static bool IsPathCompatibleWithPicker(string path, string pickerType) => pickerType switch {
        "LevelAsset" => IsLevel(path),
        "MaterialAsset" => IsMaterial(path),
        "ModelAsset" => IsModel(path),
        "PrefabAsset" => IsPrefab(path),
        "ScriptAsset" => IsScript(path),
        "TextureAsset" => IsTexture(path),
        _ => false
    };

    public static bool IsSidecarMetaFile(string path) {

        if (!AssetPaths.IsJson(path)) return false;

        var assetPath = path[..^5];
        return File.Exists(assetPath);
    }

    public static bool IsLevel(string path) => AssetPaths.IsLevel(path);
    public static bool IsMaterial(string path) => AssetPaths.IsMaterial(path);
    public static bool IsTexture(string path) => AssetFilePatterns.IsTexture(path);
    public static bool IsScript(string path) => AssetFilePatterns.IsScript(path);
    public static bool IsPrefab(string path) => AssetPaths.IsPrefab(path);
    public static bool IsShader(string path) => AssetFilePatterns.IsShader(path);
    public static bool IsFont(string path) => AssetFilePatterns.IsFont(path);

    public static bool IsModel(string path) => AssetFilePatterns.IsModel(path);

    public static string GetNameWithoutExtension(string path) {

        var name = Path.GetFileName(path);

        if (AssetPaths.IsLevel(path)) return name[..^4];
        if (AssetPaths.IsMaterial(path)) return name[..^4];
        if (IsPrefab(path)) return name[..^4];
        if (IsShader(path)) return name;

        return Path.GetFileNameWithoutExtension(name);
    }

    public static string GetRenameSuffix(string path) {

        var name = Path.GetFileName(path);

        if (AssetPaths.IsLevel(path)) return ".lvl";
        if (AssetPaths.IsMaterial(path)) return ".mat";
        if (IsPrefab(path)) return ".pre";

        return Path.GetExtension(path);
    }

    public static string GetLevelDisplayName(string value) {

        var file = Path.GetFileName(value.Replace('\\', '/'));
        if (AssetPaths.IsLevel(file)) return file[..^4];

        return Path.GetFileNameWithoutExtension(file);
    }

    private static string GetGuidForAssetPath(string path, string pickerType) =>
        AssetManager.GetGuidForPickerType(path, pickerType);
}
