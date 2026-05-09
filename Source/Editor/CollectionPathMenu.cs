using static ImGuiNET.ImGui;

internal static class CollectionPathMenu {

    public static bool DrawProjectDirectoryMenu(string label, Func<string, bool> onSelect, string? excludePath = null) {

        if (!BeginMenu(label)) return false;

        var selected = DrawDirectoryMenuRecursive(CollectionData.RootPath, onSelect, excludePath, allowRootSelection: true);

        EndMenu();
        return selected;
    }

    public static bool DrawProjectPrefabMenu(string label, Func<string, bool> onSelect) {

        if (!BeginMenu(label)) return false;

        var selected = DrawPrefabMenuRecursive(CollectionData.RootPath, onSelect);

        EndMenu();
        return selected;
    }

    private static bool DrawDirectoryMenuRecursive(string directory, Func<string, bool> onSelect, string? excludePath, bool allowRootSelection = false) {

        var currentFull = Path.GetFullPath(directory);
        var excludedFull = string.IsNullOrWhiteSpace(excludePath) ? "" : Path.GetFullPath(excludePath);
        var selected = false;

        if (allowRootSelection || !currentFull.Equals(Path.GetFullPath(CollectionData.RootPath), StringComparison.OrdinalIgnoreCase)) {
            if (!currentFull.Equals(excludedFull, StringComparison.OrdinalIgnoreCase) && MenuItem("Here")) {
                selected = onSelect(directory);
                if (selected) return true;
            }

            if (Directory.EnumerateDirectories(directory).Any())
                Separator();
        }

        foreach (var child in Directory.EnumerateDirectories(directory).OrderBy(Path.GetFileName, new NaturalStringComparer()!)) {

            var name = Path.GetFileName(child);
            if (!BeginMenu(name)) continue;

            if (DrawDirectoryMenuRecursive(child, onSelect, excludePath)) {
                EndMenu();
                return true;
            }

            EndMenu();
        }

        return selected;
    }

    private static bool DrawPrefabMenuRecursive(string directory, Func<string, bool> onSelect) {

        var prefabs = Directory.EnumerateFiles(directory)
            .Where(CollectionData.IsPrefab)
            .OrderBy(CollectionData.GetNameWithoutExtension, new NaturalStringComparer()!)
            .ToList();

        foreach (var prefab in prefabs)
            if (MenuItem(CollectionData.GetNameWithoutExtension(prefab)) && onSelect(prefab))
                return true;

        var childDirectories = Directory.EnumerateDirectories(directory)
            .OrderBy(Path.GetFileName, new NaturalStringComparer()!)
            .ToList();

        if (prefabs.Count > 0 && childDirectories.Count > 0) Separator();

        foreach (var child in childDirectories) {

            var name = Path.GetFileName(child);
            if (!BeginMenu(name)) continue;

            if (DrawPrefabMenuRecursive(child, onSelect)) {
                EndMenu();
                return true;
            }

            EndMenu();
        }

        return false;
    }
}
