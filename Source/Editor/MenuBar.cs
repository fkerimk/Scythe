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
                else if (Editor.ProjectBrowser.IsFocused) Editor.ProjectBrowser.RenameSelected();
            }

            EndMenu();
        }

        if (BeginMenu("View")) {

            var viewports = typeof(Editor).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => typeof(Viewport).IsAssignableFrom(f.FieldType)).Select(f => (Viewport)f.GetValue(null)!);

            foreach (var v in viewports) MenuItem(v.Title, "", ref v.IsOpen);

            EndMenu();
        }

        BuildPipeline.DrawMenu();

        EndMainMenuBar();
    }
}
