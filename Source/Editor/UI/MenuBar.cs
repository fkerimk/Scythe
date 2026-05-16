using System.Reflection;
using static ImGuiNET.ImGui;

internal static class MenuBar {

    public static void Draw() {

        if (!BeginMainMenuBar()) return;

        if (BeginMenu("File")) {

            if (MenuItem("New", "Ctrl+N", false, false)) {
            }

            if (MenuItem("Open", "Ctrl+O", false, false)) {
            }

            Separator();
            if (MenuItem("Exit", "Ctrl+Q")) Editor.Quit();
            EndMenu();
        }

        if (BeginMenu("Edit")) {

            if (MenuItem("Delete", "Del", false, LevelBrowser.CanDeleteSelectedObject)) LevelBrowser.DeleteSelectedObject();
            Separator();
            if (MenuItem("Undo", "Ctrl+Z", false, History.CanUndo)) History.Undo();
            if (MenuItem("Redo", "Ctrl+Y", false, History.CanRedo)) History.Redo();
            Separator();

            if (MenuItem("Rename", "F2")) {
                if (Editor.LevelBrowser.IsFocused)
                    Editor.LevelBrowser.RenameSelected();
            }

            EndMenu();
        }

        if (BeginMenu("View")) {

            var viewports = typeof(Editor).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => typeof(Viewport).IsAssignableFrom(f.FieldType)).Select(f => (Viewport)f.GetValue(null)!);

            foreach (var v in viewports) MenuItem(v.Title, "", ref v.IsOpen);

            EndMenu();
        }

        if (BeginMenu("Help")) {

            if (MenuItem("Clear Build Cache"))
                ClearBuildCache();

            if (MenuItem("Clear Asset Cache"))
                ClearAssetCache();

            EndMenu();
        }

        BuildPipeline.DrawMenu();

        EndMainMenuBar();
    }

    private static void ClearBuildCache() {

        var assemblyPath = Path.Combine(ScytheConfig.Current.Project, "Assembly");

        try {
            if (Directory.Exists(assemblyPath))
                Directory.Delete(assemblyPath, recursive: true);

            Notifications.Show("Build cache cleared.");
        } catch (Exception e) {
            Notifications.Show($"Build cache clear failed: {e.Message}");
        }
    }

    private static void ClearAssetCache() {

        var importsPath = Path.Combine(ScytheConfig.Current.Project, "Imports");

        try {
            if (Directory.Exists(importsPath))
                Directory.Delete(importsPath, recursive: true);

            AssetManager.Init();
            Core.Load();
            Notifications.Show("Asset cache cleared.");
        } catch (Exception e) {
            Notifications.Show($"Asset cache clear failed: {e.Message}");
        }
    }
}
