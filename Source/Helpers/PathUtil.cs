internal static class PathUtil {

    public static string GetBaseRoot() => BundleRuntime.GetBaseRoot();
    public static string GetBuiltInCollectionRoot() {

        if (BundleRuntime.IsActive) return BundleRuntime.GetCollectionRoot();

        var workingDirCollection = Path.GetFullPath("Collection");
        if (Directory.Exists(workingDirCollection)) return workingDirCollection;

        return Path.Combine(AppContext.BaseDirectory, "Collection");
    }

    public static string GetResourcesRoot() {
        return GetBuiltInCollectionRoot();
    }

    public static void ValidateFile(string path, out string validPath, string content = "", bool project = false) {

        validPath = path;

        if (File.Exists(validPath)) return;

        validPath = Path.Join(ScytheConfig.Current.Project, path);

        if (File.Exists(validPath)) return;

        if (!project) {

            validPath = Path.Join(GetBaseRoot(), path);

            if (File.Exists(validPath)) return;
        }

        ValidateDir(Path.GetDirectoryName(validPath)!, out _, project);

        File.WriteAllText(validPath, content);
    }

    public static void ValidateDir(string path, out string validPath, bool project = false) {

        validPath = path;

        if (Directory.Exists(validPath)) return;

        validPath = Path.Join(ScytheConfig.Current.Project, path);

        if (Directory.Exists(validPath)) return;

        if (!project) {

            validPath = Path.Join(GetBaseRoot(), path);

            if (Directory.Exists(validPath)) return;
        }

        Directory.CreateDirectory(validPath);
    }

    public static bool GetPath(string relativePath, out string fullPath) {

        fullPath = Path.GetFullPath(relativePath);

        if (File.Exists(fullPath) || Directory.Exists(fullPath)) return true;

        fullPath = Path.Join(ScytheConfig.Current.Project, relativePath);

        if (File.Exists(fullPath) || Directory.Exists(fullPath)) return true;

        fullPath = Path.Join(GetBaseRoot(), relativePath);

        if (File.Exists(fullPath) || Directory.Exists(fullPath)) return true;

        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (normalized.Equals("Collection", StringComparison.OrdinalIgnoreCase) || normalized.Equals("Resources", StringComparison.OrdinalIgnoreCase)) {
            fullPath = GetBuiltInCollectionRoot();
        } else if (normalized.StartsWith("Collection/", StringComparison.OrdinalIgnoreCase)) {
            fullPath = Path.Join(GetBaseRoot(), normalized);
        } else if (normalized.StartsWith("Resources/", StringComparison.OrdinalIgnoreCase)) {
            fullPath = Path.Join(GetBaseRoot(), "Collection", Path.GetFileName(normalized));
        } else {
            fullPath = Path.Join(GetBuiltInCollectionRoot(), relativePath);
        }

        if (File.Exists(fullPath) || Directory.Exists(fullPath)) return true;

        return false;
    }
}
