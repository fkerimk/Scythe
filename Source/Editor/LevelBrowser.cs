using System.Numerics;
using ImGuiNET;
using static ImGuiNET.ImGui;

internal class LevelBrowser : Viewport {

    public LevelBrowser() : base("Level") {
        Obj.OnDelete += obj => {

            if (SelectedObject == obj || (SelectedObject != null && IsAncestorOf(obj, SelectedObject))) SelectObject(null);
        };
    }

    // cache
    private float? _savedScroll;

    internal static Obj? DragObject, DragTarget;
    internal static DropPlacement DragPlacement;
    internal static bool IsReorderingObject => DragObject != null;

    internal static Component? DragComponent;

    internal static bool IsDragCancelled;

    private static Obj? _scheduledDeleteObject;

    public static List<Obj> SelectedObjects { get; } = [];
    public static Obj?      SelectedObject  => SelectedObjects.Count > 0 ? SelectedObjects[0] : null;

    private int _rowCount;
    private readonly Dictionary<int, float> _expandProgress = [];
    private readonly Dictionary<int, float> _childHeights = [];

    // Rename
    private Obj?    _renamingObj;
    private string  _renameBuf = "";
    private bool    _reqRenameFocus;
    private Action? _scheduledRenameAction;

    protected override void OnDraw() {

        if (Core.ActiveLevel == null) return;

        if (IsMouseReleased(ImGuiMouseButton.Left)) IsDragCancelled = false;

        _rowCount = 0;

        BeginChild("scroll", new Vector2(0, 0));

        // restore scroll 
        if (_savedScroll != null) {

            SetScrollY(_savedScroll.Value);
            _savedScroll = null;
        }

        // drag object
        if (DragObject != null && DragTarget != null) {

            switch (DragPlacement) {
                case DropPlacement.Before:
                    DragObject.RecordedMoveBefore(DragTarget);
                    break;
                case DropPlacement.After:
                    DragObject.RecordedMoveAfter(DragTarget);
                    break;
                default:
                    DragObject.RecordedSetParent(DragTarget);
                    break;
            }

            DragObject = null;
            DragTarget = null;
            DragPlacement = DropPlacement.Into;
        }

        // Delete object
        if (_scheduledDeleteObject != null) {

            if (_scheduledDeleteObject != Core.ActiveLevel.Root) {

                if (SelectedObjects.Contains(_scheduledDeleteObject)) SelectObject(null);

                _scheduledDeleteObject.RecordedDelete();
            }

            _scheduledDeleteObject = null;
        }

        // F2 Rename
        if (IsFocused && IsKeyPressed(ImGuiKey.F2)) RenameSelected();

        // Execute scheduled rename
        if (_scheduledRenameAction != null) {

            _scheduledRenameAction.Invoke();
            _scheduledRenameAction = null;
        }

        // Draw objects
        DrawObject(Core.ActiveLevel.Root, 0, [], GetWindowDrawList());

        if (IsWindowHovered() && IsMouseReleased(ImGuiMouseButton.Left) && !IsAnyItemHovered())
            SelectObject(null);

        EndChild();
    }

    private static bool IsAncestorOf(Obj ancestor, Obj? target) => Obj.IsAncestorOf(ancestor, target?.Parent);

    private bool DrawObject(Obj obj, int indent, List<bool> branchHasMore, ImDrawListPtr mainDrawList) {

        if (Core.ActiveLevel == null) return true;

        var drawList = GetWindowDrawList();
        var objId = obj.GetHashCode();
        var rowId = $"##obj_row_{objId}";
        var hasChildren = obj.Children.Count > 0;
        var openId = GetID($"open##{objId}");
        var isOpen = GetStateStorage().GetInt(openId, 1) != 0;
        var isSelected = SelectedObjects.Contains(obj);
        if (SelectedObjects.Any(s => IsAncestorOf(obj, s))) {
            isOpen = true;
            GetStateStorage().SetInt(openId, 1);
        }

        var progress = UpdateExpandProgress(objId, hasChildren && isOpen);
        var visualProgress = progress;

        var rowHeight = GetFrameHeight();
        var rowWidth = MathF.Max(GetContentRegionAvail().X, 1f);
        var indentWidth = indent * 18f;
        var startPos = GetCursorScreenPos();

        SetCursorScreenPos(startPos);
        InvisibleButton(rowId, new Vector2(rowWidth, rowHeight));

        var rowMin = GetItemRectMin();
        var rowMax = GetItemRectMax();
        var rowHovered = IsItemHovered();
        var centerY = (rowMin.Y + rowMax.Y) * 0.5f;
        var arrowSize = 8f;
        var arrowCenterX = rowMin.X + indentWidth + 10f;
        var iconX = rowMin.X + indentWidth + 20f;
        var labelX = rowMin.X + indentWidth + 38f;
        var lineColor = GetColorU32(new Vector4(1f, 1f, 1f, 0.14f));

        if (_rowCount % 2 == 0)
            drawList.AddRectFilled(rowMin, rowMax, GetColorU32(new Vector4(1f, 1f, 1f, 0.02f)));

        if (isSelected)
            drawList.AddRectFilled(rowMin, rowMax, GetColorU32(Colors.GuiTreeSelected.ToVector4() with { W = 0.35f }), 4f);
        else if (IsItemHovered())
            drawList.AddRectFilled(rowMin, rowMax, GetColorU32(new Vector4(1f, 1f, 1f, 0.05f)), 4f);

        _rowCount++;

        for (var level = 0; level < branchHasMore.Count; level++) {
            if (!branchHasMore[level]) continue;

            var x = rowMin.X + level * 18f + 10f;
            drawList.AddLine(new Vector2(x, rowMin.Y), new Vector2(x, rowMax.Y), lineColor);
        }

        if (indent > 0) {

            var branchX = rowMin.X + indentWidth - 8f;
            drawList.AddLine(new Vector2(branchX, rowMin.Y), new Vector2(branchX, rowMax.Y), lineColor);
            drawList.AddLine(new Vector2(branchX, centerY), new Vector2(arrowCenterX - 6f, centerY), lineColor);
        }

        if (hasChildren) DrawExpandArrow(drawList, new Vector2(arrowCenterX, centerY), arrowSize, visualProgress, Colors.GuiTreeEnabled.ToVector4());

        // Selection Handling
        var multi = IsKeyDown(ImGuiKey.LeftCtrl) || IsKeyDown(ImGuiKey.RightCtrl);
        var arrowRectMin = new Vector2(arrowCenterX - 8f, rowMin.Y);
        var arrowRectMax = new Vector2(arrowCenterX + 8f, rowMax.Y);
        var mousePos = GetMousePos();
        var arrowHovered = hasChildren
            && mousePos.X >= arrowRectMin.X && mousePos.X <= arrowRectMax.X
            && mousePos.Y >= arrowRectMin.Y && mousePos.Y <= arrowRectMax.Y;

        // Right click - context
        if (rowHovered && IsMouseReleased(ImGuiMouseButton.Right))
            OpenPopupOnItemClick("context##" + objId);

        // Left click - select
        else if (rowHovered && IsMouseReleased(ImGuiMouseButton.Left)) {
            if (arrowHovered)
                GetStateStorage().SetInt(openId, isOpen ? 0 : 1);
            else
                SelectObject(obj, multi);
        }

        // drag + drop must stay bound to the row item itself
        if (!IsDragCancelled && BeginDragDropSource()) {

            DragObject = obj;

            SetDragDropPayload("object", IntPtr.Zero, 0);
            Text($"Moving {DragObject.Name}");
            EndDragDropSource();
        }

        if (BeginDragDropTarget()) {

            AcceptDragDropPayload("object");

            if (DragObject != null) {

                var placement = GetDropPlacementForRow(obj, rowMin, rowMax, mousePos);
                var prospectiveParent = placement switch {
                    DropPlacement.Before or DropPlacement.After => obj.Parent,
                    _ => obj
                };

                if (prospectiveParent != null && DragObject != obj && !Obj.IsAncestorOf(DragObject, prospectiveParent)) {

                    DrawDropPreview(mainDrawList, rowMin, rowMax, placement);

                    if (IsMouseReleased(ImGuiMouseButton.Left)) {

                        DragTarget = obj;
                        DragPlacement = placement;
                        _savedScroll = GetScrollY();
                    }
                }
            }

            EndDragDropTarget();
        }

        // Object context

        if (BeginPopup("context##" + objId)) {

            Text(obj.Name);

            Separator();

            if (BeginMenu("Insert")) {

                //var types = Assembly.GetExecutingAssembly().GetTypes().Where(t => t.IsSubclassOf(typeof(Component)) && !t.IsAbstract);

                if (MenuItem("Object")) Level.RecordedMakeObject("Object", obj);

                Separator();

                if (BeginMenu("Lighting")) {

                    if (MenuItem("Directional Light")) {

                        var light = Level.RecordedMakeObject("Directional Light", obj);
                        (light.MakeComponent("Light") as Light)?.Type = 0;
                        SelectObject(light);
                    }

                    if (MenuItem("Point Light")) {

                        var light = Level.RecordedMakeObject("Point Light", obj);
                        (light.MakeComponent("Light") as Light)?.Type = 1;
                        SelectObject(light);
                    }

                    if (MenuItem("Spot Light")) {

                        var light = Level.RecordedMakeObject("Point Light", obj);
                        (light.MakeComponent("Light") as Light)?.Type = 2;
                        SelectObject(light);
                    }

                    EndMenu();
                }

                if (BeginMenu("Models")) {

                    PathUtil.GetPath("Models", out var checkPath);

                    var modelPaths = Directory.GetFiles(checkPath, "*.*", SearchOption.AllDirectories);

                    foreach (var modelPath in modelPaths) {

                        if (Path.GetExtension(modelPath) != ".iqm") continue;

                        var pre  = checkPath + "\\";
                        var path = modelPath[pre.Length..^4].Replace('\\', '/');
                        var name = Path.GetFileName(path);

                        if (!MenuItem(path)) continue;

                        var model = Level.MakeObject(name, obj);
                        (model.MakeComponent("Model") as Model)!.GUID = AssetManager.GetGuid<ModelAsset>(path) ?? path;
                        SelectObject(model);
                    }

                    EndMenu();
                }
                
                EndMenu();
            }

            if (MenuItem("Rename")) StartRename(obj);
            if (MenuItem("Delete")) _scheduledDeleteObject = obj;

            EndPopup();
        }

        // object icon
        PushFont(Fonts.ImFontAwesomeSmall);
        var iconSize = CalcTextSize(Icons.FaDotCircleO);
        SetCursorScreenPos(new Vector2(iconX, centerY - iconSize.Y * 0.5f));
        TextColored(Colors.GuiTypeObject.ToVector4(), Icons.FaDotCircleO);
        PopFont();

        // Object name
        PushFont(Fonts.ImMontserratRegular);

        if (_renamingObj == obj) {

            var renameHeight = GetFrameHeight();
            SetCursorScreenPos(new Vector2(labelX, centerY - renameHeight * 0.5f));

            if (_reqRenameFocus) {

                SetKeyboardFocusHere();
                _reqRenameFocus = false;
            }

            if (InputText("##rename", ref _renameBuf, 128, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll)) {

                ConfirmRename();
            }

            if (IsItemActive() && IsKeyPressed(ImGuiKey.Escape)) CancelRename();
            if (IsItemDeactivated()) CancelRename();

        } else {

            var textSize = CalcTextSize(obj.Name);
            SetCursorScreenPos(new Vector2(labelX, centerY - textSize.Y * 0.5f));
            TextColored(new Vector4(1, 1, 1, 1), obj.Name);
        }

        PopFont();

        // Draw child nodes
        if (progress <= 0f) return true;

        var children = obj.Children.Values.ToList();
        var cachedChildHeight = _childHeights.GetValueOrDefault(objId, rowHeight);
        var animatedHeight = MathF.Max(1f, cachedChildHeight * visualProgress);

        PushStyleVar(ImGuiStyleVar.Alpha, GetStyle().Alpha * (0.2f + visualProgress * 0.8f));
        PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(GetStyle().ItemSpacing.X, 0f));

        if (!BeginChild($"children##{objId}", new Vector2(0, animatedHeight), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBackground)) {
            EndChild();
            PopStyleVar(3);
            return true;
        }

        var childStartY = GetCursorPosY();

        for (var i = 0; i < children.Count; i++) {

            var childBranches = new List<bool>(branchHasMore) { i < children.Count - 1 };

            if (!DrawObject(children[i], indent + 1, childBranches, mainDrawList))
                break;
        }

        var spacingY = GetStyle().ItemSpacing.Y;
        _childHeights[objId] = MathF.Max(rowHeight, MathF.Max(0f, GetCursorPosY() - childStartY - spacingY)) + 1f;
        EndChild();
        PopStyleVar(3);

        return true;
    }

    private float UpdateExpandProgress(int objId, bool isOpen) {

        var current = _expandProgress.GetValueOrDefault(objId, isOpen ? 1f : 0f);
        var target = isOpen ? 1f : 0f;
        var dt = GetIO().DeltaTime;
        var step = dt * 24f;
        var next = current < target
            ? MathF.Min(current + step, target)
            : MathF.Max(current - step, target);

        _expandProgress[objId] = next;
        return next;
    }

    private static void DrawExpandArrow(ImDrawListPtr drawList, Vector2 center, float size, float progress, Vector4 color) {

        var col = GetColorU32(color);
        var angle = progress * (MathF.PI * 0.5f);
        var p1 = RotatePoint(new Vector2(-size * 0.25f, -size * 0.6f), angle) + center;
        var p2 = RotatePoint(new Vector2(-size * 0.25f, size * 0.6f), angle) + center;
        var p3 = RotatePoint(new Vector2(size * 0.55f, 0f), angle) + center;

        drawList.AddTriangleFilled(p1, p2, p3, col);
    }

    private static Vector2 RotatePoint(Vector2 point, float angle) {

        var sin = MathF.Sin(angle);
        var cos = MathF.Cos(angle);
        return new Vector2(
            point.X * cos - point.Y * sin,
            point.X * sin + point.Y * cos
        );
    }

    private static DropPlacement GetDropPlacementForRow(Obj obj, Vector2 rowMin, Vector2 rowMax, Vector2 mousePos) {

        if (obj.Parent == null) return DropPlacement.Into;

        var height = rowMax.Y - rowMin.Y;
        var topThreshold = rowMin.Y + height * 0.28f;
        var bottomThreshold = rowMax.Y - height * 0.28f;

        if (mousePos.Y <= topThreshold) return DropPlacement.Before;
        if (mousePos.Y >= bottomThreshold) return DropPlacement.After;
        return DropPlacement.Into;
    }

    private static void DrawDropPreview(ImDrawListPtr drawList, Vector2 rowMin, Vector2 rowMax, DropPlacement placement) {

        switch (placement) {
            case DropPlacement.Before:
                drawList.AddLine(
                    new Vector2(rowMin.X + 8f, rowMin.Y + 1f),
                    new Vector2(rowMax.X - 8f, rowMin.Y + 1f),
                    GetColorU32(new Vector4(0.4f, 0.75f, 1f, 0.95f)),
                    2f
                );
                break;

            case DropPlacement.After:
                drawList.AddLine(
                    new Vector2(rowMin.X + 8f, rowMax.Y - 1f),
                    new Vector2(rowMax.X - 8f, rowMax.Y - 1f),
                    GetColorU32(new Vector4(0.4f, 0.75f, 1f, 0.95f)),
                    2f
                );
                break;

            default:
                drawList.AddRect(
                    rowMin + new Vector2(3f, 2f),
                    rowMax - new Vector2(3f, 2f),
                    GetColorU32(new Vector4(0.42f, 1f, 0.55f, 0.9f)),
                    4f,
                    ImDrawFlags.None,
                    1.5f
                );
                break;
        }
    }

    internal enum DropPlacement {
        Into,
        Before,
        After
    }

    public static void SelectObject(Obj? obj, bool multiSelect = false) {

        if (obj != null || !multiSelect) Editor.SetSelectedAsset(null);

        if (!multiSelect) {

            foreach (var s in SelectedObjects) s.IsSelected = false;
            SelectedObjects.Clear();
        }

        if (obj == null) return;

        if (SelectedObjects.Contains(obj)) {

            if (!multiSelect) return;

            obj.IsSelected = false;
            SelectedObjects.Remove(obj);

        } else {

            obj.IsSelected = true;
            SelectedObjects.Add(obj);
        }
    }

    public static void DeleteSelectedObject() {

        if (!CanDeleteSelectedObject) return;

        _scheduledDeleteObject = SelectedObject;
    }

    public static bool CanDeleteSelectedObject => SelectedObject != Core.ActiveLevel?.Root;

    // Rename
    public void RenameSelected() {

        if (SelectedObject != null && SelectedObject != Core.ActiveLevel?.Root) StartRename(SelectedObject);
    }

    private void StartRename(Obj obj) {

        _renamingObj    = obj;
        _renameBuf      = obj.Name;
        _reqRenameFocus = true;
    }

    private void ConfirmRename() {

        if (_renamingObj == null) return;

        if (string.IsNullOrWhiteSpace(_renameBuf) || _renameBuf == _renamingObj.Name) {

            CancelRename();

            return;
        }

        // Name duplicate check
        if (_renamingObj.Parent != null && _renamingObj.Parent.Children.ContainsKey(_renameBuf)) {

            CancelRename();

            return;
        }

        // Defer rename
        var targetObj = _renamingObj;
        var newName   = _renameBuf;

        _scheduledRenameAction = () => {

            History.StartRecording(targetObj, $"Rename {targetObj.Name}");
            targetObj.Name = newName;

            if (Core.ActiveLevel != null) Core.ActiveLevel.IsDirty = true;
            History.StopRecording();
        };

        _renamingObj = null;
    }

    private void CancelRename() => _renamingObj = null;
}
