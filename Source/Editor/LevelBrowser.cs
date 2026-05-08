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

    internal static Component? DragComponent;

    internal static bool IsDragCancelled;

    private static Obj? _scheduledDeleteObject;

    public static List<Obj> SelectedObjects { get; } = [];
    public static Obj?      SelectedObject  => SelectedObjects.Count > 0 ? SelectedObjects[0] : null;

    private int _rowCount;

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

            DragObject.RecordedSetParent(DragTarget);

            DragObject = null;
            DragTarget = null;
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
        DrawObject(Core.ActiveLevel.Root, 0, []);

        if (IsWindowHovered() && IsMouseReleased(ImGuiMouseButton.Left) && !IsAnyItemHovered())
            SelectObject(null);

        EndChild();
    }

    private static bool IsAncestorOf(Obj ancestor, Obj? target) {

        if (target == null) return false;

        var current = target.Parent;

        while (current != null) {

            if (current == ancestor) return true;

            current = current.Parent;
        }

        return false;
    }

    private bool DrawObject(Obj obj, int indent, List<bool> branchHasMore) {

        if (Core.ActiveLevel == null) return true;

        var drawList = GetWindowDrawList();
        var rowId = $"##obj_row_{obj.GetHashCode()}";
        var hasChildren = obj.Children.Count > 0;
        var openId = GetID($"open##{obj.GetHashCode()}");
        var isOpen = GetStateStorage().GetInt(openId, 1) != 0;
        var isSelected = SelectedObjects.Contains(obj);
        if (SelectedObjects.Any(s => IsAncestorOf(obj, s))) {
            isOpen = true;
            GetStateStorage().SetInt(openId, 1);
        }

        var rowHeight = GetFrameHeight();
        var rowWidth = MathF.Max(GetContentRegionAvail().X, 1f);
        var indentWidth = indent * 18f;
        var startPos = GetCursorScreenPos();

        SetCursorScreenPos(startPos);
        InvisibleButton(rowId, new Vector2(rowWidth, rowHeight));

        var rowMin = GetItemRectMin();
        var rowMax = GetItemRectMax();
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

        if (hasChildren) DrawExpandArrow(drawList, new Vector2(arrowCenterX, centerY), arrowSize, isOpen, Colors.GuiTreeEnabled.ToVector4());

        // Selection Handling
        var multi = IsKeyDown(ImGuiKey.LeftCtrl) || IsKeyDown(ImGuiKey.RightCtrl);
        var arrowRectMin = new Vector2(arrowCenterX - 8f, rowMin.Y);
        var arrowRectMax = new Vector2(arrowCenterX + 8f, rowMax.Y);
        var mousePos = GetMousePos();
        var arrowHovered = hasChildren
            && mousePos.X >= arrowRectMin.X && mousePos.X <= arrowRectMax.X
            && mousePos.Y >= arrowRectMin.Y && mousePos.Y <= arrowRectMax.Y;

        // Right click - context
        if (IsItemHovered() && IsMouseReleased(ImGuiMouseButton.Right))
            OpenPopupOnItemClick("context##" + obj.GetHashCode());

        // Left click - select
        else if (IsItemHovered() && IsMouseReleased(ImGuiMouseButton.Left)) {
            if (arrowHovered)
                GetStateStorage().SetInt(openId, isOpen ? 0 : 1);
            else
                SelectObject(obj, multi);
        }

        // Object context

        if (BeginPopup("context##" + obj.GetHashCode())) {

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

        // start drag
        if (!IsDragCancelled && BeginDragDropSource()) {

            DragObject = obj;

            SetDragDropPayload("object", IntPtr.Zero, 0);
            Text($"Moving {DragObject.Name}");
            EndDragDropSource();
        }

        // cache drop
        if (BeginDragDropTarget()) {

            AcceptDragDropPayload("object");

            if (DragObject != null && IsMouseReleased(ImGuiMouseButton.Left)) {

                DragTarget   = obj;
                _savedScroll = GetScrollY();
            }

            EndDragDropTarget();
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
        if (!isOpen) return true;

        var children = obj.Children.Values.ToList();

        for (var i = 0; i < children.Count; i++) {

            var childBranches = new List<bool>(branchHasMore) { i < children.Count - 1 };

            if (!DrawObject(children[i], indent + 1, childBranches))
                return false;
        }

        return true;
    }

    private static void DrawExpandArrow(ImDrawListPtr drawList, Vector2 center, float size, bool isOpen, Vector4 color) {

        var col = GetColorU32(color);

        if (isOpen) {
            drawList.AddTriangleFilled(
                new Vector2(center.X - size * 0.6f, center.Y - size * 0.25f),
                new Vector2(center.X + size * 0.6f, center.Y - size * 0.25f),
                new Vector2(center.X, center.Y + size * 0.55f),
                col
            );

            return;
        }

        drawList.AddTriangleFilled(
            new Vector2(center.X - size * 0.25f, center.Y - size * 0.6f),
            new Vector2(center.X - size * 0.25f, center.Y + size * 0.6f),
            new Vector2(center.X + size * 0.55f, center.Y),
            col
        );
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
