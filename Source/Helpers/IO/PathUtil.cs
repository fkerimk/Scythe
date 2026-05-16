internal static class PathUtil {

    public static string GetBaseRoot() {

        if (BundleRuntime.IsActive) return BundleRuntime.GetBaseRoot();

        return FindEngineRoot() ?? AppContext.BaseDirectory;
    }

    private static string? FindEngineRoot() {

        var candidates = new[] {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory,
            Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? ""
        };

        foreach (var start in candidates) {

            var root = FindEngineRootFrom(start);
            if (!string.IsNullOrWhiteSpace(root)) return root;
        }

        return null;
    }

    private static string? FindEngineRootFrom(string start) {

        if (string.IsNullOrWhiteSpace(start)) return null;

        var current = new DirectoryInfo(Path.GetFullPath(start));

        while (current != null) {

            var collectionPath = Path.Combine(current.FullName, "Collection");
            var projectFilePath = Path.Combine(current.FullName, "Scythe.csproj");

            if (Directory.Exists(collectionPath) && (File.Exists(projectFilePath) || Directory.Exists(Path.Combine(current.FullName, "Source"))))
                return current.FullName;

            current = current.Parent;
        }

        return null;
    }

    public static string GetBuiltInCollectionRoot() {

        if (BundleRuntime.IsActive) return BundleRuntime.GetCollectionRoot();

        var workingDirCollection = Path.GetFullPath("Collection");
        if (Directory.Exists(workingDirCollection)) return workingDirCollection;

        var baseRootCollection = Path.Combine(GetBaseRoot(), "Collection");
        if (Directory.Exists(baseRootCollection)) return baseRootCollection;

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

        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        var builtInPrefix = "Built In/";
        var projectCollectionsRoot = Path.Join(ScytheConfig.Current.Project, "Collections");

        if (normalized.StartsWith(builtInPrefix, StringComparison.OrdinalIgnoreCase))
            fullPath = Path.Join(GetBuiltInCollectionRoot(), normalized[builtInPrefix.Length..]);
        else if (!Path.IsPathRooted(relativePath) && !normalized.StartsWith("Collection/", StringComparison.OrdinalIgnoreCase) && !normalized.StartsWith("Resources/", StringComparison.OrdinalIgnoreCase))
            fullPath = Path.Join(projectCollectionsRoot, relativePath);
        else
            fullPath = Path.Join(ScytheConfig.Current.Project, relativePath);

        if (File.Exists(fullPath) || Directory.Exists(fullPath)) return true;

        fullPath = Path.Join(GetBaseRoot(), relativePath);

        if (File.Exists(fullPath) || Directory.Exists(fullPath)) return true;

        if (normalized.Equals("Collection", StringComparison.OrdinalIgnoreCase) || normalized.Equals("Resources", StringComparison.OrdinalIgnoreCase)) {
            fullPath = GetBuiltInCollectionRoot();
        } else if (normalized.Equals("Built In", StringComparison.OrdinalIgnoreCase)) {
            fullPath = GetBuiltInCollectionRoot();
        } else if (normalized.StartsWith(builtInPrefix, StringComparison.OrdinalIgnoreCase)) {
            fullPath = Path.Join(GetBuiltInCollectionRoot(), normalized[builtInPrefix.Length..]);
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
