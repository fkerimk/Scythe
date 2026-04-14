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

    private          int     _rowCount;
    private readonly float[] _lastRowY = new float[128];

    // Rename
    private Obj?    _renamingObj;
    private string  _renameBuf = "";
    private bool    _reqRenameFocus;
    private Action? _scheduledRenameAction;

    protected override void OnDraw() {

        if (Core.ActiveLevel == null) return;

        if (IsMouseReleased(ImGuiMouseButton.Left)) IsDragCancelled = false;

        _rowCount = 0;
        Array.Clear(_lastRowY, 0, _lastRowY.Length);

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
        DrawObject(Core.ActiveLevel.Root);

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

    private bool DrawObject(Obj obj, int indent = 0) {

        if (Core.ActiveLevel == null) return true;

        var drawList  = GetWindowDrawList();
        var rowPos    = GetCursorScreenPos();
        var rowHeight = GetFrameHeight();
        var centerY   = rowPos.Y + rowHeight / 2f;

        // Zebra stripping
        if (_rowCount % 2 == 0) {

            drawList.AddRectFilled(new Vector2(GetWindowPos().X, rowPos.Y), new Vector2(GetWindowPos().X + GetWindowWidth(), rowPos.Y + rowHeight), GetColorU32(new Vector4(1, 1, 1, 0.012f)));
        }

        _rowCount++;

        // Hierarchy lines
        var lineCol = GetColorU32(new Vector4(1, 1, 1, 0.15f));

        if (indent > 0) {

            var indentStep = GetStyle().IndentSpacing;
            var lineX      = rowPos.X - indentStep + 10f;

            // Horizontal line
            drawList.AddLine(new Vector2(lineX, centerY), new Vector2(lineX + 11f, centerY), lineCol);

            // Vertical line
            if (_lastRowY[indent] > 0) drawList.AddLine(new Vector2(lineX, _lastRowY[indent]), new Vector2(lineX, centerY), lineCol);
        }

        _lastRowY[indent] = centerY;

        if (SelectedObjects.Any(s => IsAncestorOf(obj, s))) SetNextItemOpen(true);

        // tree node
        var id         = "##" + obj.GetHashCode();
        var isOpen     = GetStateStorage().GetInt(GetID(id), 1) != 0;
        var isSelected = SelectedObjects.Contains(obj);

        var arrowColor = isOpen ? Colors.GuiTreeEnabled : Colors.GuiTreeDisabled;

        if (obj.Children.Count == 0) arrowColor = Colors.Clear;

        var flags             = ImGuiTreeNodeFlags.AllowOverlap | ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.DefaultOpen;
        if (isSelected) flags |= ImGuiTreeNodeFlags.Selected;

        SetCursorPos(new Vector2(GetCursorPosX() - 5f, GetCursorPosY()));

        PushStyleColor(ImGuiCol.Text,   arrowColor.ToVector4());
        PushStyleColor(ImGuiCol.Header, Colors.GuiTreeSelected.ToVector4());
        var tree = TreeNodeEx(id, flags);
        PopStyleColor();
        PopStyleColor();

        // Selection Handling
        var multi = IsKeyDown(ImGuiKey.LeftCtrl) || IsKeyDown(ImGuiKey.RightCtrl);

        // Right click - context
        if (IsItemHovered() && IsMouseReleased(ImGuiMouseButton.Right))
            OpenPopupOnItemClick("context##" + obj.GetHashCode());

        // Left click - select
        else if (IsItemHovered() && IsMouseReleased(ImGuiMouseButton.Left)) SelectObject(obj, multi);

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
                        (model.MakeComponent("Model") as Model)?.Path = path;
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
        SameLine();
        PushFont(Fonts.ImFontAwesomeSmall);
        SetCursorPos(new Vector2(GetCursorPosX() - 10f, GetCursorPosY() + 2.5f));
        TextColored(Colors.GuiTypeObject.ToVector4(), Icons.FaDotCircleO);
        PopFont();

        // Object name
        SameLine();
        PushFont(Fonts.ImMontserratRegular);

        if (_renamingObj == obj) {

            SetCursorPos(new Vector2(GetCursorPosX() - 2.0f, GetCursorPosY() - 4.5f)); // Lift to align box with text baseline

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

            SetCursorPos(new Vector2(GetCursorPosX() - 2.0f, GetCursorPosY() - 1.5f));
            TextColored(new Vector4(1, 1, 1, 1), obj.Name);
        }

        PopFont();

        // Draw child nodes
        if (!tree) return true;

        _lastRowY[indent + 1] = centerY;

        if (obj.Children.Any(child => !DrawObject(child.Value, indent + 1))) {

            TreePop();

            return false;
        }

        TreePop();

        return true;
    }

    public static void SelectObject(Obj? obj, bool multiSelect = false) {

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