using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using Newtonsoft.Json;
using static ImGuiNET.ImGui;
using static Raylib_cs.Raylib;

internal class EditorRender() : Viewport("Render (Editor)") {

    public RenderTexture2D Rt = new(), OutlineRt = new();
    public Vector2 TexSize = Vector2.One, TexTemp = Vector2.Zero;
    public static Vector2 RelativeMouse3D;
    private int _tabDropIndicatorIndex = -1;
    private readonly List<(float MinX, float MaxX)> _tabBounds = [];

    private static string GetPath() => Path.Join(ScytheConfig.Current.Project, "Project", "EditorRender.json");

    public void Load() {

        var path = GetPath();

        if (File.Exists(path)) {

            SafeExec.Try(() => {

                    var settings = JsonFile.ReadOrDefault<EditorRenderSettings?>(path, null);

                    if (settings == null) return;

                    foreach (var relPath in settings.OpenLevels) {

                        var absPath = Path.Join(ScytheConfig.Current.Project, relPath);
                        if (File.Exists(absPath)) Editor.OpenLevel(absPath);
                    }

                    var activeLevelIndex = ResolveActiveLevelIndex(settings);
                    if (activeLevelIndex >= 0 && activeLevelIndex < Core.OpenLevels.Count)
                        Core.SetActiveLevel(activeLevelIndex);
                }
            );
        }

        EnsureAtLeastOneLevelOpen();
    }

    private void EnsureAtLeastOneLevelOpen() {

        if (Core.OpenLevels.Count > 0) return;

        var startupGuid = ProjectConfig.Current.StartupLevel?.Replace('\\', '/');
        var startupStoredPath = ProjectConfig.Current.StartupLevelPath?.Replace('\\', '/');

        if (!string.IsNullOrWhiteSpace(startupGuid) || !string.IsNullOrWhiteSpace(startupStoredPath)) {

            var lookupGuid = startupGuid ?? "";
            var lookupPath = startupStoredPath ?? "";
            var levelAsset = AssetManager.ResolveReference<LevelAsset>(ref lookupGuid, ref lookupPath);

            ProjectConfig.Current.StartupLevel = lookupGuid;
            ProjectConfig.Current.StartupLevelPath = lookupPath;

            if (levelAsset is { IsLoaded: true } && File.Exists(levelAsset.File)) {
                Editor.OpenLevel(levelAsset.File);
                return;
            }
        }

        var firstLevel = FindFirstLevelPath();
        if (!string.IsNullOrWhiteSpace(firstLevel)) Editor.OpenLevel(firstLevel);
    }

    public void Save() {

        var path = GetPath();

        var settings = new EditorRenderSettings {
            OpenLevels = Core.OpenLevels.Select(l => Path.GetRelativePath(ScytheConfig.Current.Project, l.JsonPath).Replace('\\', '/')).ToList(),
            ActiveLevelIndex = Core.ActiveLevelIndex,
            ActiveLevelPath = Core.ActiveLevel == null
                ? null
                : Path.GetRelativePath(ScytheConfig.Current.Project, Core.ActiveLevel.JsonPath).Replace('\\', '/')
        };

        JsonFile.WriteIndented(path, settings);
    }

    private class EditorRenderSettings {

        public List<string> OpenLevels { get; init; } = [];
        public int ActiveLevelIndex { get; init; } = -1;
        public string? ActiveLevelPath { get; init; }
    }

    private static int ResolveActiveLevelIndex(EditorRenderSettings settings) {

        if (!string.IsNullOrWhiteSpace(settings.ActiveLevelPath)) {
            var normalizedActivePath = Path.GetFullPath(Path.Join(ScytheConfig.Current.Project, settings.ActiveLevelPath));
            var pathIndex = Core.OpenLevels.FindIndex(level =>
                string.Equals(Path.GetFullPath(level.JsonPath), normalizedActivePath, StringComparison.OrdinalIgnoreCase));
            if (pathIndex >= 0) return pathIndex;
        }

        return settings.ActiveLevelIndex;
    }

    private static string? FindFirstLevelPath() {

        if (!Directory.Exists(ScytheConfig.Current.Project)) return null;

        return Directory.EnumerateFiles(ScytheConfig.Current.Project, "*", SearchOption.AllDirectories)
            .Where(CollectionData.IsLevel)
            .OrderBy(path => path, new NaturalStringComparer()!)
            .FirstOrDefault();
    }

    protected override void OnDraw() {

        EnsureAtLeastOneLevelOpen();

        if (Core.OpenLevels.Count == 0) return;

        DrawLevelTabs();
        DrawActiveLevelContent();

        // Draw FPS in top-right corner of the viewport using ImGui DrawList (so it stays on top)
        if (Rt.Texture is { Width: > 0, Height: > 0 }) {

            const float fontSize = 26f;

            var fpsText = GetFPS().ToString();
            var textSize = CalcTextSize(fpsText) * (fontSize / GetFontSize());
            var padding = new Vector2(10, 5);

            // Draw on top of everything in this window
            var drawList = GetWindowDrawList();
            var pos = WindowPos + new Vector2(ContentRegion.X - textSize.X - padding.X, GetFrameHeight() + padding.Y);

            drawList.AddText(GetFont(), fontSize, pos + new Vector2(1, 1), ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 1)), fpsText); // Shadow
            drawList.AddText(GetFont(), fontSize, pos, ColorConvertFloat4ToU32(Colors.Primary.ToVector4()), fpsText);
        }

        Core.ShouldFocusActiveLevel = false;
    }

    private void DrawLevelTabs() {
        PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6f, 6f));
        PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0f, 0f));

        var closeIndex = -1;
        var drawList = GetWindowDrawList();
        var availableWidth = GetContentRegionAvail().X;
        var tabHeight = GetFrameHeight() + 6f;
        var stripHeight = tabHeight + 12f;
        var closeSize = 18f;
        var horizontalPadding = 12f;
        var startPaddingX = 8f;
        _tabDropIndicatorIndex = -1;
        _tabBounds.Clear();

        var stripMin = GetCursorScreenPos();
        Dummy(new Vector2(MathF.Max(availableWidth, 1f), stripHeight));
        var cursorX = stripMin.X + startPaddingX;
        var tabY = stripMin.Y + (stripHeight - tabHeight) * 0.5f;

        for (var i = 0; i < Core.OpenLevels.Count; i++) {
            var level = Core.OpenLevels[i];
            var isSelected = Core.ActiveLevelIndex == i;
            var label = $"{level.Name}{(level.IsDirty ? " *" : "")}";
            var textSize = CalcTextSize(label);
            var tabWidth = MathF.Min(availableWidth, textSize.X + horizontalPadding * 2f + closeSize + 8f);

            PushID(level.GUID);
            SetCursorScreenPos(new Vector2(cursorX, tabY));

            InvisibleButton("##level_tab", new Vector2(tabWidth, tabHeight));
            var tabMin = GetItemRectMin();
            var tabMax = GetItemRectMax();
            _tabBounds.Add((tabMin.X, tabMax.X));
            var hovered = IsItemHovered();
            var closeMin = new Vector2(tabMax.X - closeSize - 6f, tabMin.Y + (tabHeight - closeSize) * 0.5f);
            var closeMax = closeMin + new Vector2(closeSize, closeSize);
            var closeHovered = hovered && IsMouseHoveringRect(closeMin, closeMax);

            var bg = isSelected
                ? Colors.GuiTabSelected.ToVector4() with { W = 0.95f }
                : hovered
                    ? Colors.GuiTabHovered.ToVector4() with { W = 0.95f }
                    : Colors.GuiTab.ToVector4() with { W = 0.95f };
            drawList.AddRectFilled(tabMin, tabMax, ColorConvertFloat4ToU32(bg), 8f);

            if (isSelected)
                drawList.AddRect(tabMin, tabMax, ColorConvertFloat4ToU32(Colors.Primary.ToVector4() with { W = 0.85f }), 8f, ImDrawFlags.None, 1.5f);
            else
                drawList.AddRect(tabMin, tabMax, ColorConvertFloat4ToU32(Colors.GuiBorder.ToVector4() with { W = 0.8f }), 8f, ImDrawFlags.None, 1f);

            var textPos = new Vector2(tabMin.X + horizontalPadding, tabMin.Y + (tabHeight - textSize.Y) * 0.5f);
            drawList.AddText(textPos, ColorConvertFloat4ToU32(Colors.GuiText.ToVector4()), label);

            var closeColor = closeHovered ? Colors.Primary.ToVector4() : Colors.GuiTextDisabled.ToVector4();
            var inset = 5f;
            drawList.AddLine(closeMin + new Vector2(inset, inset), closeMax - new Vector2(inset, inset), ColorConvertFloat4ToU32(closeColor), 1.6f);
            drawList.AddLine(new Vector2(closeMin.X + inset, closeMax.Y - inset), new Vector2(closeMax.X - inset, closeMin.Y + inset), ColorConvertFloat4ToU32(closeColor), 1.6f);

            if (IsItemClicked(ImGuiMouseButton.Left)) {
                if (closeHovered)
                    closeIndex = i;
                else if (!isSelected)
                    Core.SetActiveLevel(i);
            }

            if (!closeHovered && BeginDragDropSource()) {
                var payloadIndex = i;
                unsafe { SetDragDropPayload("LEVEL_TAB_INDEX", new IntPtr(&payloadIndex), (uint)sizeof(int)); }
                Text(label);
                EndDragDropSource();
            }

            if (BeginDragDropTarget()) {
                unsafe {
                    var payload = AcceptDragDropPayload("LEVEL_TAB_INDEX", ImGuiDragDropFlags.AcceptBeforeDelivery);
                    if (payload.NativePtr != null && payload.Data != IntPtr.Zero) {
                        var sourceIndex = *(int*)payload.Data;
                        var mouseX = GetMousePos().X;
                        var insertIndex = mouseX < (tabMin.X + tabMax.X) * 0.5f ? i : i + 1;
                        if (!IsSameLevelSlot(sourceIndex, insertIndex)) {
                            _tabDropIndicatorIndex = insertIndex;
                            if (payload.IsDelivery())
                                Core.MoveLevel(sourceIndex, insertIndex);
                        }
                    }
                }
                EndDragDropTarget();
            }

            PopID();
            cursorX = tabMax.X + 6f;
        }

        SetCursorScreenPos(new Vector2(cursorX, tabY));
        InvisibleButton("##level_tab_end_drop", new Vector2(24f, tabHeight));
        if (BeginDragDropTarget()) {
            unsafe {
                var payload = AcceptDragDropPayload("LEVEL_TAB_INDEX", ImGuiDragDropFlags.AcceptBeforeDelivery);
                if (payload.NativePtr != null && payload.Data != IntPtr.Zero) {
                    var sourceIndex = *(int*)payload.Data;
                    var insertIndex = Core.OpenLevels.Count;
                    if (!IsSameLevelSlot(sourceIndex, insertIndex)) {
                        _tabDropIndicatorIndex = insertIndex;
                        if (payload.IsDelivery())
                            Core.MoveLevel(sourceIndex, insertIndex);
                    }
                }
            }
            EndDragDropTarget();
        }

        PopStyleVar();
        PopStyleVar();

        if (_tabDropIndicatorIndex >= 0 && Core.OpenLevels.Count > 0) {
            var indicatorX = GetTabDropIndicatorX(_tabDropIndicatorIndex);
            var top = stripMin.Y + (stripHeight - tabHeight) * 0.5f;
            drawList.AddLine(
                new Vector2(indicatorX, top + 4f),
                new Vector2(indicatorX, top + tabHeight - 4f),
                ColorConvertFloat4ToU32(Colors.Primary.ToVector4()),
                3f
            );
        }

        Separator();

        if (closeIndex >= 0)
            Core.CloseLevel(closeIndex);
    }

    private float GetTabDropIndicatorX(int insertIndex) {

        if (_tabBounds.Count == 0) return 0f;
        insertIndex = Math.Clamp(insertIndex, 0, _tabBounds.Count);

        if (insertIndex == 0)
            return _tabBounds[0].MinX - 3f;

        if (insertIndex >= _tabBounds.Count)
            return _tabBounds[^1].MaxX + 3f;

        return _tabBounds[insertIndex].MinX - 3f;
    }

    private static bool IsSameLevelSlot(int sourceIndex, int insertIndex) =>
        sourceIndex == insertIndex || sourceIndex + 1 == insertIndex;

    private void DrawActiveLevelContent() {

        if (Rt.Texture is not { Width: > 0, Height: > 0 }) return;

        var tex = (IntPtr)Rt.Texture.Id;
        var contentPos = GetCursorScreenPos();
        var avail = GetContentRegionAvail();

        Image(tex, avail, new Vector2(0, 1), new Vector2(1, 0));

        var mouse = GetMousePosition();
        var relX = Raymath.Clamp((mouse.X - contentPos.X) / avail.X, 0, 1);
        var relY = Raymath.Clamp((mouse.Y - contentPos.Y) / avail.Y, 0, 1);

        relX = (relX - 0.5f) * (avail.X / GetScreenWidth()) * (GetScreenHeight() / avail.Y) + 0.5f;
        RelativeMouse3D = new Vector2(relX * GetScreenWidth(), relY * GetScreenHeight());

        TexSize = avail;
    }
}
