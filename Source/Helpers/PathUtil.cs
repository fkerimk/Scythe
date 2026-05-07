internal static class PathUtil {

    public static string GetBaseRoot() => BundleRuntime.GetBaseRoot();
    public static string GetResourcesRoot() {

        if (BundleRuntime.IsActive) return BundleRuntime.GetResourcesRoot();

        var workingDirResources = Path.GetFullPath("Resources");
        if (Directory.Exists(workingDirResources)) return workingDirResources;

        return Path.Combine(AppContext.BaseDirectory, "Resources");
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

        var normalized = relativePath.Replace('\\', '/');
        if (normalized.Equals("Resources", StringComparison.OrdinalIgnoreCase))
            fullPath = GetResourcesRoot();
        else if (normalized.StartsWith("Resources/", StringComparison.OrdinalIgnoreCase))
            fullPath = Path.Join(GetBaseRoot(), relativePath);
        else
            fullPath = Path.Join(GetResourcesRoot(), relativePath);

        if (File.Exists(fullPath) || Directory.Exists(fullPath)) return true;

        return false;
    }
}
