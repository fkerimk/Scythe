#if SCYTHE_RUNTIME_BUILD
using System.Numerics;
using Raylib_cs;

internal static class Editor {
    public static readonly EditorRender EditorRender = new();
    public static void OpenLevel(string path) => Core.OpenLevel(CollectionData.GetLevelDisplayName(path), path);
    public static void CreateLevel(string path) { }
    public static void SetSelectedAsset(string? path) { }
    public static void SelectProjectSettings() { }
    public static void OnDocumentPathMoved(string oldPath, string newPath) { }
}

internal static class CollectionData {
    public static bool IsLevel(string path) => AssetPaths.IsLevel(path);
    public static string GetLevelDisplayName(string value) {
        return AssetPaths.GetDisplayName(value, ".lvl");
    }
}

internal class EditorRender {
    public bool IsHovered { get; set; }
    public Vector2 RelativeMouse { get; set; }
    public static Vector2 RelativeMouse3D { get; set; }
}

internal class LevelBrowser {
    public static List<Obj> SelectedObjects { get; } = [];
    public static Obj? SelectedObject => SelectedObjects.Count > 0 ? SelectedObjects[0] : null;
}

internal static class History {
    public static bool CanUndo => false;
    public static bool CanRedo => false;
    public static void Clear() { }
    public static void Undo() { }
    public static void Redo() { }
    public static void StartRecording(object target, string name = "") { }
    public static void StopRecording() { }
    public static void SetUndoAction(Action action) { }
    public static void SetRedoAction(Action action) { }
    public static void Execute(string name, Action redo, Action undo) => redo();
}

internal static class Preview {
    public static void UpdateThumbnail(Asset asset) { }
}

internal static class FreeCam {
    public static Vector3 Pos { get; set; }
    public static Vector2 Rot { get; set; }
    public static void SetFromTarget(Camera3D? camera) { }
}

internal static class Notifications {
    public static void Show(string text, float duration = 2.5f) { }
    public static void ShowTask(BackgroundTask task) { }
    public static void Draw() { }
}

internal static class Icons {
    public const string FaPlay = "\uf04b";
    public const string FaPause = "\uf04c";
    public const string FaStop = "\uf04d";
    public const string FaClock = "\uf017";
    public const string FaFilm = "\uf008";
    public const string FaCheck = "\uf00c";
    public const string FaXMark = "\uf00d";
    public const string FaFile = "\uf15b";
    public const string FaFolder = "\uf07b";
    public const string FaArchive = "\uf187";
    public const string FaLevelUp = "\uf148";
    public const string FaDotCircleO = "\uf192";
    public const string FaCube = "\uf1b2";
    public const string FaArrowsAlt = "\uf0b2";
    public const string FaPlayCircle = "\uf144";
    public const string FaArrows = "\uf047";
    public const string FaLightbulbO = "\uf0eb";
    public const string FaVideoCamera = "\uf03d";
    public const string FaCrosshairs = "\uf05b";
    public const string FaCode = "\uf121";
    public const string FaFileCode = "\uf1c9";
    public const string FaSearch = "\uf002";
    public const string FaEye = "\uf06e";
    public const string FaEyeSlash = "\uf070";
    public const string FaPlus = "\uf067";
    public const string FaMap = "\uf279";
    public const string FaFlag = "\uf024";
    public const string FaHouse = "\uf015";
    public const string FaFileImage = "\uf1c5";
    public const string FaTrashAlt = "\uf2ed";
    public const string FaWandMagicSparkles = "\ue2ca";
}
#endif
