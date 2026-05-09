using Raylib_cs;
using static Raylib_cs.Raylib;

internal static class Shortcuts {

    public static void Check() {

        // Always allow F5 to toggle play mode
        if (IsKeyPressed(KeyboardKey.F5)) {

            RunPlayMode();

            return;
        }

        // Ignore other shortcuts if playing or if text inputs are active
        if (Core.IsPlaying) return;

        if (IsKeyDown(KeyboardKey.LeftControl)) {

            if (IsKeyPressed(KeyboardKey.Q)) Editor.Quit();
            if (IsKeyPressed(KeyboardKey.D)) DuplicateSelectedObject();
            if (IsKeyPressed(KeyboardKey.S)) SaveActiveLevel();
            if (IsKeyPressed(KeyboardKey.Z)) History.Undo();
            if (IsKeyPressed(KeyboardKey.Y)) History.Redo();
        }

        if (IsKeyPressed(KeyboardKey.F2) && Editor.SelectedAssetPath != null)
            Editor.Collections.StartRenameSelected();

        if (IsKeyPressed(KeyboardKey.Delete)) {

            if (Editor.SelectedAssetPath != null)
                Editor.Collections.DeleteSelectedAsset();
            else
                LevelBrowser.DeleteSelectedObject();
        }
    }

    private static void DuplicateSelectedObject() {

        if (LevelBrowser.SelectedObject == null) return;
        if (Core.ActiveLevel?.IsPrefabDocument == true && ReferenceEquals(LevelBrowser.SelectedObject.Parent, Core.ActiveLevel.Root)) return;

        LevelBrowser.SelectObject(LevelBrowser.SelectedObject.CloneRecorded());
    }

    private static void SaveActiveLevel() {

        Core.ActiveLevel?.Save();
        Notifications.Show("Saved");
    }

    private static void RunPlayMode() {

        var center = Editor.RuntimeRender.ScreenPos + Editor.RuntimeRender.TexSize / 2f;
        Editor.TogglePlayMode(center);
    }
}
