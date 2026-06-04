using System.Numerics;
using System.Reflection;
using EnumsNET;
using ImGuiNET;
using Raylib_cs;
using static ImGuiNET.ImGui;

internal partial class ObjectBrowser : Viewport {

    private delegate bool InspectorFieldRenderer(string id, ref object? value, string? pickerType);

    private int _propIndex;
    private readonly IEnumerable<Type> _addComponentTypes;
    private (string Name, string Path, string GUID)[] _foundAssets = [];
    private PickerSearchEntry[] _pickerEntries = [];
    private string _searchFilter = "";
    private bool _showAnimationFrames;
    private readonly Dictionary<string, int> _pendingTextureQuality = new();
    private readonly Dictionary<string, PickerBrowserState> _pickerStates = new();
    private readonly HashSet<int> _forceOpenHeaders = [];
    private static readonly Dictionary<Type, InspectorFieldRenderer> _fieldRenderers = CreateFieldRenderers();
    private static readonly Dictionary<Type, InspectableProperty[]> _inspectablePropertyCache = new();
    private static readonly Dictionary<string, PickerTypeMetadata> _pickerTypeMetadata = CreatePickerTypeMetadata();
    private static readonly Dictionary<CollectionAssetKind, PickerCategoryMetadata> _pickerCategoryMetadata = CreatePickerCategoryMetadata();
    private static readonly Dictionary<string, Action<ObjectBrowser, string>> _assetInspectorByExtension = CreateAssetInspectorByExtension();
    private string? _draggingArrayId;
    private int _draggingArrayIndex = -1;

    public ObjectBrowser() : base("Object") {

        AutoHideWhenEmpty = true;

        var hideComponents = new[] { "Transform" };

        _addComponentTypes = Assembly.GetExecutingAssembly().GetTypes().Where(t => t.IsSubclassOf(typeof(Component)) && !t.IsAbstract && !hideComponents.Contains(t.Name));
    }

    protected override void OnDraw() {

        _propIndex = 0;
        _labelDragDelta = 0;
        _labelWasActivated = false;
        _labelWasDeactivated = false;

        // Asset inspection
        var selectedFile = Editor.SelectedAssetPath;
        if (!string.IsNullOrEmpty(selectedFile)) {

            DrawAssetInspector(selectedFile.Replace('\\', '/'));
            return;
        }

        if (Editor.ProjectSettingsSelected) {

            DrawProjectSettings();
            return;
        }

        if (Core.ActiveLevel == null) return;

        var targets = LevelBrowser.SelectedObjects;
        if (targets.Count == 0) return;

        // Header info
        PushStyleColor(ImGuiCol.Text, Colors.GuiTextDisabled.ToVector4());

        if (targets.Count == 1) {

            if (targets[0].Parent != null) {

                Text(targets[0].Parent?.Name);
                SameLine();
            }
        } else
            Text($"{targets.Count} objects selected");

        PopStyleColor();

        Separator();
        Spacing();

        // Object & component inspection
        DrawProperties(targets.Cast<object>().ToList(), false, "Object");
        DrawProperties(targets.Select(t => (object)t.Transform).ToList(), true, "Transform", false);

        var firstObj = targets[0];

        if (targets.Count == 1) {
            foreach (var component in firstObj.ComponentEntries.Values.ToList())
                DrawProperties([component], true, component.GetType().Name, false);
        } else {
            var componentTypes = targets
                .SelectMany(target => target.ComponentEntries.Values)
                .Select(component => component.GetType().Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, new NaturalStringComparer());

            foreach (var compName in componentTypes) {
                var groupedComponents = targets
                    .Select(t => t.ComponentEntries.Values.Where(component => component.GetType().Name == compName).ToList())
                    .ToList();
                var maxSharedCount = groupedComponents.Min(components => components.Count);

                for (var index = 0; index < maxSharedCount; index++) {
                    var compInstances = groupedComponents
                        .Select(components => components[index])
                        .Cast<object>()
                        .ToList();
                    DrawProperties(compInstances, true, compName, false);
                }
            }
        }

        DrawAddComponentButton(targets);
    }

    protected override bool HasContent() {

        if (!string.IsNullOrEmpty(Editor.SelectedAssetPath)) return true;
        if (Editor.ProjectSettingsSelected) return true;
        if (Core.ActiveLevel == null) return false;

        return LevelBrowser.SelectedObjects.Count > 0;
    }

    private void DrawAddComponentButton(List<Obj> targets) {

        if (targets.Count != 1) return;

        Spacing();
        Separator();
        Spacing();

        if (Button("Add Component", new Vector2(GetContentRegionAvail().X, 0))) OpenPopup("AddComponentPopup");

        if (!BeginPopup("AddComponentPopup")) return;

        foreach (var type in _addComponentTypes) {

            if (!Selectable(type.Name)) continue;

            var targetObj = targets[0];

            if (Activator.CreateInstance(type, targetObj) is not Component component) continue;

            var compName = type.Name;
            History.Execute(
                $"Add Component {compName}",
                redo: () => {
                    if (!targetObj.ComponentEntries.Values.Any(existing => ReferenceEquals(existing, component))) {
                        targetObj.ComponentEntries.Add(component);
                        _forceOpenHeaders.Add(component.GetHashCode());
                        if (targetObj.FindPrefabRoot() != null && Core.ActiveLevel?.IsPrefabDocument != true)
                            PrefabUtility.MarkAsAddedComponent(component);
                    }

                    if (component.Load()) component.IsLoaded = true;
                    if (component is Animation anim && targetObj.ComponentEntries.TryGetValue("Model", out var m)) anim.GUID = (m as Model)!.GUID;
                    if (Core.ActiveLevel != null) Core.ActiveLevel.IsDirty = true;
                },
                undo: () => {
                    var current = targetObj.ComponentEntries.Values.FirstOrDefault(existing => ReferenceEquals(existing, component));
                    if (current == null) return;
                    current.UnloadAndQuit();
                    targetObj.ComponentEntries.Remove(current);
                    if (Core.ActiveLevel != null) Core.ActiveLevel.IsDirty = true;
                }
            );
        }

        EndPopup();
    }


    private (bool changed, bool deactivated) DrawInspectorField(string id, ref object? value, Type type, List<object> targets, string? propName, string? pickerType = null, bool showResetButton = false, bool highlightOverride = false, object? resetValue = null, Action? applyOverride = null, Action? applyOverrideWithHistory = null) {

        var changed = false;
        var deactivated = false;
        PushItemWidth(-1); // Fill the entire column
        var isArrayField = type.IsArray && type.GetArrayRank() == 1;

        if (!isArrayField && DrawPickerButtons(id, pickerType, ref value, targets, propName, trackActivation: true))
            changed = deactivated = true;

        if (!string.IsNullOrEmpty(pickerType) && !isArrayField)
            SetNextItemWidth(GetContentRegionAvail().X);

        var resetButtonSize = GetFrameHeight();
        var useInlineArrayActions = isArrayField && highlightOverride;
        var availableWidth = GetContentRegionAvail().X;
        if (!useInlineArrayActions && showResetButton) availableWidth = MathF.Max(1f, availableWidth - resetButtonSize - 4f);
        if (!useInlineArrayActions && applyOverride != null) availableWidth = MathF.Max(1f, availableWidth - resetButtonSize - 4f);
        SetNextItemWidth(availableWidth);

        if (highlightOverride) {
            PushStyleColor(ImGuiCol.FrameBg, Colors.GuiFieldOverride.ToVector4());
            PushStyleColor(ImGuiCol.FrameBgHovered, Colors.GuiFieldOverrideHovered.ToVector4());
            PushStyleColor(ImGuiCol.FrameBgActive, Colors.GuiFieldOverrideActive.ToVector4());
        }

        changed |= DrawFieldControl(
            id,
            ref value,
            type,
            targets,
            propName,
            pickerType,
            ref deactivated,
            inlineResetVisible: useInlineArrayActions && showResetButton,
            resetValue: resetValue,
            inlineApplyAction: useInlineArrayActions ? applyOverride : null,
            inlineApplyWithHistoryAction: useInlineArrayActions ? applyOverrideWithHistory : null
        );

        HandleInspectorDropTarget(ref value, type, pickerType, targets, propName, ref changed, ref deactivated);

        _labelDragDelta = 0;

        // History Logic inside Universal Control
        if ((IsItemActivated() || _labelWasActivated) && propName != null) {
            targets.ForEach(t => History.StartRecording(t, propName));
            _labelWasActivated = false;
        }

        if (IsItemDeactivated() || _labelWasDeactivated) {
            deactivated = true;
            _labelWasDeactivated = false;
        }

        if (IsItemHovered() && type == typeof(string) && !string.IsNullOrEmpty((string)value!)) SetTooltip(GetAssetTooltip((string)value!, pickerType));

        if (highlightOverride) PopStyleColor(3);

        if (!useInlineArrayActions && showResetButton && highlightOverride) {

            SameLine();
            BeginDisabled(!highlightOverride);
            PushFont(Fonts.ImFontAwesomeSmall);

            if (Button($"{Icons.FaRotateLeft}##{id}_reset", new Vector2(resetButtonSize, resetButtonSize))) {

                if (propName != null) targets.ForEach(t => History.StartRecording(t, propName));
                value = resetValue;
                changed = true;
                deactivated = true;
            }

            PopFont();
            EndDisabled();

            if (IsItemHovered())
                SetTooltip("Reset override");
        }

        if (!useInlineArrayActions && applyOverride != null && highlightOverride) {

            SameLine();
            BeginDisabled(!highlightOverride);
            PushFont(Fonts.ImFontAwesomeSmall);

            if (Button($"{Icons.FaCheck}##{id}_apply", new Vector2(resetButtonSize, resetButtonSize))) {
                if (applyOverrideWithHistory != null) {
                    if (propName != null)
                        History.StopRecording();

                    applyOverrideWithHistory.Invoke();
                    deactivated = true;
                    PopFont();
                    EndDisabled();

                    if (IsItemHovered())
                        SetTooltip("Apply override to prefab");

                    return (changed, deactivated);
                }

                if (propName != null)
                    History.StopRecording();

                var applyTarget = targets.Count == 1 ? targets[0] : null;
                var applyObj = applyTarget switch {
                    Obj obj => obj,
                    Transform transform => transform.Obj,
                    Component component => component.Obj,
                    _ => null
                };
                var applyProperty = applyTarget == null || string.IsNullOrWhiteSpace(propName)
                    ? null
                    : applyTarget.GetType().GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (applyObj != null && applyProperty != null && PrefabUtility.TryGetSourcePrefabFile(applyObj, out var prefabFile) && File.Exists(prefabFile)) {
                    var beforeLocalValue = CloneApplyHistoryValue(applyProperty.GetValue(applyTarget));
                    using var transaction = History.Begin($"Apply {propName ?? "Override"} To Prefab");
                    transaction.CapturePath(prefabFile);
                    transaction.After(
                        redo: () => {
                            if (applyTarget == null) return;
                            if (!PrefabUtility.RefreshSourcePrefabFile(prefabFile)) return;
                            RestoreApplyHistoryTargetState(applyTarget, applyProperty, pickerType, beforeLocalValue, isOverridden: false);
                        },
                        undo: () => {
                            if (applyTarget == null) return;
                            if (!PrefabUtility.RefreshSourcePrefabFile(prefabFile)) return;
                            RestoreApplyHistoryTargetState(applyTarget, applyProperty, pickerType, beforeLocalValue, isOverridden: true);
                        }
                    );
                    applyOverride.Invoke();
                    if (transaction.Commit()) Notifications.Show(transaction.Description);
                } else
                    applyOverride.Invoke();

                deactivated = true;
            }

            PopFont();
            EndDisabled();

            if (IsItemHovered())
                SetTooltip("Apply override to prefab");
        }

        if (!isArrayField)
            DrawPickerPopup(id, type, pickerType, ref value, targets, propName, ref changed, ref deactivated);

        PopItemWidth();
        NextColumn();

        return (changed, deactivated);
    }

    private bool DrawFieldControl(string id, ref object? value, Type type, List<object> targets, string? propName, string? pickerType, ref bool deactivated, bool inlineResetVisible = false, object? resetValue = null, Action? inlineApplyAction = null, Action? inlineApplyWithHistoryAction = null) {

        if (type.IsArray && type.GetArrayRank() == 1 && type.GetElementType() is { } elementType && ScriptFieldUtility.IsSupportedScalarFieldType(elementType))
            return DrawArrayField(id, ref value, elementType, targets, propName, pickerType, ref deactivated, inlineResetVisible, resetValue, inlineApplyAction, inlineApplyWithHistoryAction);

        if (ScriptFieldUtility.IsSceneReferenceType(type))
            return DrawSceneReferenceField(id, ref value, type);

        if (type.IsEnum)
            return DrawEnumField(id, ref value, type);

        return _fieldRenderers.TryGetValue(type, out var renderer) && renderer(id, ref value, pickerType);
    }

    private bool DrawArrayField(string id, ref object? value, Type elementType, List<object> targets, string? propName, string? pickerType, ref bool deactivated, bool inlineResetVisible, object? resetValue, Action? inlineApplyAction, Action? inlineApplyWithHistoryAction) {

        var items = value is Array array
            ? array.Cast<object?>().ToList()
            : new List<object?>();
        var changed = false;
        const float actionSpacing = 8f;
        const float indexWidth = 28f;
        var actionButtonSize = GetFrameHeight();
        var actionButtonCount = 1 + (inlineResetVisible ? 1 : 0) + (inlineApplyAction != null ? 1 : 0);
        var mousePos = GetMousePos();
        var isDraggingThisArray = string.Equals(_draggingArrayId, id, StringComparison.Ordinal) && _draggingArrayIndex >= 0;
        var hoverInsertIndex = -1;
        var indicatorStart = Vector2.Zero;
        var indicatorEnd = Vector2.Zero;
        var firstRowMin = Vector2.Zero;
        var lastRowMax = Vector2.Zero;
        var hasRowBounds = false;

        BeginGroup();

        var count = items.Count;
        var countWidth = MathF.Max(1f, GetContentRegionAvail().X - actionButtonCount * actionButtonSize - actionSpacing * actionButtonCount);
        SetNextItemWidth(countWidth);
        if (DragInt($"##{id}_count", ref count, 0.2f)) {
            StartHistoryCapture(targets, propName);
            count = Math.Max(0, count);

            while (items.Count < count)
                items.Add(ScriptFieldUtility.GetTypeDefault(elementType));

            if (items.Count > count)
                items.RemoveRange(count, items.Count - count);

            changed = true;
            deactivated = true;
        }

        if (IsItemHovered())
            SetTooltip("Array length");

        SameLine(0, actionSpacing);
        PushFont(Fonts.ImFontAwesomeSmall);
        if (Button($"{Icons.FaPlus}##{id}_add", new Vector2(actionButtonSize, actionButtonSize))) {
            StartHistoryCapture(targets, propName);
            items.Add(ScriptFieldUtility.GetTypeDefault(elementType));
            changed = true;
            deactivated = true;
        }

        var resetHovered = false;
        if (inlineResetVisible) {
            SameLine(0, actionSpacing);
            if (Button($"{Icons.FaRotateLeft}##{id}_reset", new Vector2(actionButtonSize, actionButtonSize))) {
                if (propName != null) targets.ForEach(t => History.StartRecording(t, propName));
                items = resetValue is Array resetArray
                    ? resetArray.Cast<object?>().ToList()
                    : [];
                changed = true;
                deactivated = true;
            }
            resetHovered = IsItemHovered();
        }

        var applyHovered = false;
        if (inlineApplyAction != null) {
            SameLine(0, actionSpacing);
            if (Button($"{Icons.FaCheck}##{id}_apply", new Vector2(actionButtonSize, actionButtonSize))) {
                if (inlineApplyWithHistoryAction != null) {
                    if (propName != null)
                        History.StopRecording();

                    inlineApplyWithHistoryAction.Invoke();
                } else {
                    if (propName != null)
                        History.StopRecording();

                    inlineApplyAction.Invoke();
                }

                deactivated = true;
            }
            applyHovered = IsItemHovered();
        }
        PopFont();

        if (resetHovered)
            SetTooltip("Reset override");

        if (applyHovered)
            SetTooltip("Apply override to prefab");

        for (var index = 0; index < items.Count; index++) {
            PushID($"{id}_{index}");

            var rowStart = GetCursorScreenPos();
            var rowWidth = MathF.Max(1f, GetContentRegionAvail().X);
            var rowHeight = GetFrameHeight();
            var rowMin = rowStart;
            var rowMax = rowStart + new Vector2(rowWidth, rowHeight);

            if (!hasRowBounds) {
                firstRowMin = rowMin;
                hasRowBounds = true;
            }

            lastRowMax = rowMax;

            if (isDraggingThisArray
                && mousePos.X >= rowMin.X
                && mousePos.X <= rowMax.X
                && mousePos.Y >= rowMin.Y
                && mousePos.Y <= rowMax.Y) {
                var candidateInsertIndex = mousePos.Y < (rowMin.Y + rowMax.Y) * 0.5f ? index : index + 1;
                if (candidateInsertIndex != _draggingArrayIndex && candidateInsertIndex != _draggingArrayIndex + 1) {
                    hoverInsertIndex = candidateInsertIndex;
                    var indicatorY = hoverInsertIndex == index ? rowMin.Y + 1f : rowMax.Y - 1f;
                    indicatorStart = new Vector2(rowMin.X, indicatorY);
                    indicatorEnd = new Vector2(rowMax.X, indicatorY);
                }
            }

            InvisibleButton($"##reorder_{index}", new Vector2(indexWidth, rowHeight));
            var indexMin = GetItemRectMin();
            var indexHovered = IsItemHovered();

            if (indexHovered)
                SetMouseCursor(ImGuiMouseCursor.Hand);

            var textColor = ColorConvertFloat4ToU32(Colors.GuiTextDisabled.ToVector4());
            var textSize = CalcTextSize(index.ToString());
            var textPos = new Vector2(
                indexMin.X + (indexWidth - textSize.X) * 0.5f,
                indexMin.Y + (GetFrameHeight() - textSize.Y) * 0.5f
            );
            GetWindowDrawList().AddText(textPos, textColor, index.ToString());

            if (IsItemActive() && IsMouseDragging(ImGuiMouseButton.Left)) {
                _draggingArrayId = id;
                _draggingArrayIndex = index;
            }

            SetCursorScreenPos(new Vector2(rowStart.X + indexWidth + actionSpacing, rowStart.Y));

            var elementValue = items[index];
            var availableWidth = MathF.Max(1f, GetContentRegionAvail().X - actionButtonSize - actionSpacing);
            SetNextItemWidth(availableWidth);

            if (DrawInlineFieldControl($"##value_{index}", ref elementValue, elementType, targets, propName, pickerType, ref deactivated)) {
                items[index] = elementValue;
                changed = true;
            }

            SameLine(0, actionSpacing);
            PushFont(Fonts.ImFontAwesomeSmall);

            if (Button($"{Icons.FaXMark}##remove", new Vector2(GetFrameHeight(), GetFrameHeight()))) {
                StartHistoryCapture(targets, propName);
                items.RemoveAt(index);
                index--;
                changed = true;
                deactivated = true;
            }

            PopFont();
            PopID();
        }

        if (isDraggingThisArray && hasRowBounds) {
            if (hoverInsertIndex < 0 && mousePos.X >= firstRowMin.X && mousePos.X <= lastRowMax.X) {
                if (mousePos.Y < firstRowMin.Y) {
                    hoverInsertIndex = 0;
                    indicatorStart = new Vector2(firstRowMin.X, firstRowMin.Y + 1f);
                    indicatorEnd = new Vector2(lastRowMax.X, firstRowMin.Y + 1f);
                } else if (mousePos.Y > lastRowMax.Y) {
                    hoverInsertIndex = items.Count;
                    indicatorStart = new Vector2(firstRowMin.X, lastRowMax.Y - 1f);
                    indicatorEnd = new Vector2(lastRowMax.X, lastRowMax.Y - 1f);
                }
            }

            if (hoverInsertIndex >= 0)
                GetWindowDrawList().AddLine(indicatorStart, indicatorEnd, ColorConvertFloat4ToU32(Colors.Primary.ToVector4()), 2f);
        }

        if (IsMouseReleased(ImGuiMouseButton.Left) && isDraggingThisArray) {
            if (hoverInsertIndex >= 0) {
                var sourceIndex = _draggingArrayIndex;
                var targetIndex = hoverInsertIndex;
                var isNoOpDrop = targetIndex == sourceIndex || targetIndex == sourceIndex + 1;
                if (targetIndex > sourceIndex) targetIndex--;

                if (!isNoOpDrop && targetIndex != sourceIndex) {
                    StartHistoryCapture(targets, propName);
                    var movedItem = items[sourceIndex];
                    items.RemoveAt(sourceIndex);
                    items.Insert(targetIndex, movedItem);
                    changed = true;
                    deactivated = true;
                }
            }

            _draggingArrayId = null;
            _draggingArrayIndex = -1;
        }

        EndGroup();

        if (!changed) return false;

        value = BuildArrayValue(items, elementType);
        return true;
    }

    private bool DrawInlineFieldControl(string id, ref object? value, Type type, List<object> targets, string? propName, string? pickerType, ref bool deactivated) {

        var changed = false;

        if (DrawPickerButtons(id, pickerType, ref value, targets, propName, trackActivation: false))
            changed = deactivated = true;

        if (ScriptFieldUtility.IsSceneReferenceType(type))
            changed |= DrawSceneReferenceField(id, ref value, type);
        else if (type.IsEnum)
            changed |= DrawEnumField(id, ref value, type);
        else if (_fieldRenderers.TryGetValue(type, out var renderer))
            changed |= renderer(id, ref value, pickerType);

        if (IsItemDeactivated())
            deactivated = true;

        if (IsItemHovered() && type == typeof(string) && value is string stringValue && !string.IsNullOrEmpty(stringValue))
            SetTooltip(GetAssetTooltip(stringValue, pickerType));

        DrawPickerPopup(id, type, pickerType, ref value, targets, propName, ref changed, ref deactivated);

        return changed;
    }

    private static Array BuildArrayValue(List<object?> items, Type elementType) {

        var array = Array.CreateInstance(elementType, items.Count);

        for (var i = 0; i < items.Count; i++)
            array.SetValue(items[i] ?? ScriptFieldUtility.GetTypeDefault(elementType), i);

        return array;
    }

    private static void StartHistoryCapture(List<object> targets, string? propName) {

        if (propName == null) return;

        foreach (var target in targets)
            History.StartRecording(target, propName);
    }

    private bool DrawPickerButtons(string id, string? pickerType, ref object? value, List<object> targets, string? propName, bool trackActivation) {

        if (string.IsNullOrEmpty(pickerType))
            return false;

        PushFont(Fonts.ImFontAwesomeSmall);

        if (Button($"{Icons.FaSearch}##{id}_btn"))
            OpenPicker(id, pickerType);

        if (trackActivation) TrackPickerActivation(targets, propName);

        SameLine();

        var cleared = false;
        if (Button($"{Icons.FaXMark}##{id}_clear")) {
            StartHistoryCapture(targets, propName);
            value = IsScenePickerType(pickerType) ? null : "";
            cleared = true;
        }

        if (trackActivation) TrackPickerActivation(targets, propName);

        PopFont();
        SameLine();
        return cleared;
    }

    private void OpenPicker(string id, string pickerType) {

        if (IsScenePickerType(pickerType)) {
            _searchFilter = "";
            OpenPopup($"Picker_{id}");
            return;
        }

        _foundAssets = AssetManager.GetNames(pickerType).ToArray();
        _pickerEntries = BuildPickerEntries(pickerType);
        _searchFilter = "";
        _pickerStates[id] = new PickerBrowserState();
        OpenPopup($"Picker_{id}");
    }

    private static void TrackPickerActivation(List<object> targets, string? propName) {

        if (propName == null) return;

        if (IsItemActivated())
            targets.ForEach(t => History.StartRecording(t, propName));
    }

    private void DrawPickerPopup(string id, Type fieldType, string? pickerType, ref object? value, List<object> targets, string? propName, ref bool changed, ref bool deactivated) {

        SetNextWindowSizeConstraints(new Vector2(320, 100), new Vector2(320, 600));
        SetNextWindowSize(new Vector2(320, 0), ImGuiCond.Appearing);
        if (!BeginPopup($"Picker_{id}")) return;

        SetNextItemWidth(-1);
        InputTextWithHint("##filter", "Search...", ref _searchFilter, 128);

        if (IsScenePickerType(pickerType))
            DrawScenePickerPopup(fieldType, ref value, targets, propName, ref changed, ref deactivated);
        else if (SupportsCollectionPicker(pickerType))
            DrawCollectionAwarePickerPopup(id, pickerType!, ref value, targets, propName, ref changed, ref deactivated);
        else
            DrawFlatPickerPopup(ref value, targets, propName, ref changed, ref deactivated);

        EndPopup();
    }


    private static Dictionary<Type, InspectorFieldRenderer> CreateFieldRenderers() => new() {
        [typeof(string)] = DrawStringField,
        [typeof(float)] = DrawFloatField,
        [typeof(double)] = DrawDoubleField,
        [typeof(int)] = DrawIntField,
        [typeof(bool)] = DrawBoolField,
        [typeof(Vector2)] = DrawVector2Field,
        [typeof(Vector3)] = DrawVector3Field,
        [typeof(Color)] = DrawColorField,
        [typeof(Bool3)] = DrawBool3Field
    };

    private static string? GetScenePickerType(Type type) =>
        ScriptFieldUtility.IsSceneReferenceType(type)
            ? $"SceneRef:{type.AssemblyQualifiedName}"
            : null;

    private static bool IsScenePickerType(string? pickerType) =>
        !string.IsNullOrWhiteSpace(pickerType) && pickerType.StartsWith("SceneRef:", StringComparison.Ordinal);

    private void HandleInspectorDropTarget(ref object? value, Type type, string? pickerType, List<object> targets, string? propName, ref bool changed, ref bool deactivated) {

        if (!BeginDragDropTarget()) return;

        AcceptDragDropPayload("collection_entry");
        AcceptDragDropPayload("object");
        AcceptDragDropPayload("component");
        var hasActivePayload = !string.IsNullOrWhiteSpace(DragDropPayload.Data)
                               || LevelBrowser.DragObject != null
                               || LevelBrowser.DragComponent != null;

        var dropHandled = false;
        var canAcceptDrop = false;
        var invalidReason = "";

        if (!dropHandled && TryResolveDraggedAssetValue(pickerType, out var draggedAssetValue, out var assetInvalidReason)) {
            if (draggedAssetValue != null) {
                canAcceptDrop = true;

                if (IsMouseReleased(ImGuiMouseButton.Left)) {
                    ApplyPickerValue(draggedAssetValue, ref value, targets, propName, ref changed, ref deactivated);
                    dropHandled = true;
                }
            } else {
                invalidReason = assetInvalidReason;
            }
        }

        if (!dropHandled && TryResolveDraggedSceneValue(type, out var draggedSceneValue, out var sceneInvalidReason)) {
            if (draggedSceneValue != null) {
                canAcceptDrop = true;

                if (IsMouseReleased(ImGuiMouseButton.Left)) {
                    ApplyPickerValue(draggedSceneValue, ref value, targets, propName, ref changed, ref deactivated);
                    dropHandled = true;
                }
            } else if (string.IsNullOrWhiteSpace(invalidReason)) {
                invalidReason = sceneInvalidReason;
            }
        }

        if (hasActivePayload && canAcceptDrop) {
            var drawList = GetWindowDrawList();
            drawList.AddRect(GetItemRectMin(), GetItemRectMax(), GetColorU32(Colors.Primary.ToVector4()), 4f, ImDrawFlags.None, 1.5f);
        }

        if (hasActivePayload && !dropHandled && !string.IsNullOrWhiteSpace(invalidReason) && IsMouseReleased(ImGuiMouseButton.Left))
            Notifications.Show(invalidReason);

        if (IsMouseReleased(ImGuiMouseButton.Left)) {
            LevelBrowser.DragComponent = null;
            LevelBrowser.DragObject = null;
        }

        EndDragDropTarget();
    }

    private static bool TryResolveDraggedAssetValue(string? pickerType, out object? value, out string invalidReason) {

        value = null;
        invalidReason = "";

        if (string.IsNullOrWhiteSpace(pickerType) || IsScenePickerType(pickerType)) return false;
        if (string.IsNullOrWhiteSpace(DragDropPayload.Data)) return false;

        var draggedPath = DragDropPayload.Data;
        if (Directory.Exists(draggedPath)) {
            if (!CollectionData.TryGetCollectionSelectionValue(draggedPath, pickerType, out var collectionValue)) {
                invalidReason = $"Drop failed: '{Path.GetFileName(draggedPath)}' is not compatible with this field.";
                return true;
            }

            value = collectionValue;
            return true;
        }

        if (!File.Exists(draggedPath)) return false;

        if (!CollectionData.IsPathCompatibleWithPicker(draggedPath, pickerType)) {
            invalidReason = $"Drop failed: '{Path.GetFileName(draggedPath)}' is not compatible with this field.";
            return true;
        }

        var resolvedValue = ResolvePickerAssetValue(draggedPath, pickerType);
        if (string.IsNullOrWhiteSpace(resolvedValue)) {
            invalidReason = $"Drop failed: Could not resolve '{Path.GetFileName(draggedPath)}'.";
            return true;
        }

        value = resolvedValue;
        return true;
    }

    private static bool TryResolveDraggedSceneValue(Type targetType, out object? value, out string invalidReason) {

        value = null;
        invalidReason = "";

        if (!ScriptFieldUtility.IsSceneReferenceType(targetType)) return false;

        if (LevelBrowser.DragComponent != null) {
            var draggedComponent = LevelBrowser.DragComponent;
            if (draggedComponent is Script script && typeof(ScytheScript).IsAssignableFrom(targetType)) {
                var scriptType = script.GetAsset()?.ScriptType;
                if (scriptType != null && targetType.IsAssignableFrom(scriptType)) {
                    value = SceneReferenceValue.FromTarget(script);
                    return true;
                }
            }

            if (targetType.IsInstanceOfType(draggedComponent)) {
                value = SceneReferenceValue.FromTarget(draggedComponent);
                return true;
            }

            invalidReason = $"Drop failed: '{draggedComponent.GetType().Name}' is not compatible with this field.";
            return true;
        }

        if (LevelBrowser.DragObject != null) {
            var draggedObject = LevelBrowser.DragObject;

            if (targetType == typeof(Obj)) {
                value = SceneReferenceValue.FromTarget(draggedObject);
                return true;
            }

            if (TryResolveSceneTargetFromObject(draggedObject, targetType, out var resolvedTarget)) {
                value = SceneReferenceValue.FromTarget(resolvedTarget);
                return true;
            }

            invalidReason = $"Drop failed: '{draggedObject.Name}' is not compatible with this field.";
            return true;
        }

        return false;
    }

    private static bool TryResolveSceneTargetFromObject(Obj obj, Type targetType, out object resolvedTarget) {

        resolvedTarget = null!;

        if (typeof(ScytheScript).IsAssignableFrom(targetType)) {
            foreach (var component in obj.ComponentEntries.Values.OfType<Script>()) {
                var scriptType = component.Instance?.GetType() ?? component.GetAsset()?.ScriptType;
                if (scriptType == null || !targetType.IsAssignableFrom(scriptType)) continue;

                resolvedTarget = component;
                return true;
            }

            return false;
        }

        if (!typeof(Component).IsAssignableFrom(targetType)) return false;

        foreach (var component in obj.ComponentEntries.Values) {
            if (!targetType.IsInstanceOfType(component)) continue;

            resolvedTarget = component;
            return true;
        }

        return false;
    }

    private static void NavigateToPickerReference(object? value, string? pickerType, Type targetType) {

        if (value == null) return;

        if (!string.IsNullOrWhiteSpace(pickerType) && !IsScenePickerType(pickerType)) {
            var selectedValue = value as string;
            var path = ResolveAbsoluteAssetReferencePath(selectedValue, pickerType);

            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                Editor.SetSelectedAsset(path);

            return;
        }

        var sceneReferenceContext = LevelBrowser.SelectedObject ?? Core.ActiveLevel?.Root;

        var sceneTarget = value is SceneReferenceValue reference
            ? ScriptFieldUtility.ResolveStoredValueForAssignment(reference, targetType, sceneReferenceContext)
            : value;

        var obj = sceneTarget switch {
            Obj sceneObj => sceneObj,
            ScytheScript script => script.Obj,
            Component component => component.Obj,
            _ => null
        };

        if (obj == null && value is SceneReferenceValue unresolvedReference && sceneReferenceContext != null) {
            var current = unresolvedReference.IsPrefabLocal
                ? sceneReferenceContext.FindPrefabRoot() ?? sceneReferenceContext.GetRoot()
                : sceneReferenceContext.GetRoot();

            foreach (var segment in unresolvedReference.Path) {
                if (!current.ChildEntries.TryGetValue(segment.Name, segment.Occurrence, out var next)) {
                    current = null!;
                    break;
                }

                current = next;
            }

            obj = current;
        }

        if (obj != null)
            LevelBrowser.SelectObject(obj);
    }

    private static string ResolveAbsoluteAssetReferencePath(string? selectedValue, string pickerType) =>
        pickerType switch {
            "ShaderAsset" => AssetManager.GetPath<ShaderAsset>(selectedValue ?? "") ?? "",
            "LevelAsset" => AssetManager.GetPath<LevelAsset>(selectedValue ?? "") ?? "",
            "PrefabAsset" => AssetManager.GetPath<PrefabAsset>(selectedValue ?? "") ?? "",
            "TextureAsset" => AssetManager.GetPath<TextureAsset>(selectedValue ?? "") ?? "",
            "ModelAsset" => AssetManager.GetPath<ModelAsset>(selectedValue ?? "") ?? "",
            "AnimationAsset" => AssetManager.GetPath<AnimationAsset>(selectedValue ?? "") ?? "",
            "MaterialAsset" => AssetManager.GetPath<MaterialAsset>(selectedValue ?? "") ?? "",
            "ScriptAsset" => AssetManager.GetPath<ScriptAsset>(selectedValue ?? "") ?? "",
            _ => ""
        };











    private void DrawProjectSettings() {

        PushID("ProjectSettings");
        DrawSectionHeader("Project", Icons.FaHouse, Colors.GuiText, out var open);

        if (open) {
            DrawConfigField("Name", "ProjectName", ProjectConfig.Current.Name, typeof(string), nameof(ProjectConfig.Name), null, value => {
                ProjectConfig.Current.Name = (string)value!;
                ProjectConfig.Current.Save();
            });

            DrawConfigField("Startup Level", "ProjectStartupLevel", ProjectConfig.Current.StartupLevel, typeof(string), nameof(ProjectConfig.StartupLevel), "LevelAsset", value => {
                ProjectConfig.Current.StartupLevel = (string)value!;
                ProjectConfig.Current.StartupLevelPath = AssetManager.GetPath<LevelAsset>(ProjectConfig.Current.StartupLevel) is { } path
                    ? AssetManager.GetStoredPath(path)
                    : "";
                ProjectConfig.Current.Save();
            });

            DrawConfigField("Background Scripts", "ProjectBackgroundScripts", ProjectConfig.Current.BackgroundScripts, typeof(string[]), nameof(ProjectConfig.BackgroundScripts), "ScriptAsset", value => {
                ProjectConfig.Current.BackgroundScripts = (string[]?)value ?? [];
                SyncProjectBackgroundScriptPaths();
                ProjectConfig.Current.Save();
            });
        }

        EndSection(open);
        PopID();
    }

    private void DrawLevelSettings() {

        var level = Core.ActiveLevel;
        if (level == null) return;

        PushID(level.GUID);
        DrawSectionHeader("Level", Icons.FaMap, Colors.GuiCollectionLevel, out var open);

        if (open) {

            DrawShadowedLabel("Name");
            TextDisabled(level.Name);
            NextColumn();

            DrawConfigField("Skybox", "LevelSkybox", level.Skybox, typeof(string), nameof(Level.Skybox), "TextureAsset", value => {
                level.Skybox = (string)value!;
                level.SkyboxPath = AssetManager.GetPath<TextureAsset>(level.Skybox) is { } path
                    ? AssetManager.GetStoredPath(path)
                    : "";
                level.IsDirty = true;
                Core.ApplyLevelVisualSettings();
            }, level);

            DrawConfigField("Background Color", "LevelBackgroundColor", level.BackgroundColor, typeof(Color), nameof(Level.BackgroundColor), null, value => {
                level.BackgroundColor = (Color)value!;
                level.IsDirty = true;
            }, level);

            DrawConfigField("Ambient Color", "LevelAmbientColor", level.AmbientColor, typeof(Color), nameof(Level.AmbientColor), null, value => {
                level.AmbientColor = (Color)value!;
                level.IsDirty = true;
                Core.ApplyLevelVisualSettings();
            }, level);
        }

        EndSection(open);
        PopID();
    }













    private void DrawAnimationPreviewControls(Animation animation) {

        if (!animation.HasPreviewClip) return;

        Spacing();
        Separator();
        Spacing();

        var isPlaying = animation.EditorPreviewPlaying;
        PushFont(Fonts.ImFontAwesomeSmall);

        var playHovered = false;
        if (Button(isPlaying ? Icons.FaPause : Icons.FaPlay, new Vector2(30, 24)))
            if (isPlaying)
                animation.PausePreview();
            else
                animation.PlayPreview();

        playHovered = IsItemHovered();

        SameLine();

        var stopHovered = false;
        if (Button(Icons.FaStop, new Vector2(30, 24))) animation.StopPreview();

        stopHovered = IsItemHovered();

        SameLine();

        var modeHovered = false;
        if (Button(_showAnimationFrames ? Icons.FaFilm : Icons.FaClock, new Vector2(30, 24)))
            _showAnimationFrames = !_showAnimationFrames;

        modeHovered = IsItemHovered();

        PopFont();

        if (playHovered) SetTooltip(isPlaying ? "Pause" : "Play");
        if (stopHovered) SetTooltip("Stop");
        if (modeHovered) SetTooltip(_showAnimationFrames ? "Frames" : "Seconds");

        var duration = _showAnimationFrames ? animation.DurationFrames : animation.DurationSeconds;
        var value = _showAnimationFrames ? animation.CurrentFrame : animation.CurrentTime;
        var max = Math.Max(duration, 0.0001f);
        var format = _showAnimationFrames
            ? $"{value:0}f / {duration:0}f"
            : $"{value:0.00}s / {duration:0.00}s";

        SameLine();
        SetNextItemWidth(GetContentRegionAvail().X);
        BeginDisabled(duration <= 0f);
        if (SliderFloat("##animation_time", ref value, 0f, max, format))
            if (_showAnimationFrames)
                animation.CurrentFrame = value;
            else
                animation.CurrentTime = value;
        EndDisabled();
    }

    // Asset inspectors





    private void DrawProperties(List<object> targets, bool separator, string title, bool defaultOpen = true) {

        if (targets.Count == 0) return;
        var first = targets[0];
        PushID(first.GetHashCode());

        var open = true;

        if (separator) {

            var icon = first is Component c ? c.LabelIcon : Icons.FaCube;
            var color = first is Component cc ? cc.LabelColor : Colors.GuiTypeModel;
            var isRemovable = first is Component and not Transform && targets.Count == 1;
            var forceOpen = _forceOpenHeaders.Remove(first.GetHashCode());
            var subtitle = first is Script scriptComponent
                ? GetScriptHeaderSubtitle(scriptComponent)
                : null;

            DrawSectionHeader(
                title,
                icon,
                color,
                out open,
                isRemovable,
                () => {

                    var comp = (first as Component)!;
                    var targetObj = comp.Obj;
                    var name = comp.GetType().Name;
                    History.Execute(
                        $"Remove {name}",
                        redo: () => {
                            var current = targetObj.ComponentEntries.Values.FirstOrDefault(c => ReferenceEquals(c, comp));
                            if (current == null) return;
                            current.UnloadAndQuit();
                            targetObj.ComponentEntries.Remove(current);
                            if (Core.ActiveLevel != null) Core.ActiveLevel.IsDirty = true;
                        },
                        undo: () => {
                            if (targetObj.ComponentEntries.Values.Any(c => ReferenceEquals(c, comp))) return;
                            targetObj.ComponentEntries.Add(comp);
                            if (comp.Load()) comp.IsLoaded = true;
                            if (Core.ActiveLevel != null) Core.ActiveLevel.IsDirty = true;
                        }
                    );

                },
                defaultOpen || forceOpen,
                first as Component,
                subtitle
            );

        } else {

            PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 8));
            Columns(2, "##props", false);
            SetColumnWidth(0, GetWindowWidth() * 0.3f); // Reduced label width
        }

        if (open) {

            if (separator && first is Component component && targets.Count == 1 && PrefabUtility.IsAddedComponentOverride(component)) {

                Columns(1);
                PopStyleVar();

                if (Button("Apply Component To Prefab", new Vector2(GetContentRegionAvail().X, 0))) {
                    if (PrefabUtility.TryGetSourcePrefabFile(component.Obj, out var prefabFile) && File.Exists(prefabFile)) {
                        using var transaction = History.Begin($"Apply Component {component.GetType().Name} To Prefab");
                        transaction.CapturePath(prefabFile);
                        transaction.After(
                            redo: () => PrefabUtility.RefreshSourcePrefabFile(prefabFile),
                            undo: () => PrefabUtility.RefreshSourcePrefabFile(prefabFile)
                        );
                        PrefabUtility.ApplyAddedComponentToPrefab(component);
                        if (transaction.Commit()) Notifications.Show(transaction.Description);
                    } else
                        PrefabUtility.ApplyAddedComponentToPrefab(component);

                    if (Core.ActiveLevel != null) Core.ActiveLevel.IsDirty = true;
                }

                if (Button("Revert Added Component", new Vector2(GetContentRegionAvail().X, 0))) {
                    var targetObj = component.Obj;
                    var componentName = component.GetType().Name;
                    History.Execute(
                        $"Revert Added Component {componentName}",
                        redo: () => {
                            PrefabUtility.RevertAddedComponent(component);
                            if (Core.ActiveLevel != null) Core.ActiveLevel.IsDirty = true;
                        },
                        undo: () => {
                            if (targetObj.ComponentEntries.Values.Any(existing => ReferenceEquals(existing, component))) return;
                            targetObj.ComponentEntries.Add(component);
                            if (component.Load()) component.IsLoaded = true;
                            if (Core.ActiveLevel != null) Core.ActiveLevel.IsDirty = true;
                        }
                    );
                    TreePop();
                    Spacing();
                    PopID();
                    return;
                }

                Spacing();
                Separator();
                Spacing();

                PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 8));
                Columns(2, $"##{title}_cols_added_component", false);
                SetColumnWidth(0, GetWindowWidth() * 0.3f);
            }

            foreach (var inspectableProperty in GetInspectableProperties(first.GetType())) {

                var prop = inspectableProperty.Property;
                var id = $"##prop_{_propIndex++}";
                var values = targets.Select(prop.GetValue).ToList();
                var allSame = values.All(v => Equals(v, values[0]));
                var val = allSame ? values[0] : null;
                var picker = inspectableProperty.PickerType;
                var (highlightOverride, resetValue) = GetPrefabOverrideState(first, prop);
                var applyOverride = GetPrefabApplyAction(first, prop, val);

                DrawShadowedLabel(inspectableProperty.Label, highlightOverride);

                var (changed, deactivated) = DrawInspectorField(id, ref val, prop.PropertyType, targets, prop.Name, picker, showResetButton: highlightOverride, highlightOverride: highlightOverride, resetValue: resetValue, applyOverride: applyOverride);

                if (changed) {

                    foreach (var t in targets) {

                        prop.SetValue(t, val);
                        SyncAssetReferencePath(t, prop, picker, val as string);
                        ApplyPrefabOverrideMarker(t, prop, val, resetValue);
                        if (t is Component comp && inspectableProperty.HasAssetBinding) comp.UnloadAndQuit();
                    }

                    if (Core.ActiveLevel != null) Core.ActiveLevel.IsDirty = true;
                }

                if (deactivated) History.StopRecording();
            }

            if (first is Script script && targets.All(target => target is Script)) {

                var asset = script.GetAsset();

                if (asset?.ScriptType != null)
                    DrawScriptFieldRows(targets, asset, ScriptFieldStorageKind.Expose);
            }
        }

        if (separator) {

            if (open && first is Animation animation && targets.Count == 1) {

                Columns(1);
                PopStyleVar();
                DrawAnimationPreviewControls(animation);
                TreePop();
                Spacing();

            } else
                EndSection(open);

        } else {

            Columns(1);
            PopStyleVar();
        }

        PopID();
    }

    private static string? GetScriptHeaderSubtitle(Script scriptComponent) {

        var asset = scriptComponent.GetAsset();
        if (asset == null) return null;

        return Path.GetFileNameWithoutExtension(asset.File);
    }



    private static (bool HighlightOverride, object? ResetValue) GetPrefabOverrideState(object target, PropertyInfo property) {

        if (!IsPrefabBoundTarget(target)) return (false, null);

        if (target is Obj obj && TryGetPrefabSourceValue(obj, property, out var objValue))
            return (!ObjectGraph.AreEqual(property.GetValue(obj), objValue), objValue);

        if (target is Transform transform && TryGetPrefabSourceValue(transform, property, out var transformValue))
            return (!ObjectGraph.AreEqual(property.GetValue(transform), transformValue), transformValue);

        if (target is Component component && TryGetPrefabSourceValue(component, property, out var componentValue))
            return (!ObjectGraph.AreEqual(property.GetValue(component), componentValue), componentValue);

        return (false, null);
    }


    private static bool TryGetPrefabSourceValue(Obj obj, PropertyInfo property, out object? sourceValue) {

        sourceValue = null;
        if (!PrefabUtility.TryGetSourceObject(obj, out var sourceObj) || sourceObj == null) return false;

        var sourceProp = sourceObj.GetType().GetProperty(property.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (sourceProp == null || !sourceProp.CanRead) return false;

        sourceValue = sourceProp.GetValue(sourceObj);
        return true;
    }

    private static bool TryGetPrefabSourceValue(Transform transform, PropertyInfo property, out object? sourceValue) {

        sourceValue = null;
        if (!PrefabUtility.TryGetSourceObject(transform.Obj, out var sourceObj) || sourceObj == null) return false;

        var sourcePropertyName = property.Name == nameof(Transform.Euler) ? nameof(Transform.Euler) : PrefabUtility.GetTransformOverrideKey(property.Name);
        var sourceProp = sourceObj.Transform.GetType().GetProperty(sourcePropertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (sourceProp == null || !sourceProp.CanRead) return false;

        sourceValue = sourceProp.GetValue(sourceObj.Transform);
        return true;
    }

    private static bool TryGetPrefabSourceValue(Component component, PropertyInfo property, out object? sourceValue) {

        sourceValue = null;
        if (!PrefabUtility.TryGetSourceObject(component.Obj, out var sourceObj) || sourceObj == null) return false;
        if (!sourceObj.ComponentEntries.TryGetValue(component.GetType().Name, component.Obj.ComponentEntries.GetOccurrenceIndex(component), out var sourceComponent)) return false;

        var sourceProp = sourceComponent.GetType().GetProperty(property.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (sourceProp == null || !sourceProp.CanRead) return false;

        sourceValue = sourceProp.GetValue(sourceComponent);
        return true;
    }

    private static void SyncAssetReferencePath(object target, PropertyInfo property, string? pickerType, string? selectedValue) {

        if (string.IsNullOrWhiteSpace(pickerType)) return;
        if (!string.Equals(property.Name, "GUID", StringComparison.Ordinal)) return;

        var pathProperty = target.GetType().GetProperty("Path", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (pathProperty == null || pathProperty.PropertyType != typeof(string) || !pathProperty.CanWrite) return;

        pathProperty.SetValue(target, ResolveAssetReferencePath(selectedValue, pickerType));
    }

    private static string ResolveAssetReferencePath(string? selectedValue, string pickerType) {

        if (string.IsNullOrWhiteSpace(selectedValue)) return "";

        return TryGetPickerTypeMetadata(pickerType, out var metadata)
            ? metadata.ResolveStoredPath(selectedValue)
            : "";
    }

    private static void ApplyPrefabOverrideMarker(object target, PropertyInfo property, object? value, object? sourceValue) {

        if (!IsPrefabBoundTarget(target)) return;

        var isOverridden = !ObjectGraph.AreEqual(value, sourceValue);

        if (target is Obj obj)
            obj.SetPrefabOverride(property.Name, isOverridden);
        else if (target is Transform transform)
            transform.SetPrefabOverride(PrefabUtility.GetTransformOverrideKey(property.Name), isOverridden);
        else if (target is Component component)
            component.SetPrefabOverride(property.Name, isOverridden);
    }

    private static Action? GetPrefabApplyAction(object target, PropertyInfo property, object? value) {

        if (!IsPrefabBoundTarget(target)) return null;

        return target switch {
            Obj obj when TryGetPrefabSourceValue(obj, property, out _) => () => PrefabUtility.ApplyObjectPropertyToPrefab(obj, property, property.GetValue(obj)),
            Transform transform when TryGetPrefabSourceValue(transform, property, out _) => () => PrefabUtility.ApplyTransformPropertyToPrefab(transform, property, property.GetValue(transform)),
            Component component when TryGetPrefabSourceValue(component, property, out _) => () => PrefabUtility.ApplyComponentPropertyToPrefab(component, property, property.GetValue(component)),
            _ => null
        };
    }



    private static bool IsPrefabBoundTarget(object target) => target switch {
        Obj obj => obj.FindPrefabRoot() != null,
        Transform transform => transform.Obj.FindPrefabRoot() != null,
        Component component => component.Obj.FindPrefabRoot() != null,
        _ => false
    };


    private void DrawImportedAsset<TAsset>(string path, Action<TAsset> draw) where TAsset : Asset, new() {

        var asset = AssetManager.GetOrImport<TAsset>(path);
        if (asset != null)
            draw(asset);
    }





    private static object? CloneApplyHistoryValue(object? value) {

        if (value == null) return null;
        if (value is string) return value;
        if (value.GetType().IsValueType) return value;
        return ObjectGraph.DeepClone(value);
    }

    private static void RestoreApplyHistoryTargetState(object target, PropertyInfo property, string? pickerType, object? localValue, bool isOverridden) {

        var restoredValue = CloneApplyHistoryValue(localValue);
        property.SetValue(target, restoredValue);
        SyncAssetReferencePath(target, property, pickerType, restoredValue as string);

        switch (target) {
            case Obj obj:
                obj.SetPrefabOverride(property.Name, isOverridden);
                break;
            case Transform transform:
                transform.SetPrefabOverride(PrefabUtility.GetTransformOverrideKey(property.Name), isOverridden);
                break;
            case Component component:
                component.SetPrefabOverride(property.Name, isOverridden);
                break;
        }

        if (target is Component componentTarget && pickerType != null)
            componentTarget.UnloadAndQuit();
    }

    private void DrawConfigField(string label, string id, object? currentValue, Type type, string propertyName, string? pickerType, Action<object?> apply, object? target = null) {

        target ??= ProjectConfig.Current;
        DrawShadowedLabel(label);

        object? value = currentValue;
        var (changed, deactivated) = DrawInspectorField(id, ref value, type, [target], propertyName, pickerType);

        if (changed)
            apply(value);

        if (deactivated)
            History.StopRecording();
    }


    private static string FormatFileSize(long bytes) {

        if (bytes < 0) return "-";

        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1) {

            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }

    private static void SyncProjectBackgroundScriptPaths() {

        ProjectConfig.Current.BackgroundScripts ??= [];
        ProjectConfig.Current.BackgroundScriptPaths ??= [];

        var paths = new string[ProjectConfig.Current.BackgroundScripts.Length];

        for (var i = 0; i < ProjectConfig.Current.BackgroundScripts.Length; i++) {
            var guid = ProjectConfig.Current.BackgroundScripts[i] ?? "";
            var path = i < ProjectConfig.Current.BackgroundScriptPaths.Length ? ProjectConfig.Current.BackgroundScriptPaths[i] ?? "" : "";

            if (string.IsNullOrWhiteSpace(guid) && string.IsNullOrWhiteSpace(path)) {
                ProjectConfig.Current.BackgroundScripts[i] = "";
                paths[i] = "";
                continue;
            }

            var lookupGuid = guid;
            var lookupPath = path;
            var asset = AssetManager.ResolveReference<ScriptAsset>(ref lookupGuid, ref lookupPath)
                ?? AssetManager.Get<ScriptAsset>(guid)
                ?? AssetManager.Get<ScriptAsset>(path)
                ?? AssetManager.GetOrImport<ScriptAsset>(path);

            ProjectConfig.Current.BackgroundScripts[i] = lookupGuid;
            paths[i] = asset != null ? AssetManager.GetStoredPath(asset.File) : lookupPath;
        }

        ProjectConfig.Current.BackgroundScriptPaths = paths;
    }

    private static bool SupportsCollectionPicker(string? pickerType) =>
        TryGetPickerTypeMetadata(pickerType, out var metadata) && metadata.Kind != null;

    private static PickerSearchEntry[] BuildPickerEntries(string pickerType) {

        var entries = new List<PickerSearchEntry>();

        foreach (var collectionPath in CollectionData.EnumerateAllCollections()) {
            if (!CollectionData.TryGetCollectionSelectionValue(collectionPath, pickerType, out var value)) continue;

            var logicalPath = CollectionData.GetLogicalCollectionPath(collectionPath);
            var targetPath = CollectionData.GetResolvedTargetAssetPath(collectionPath, pickerType) ?? CollectionData.GetResolvedTargetPath(collectionPath);
            entries.Add(new PickerSearchEntry(
                logicalPath,
                targetPath == null ? logicalPath : AssetManager.GetStoredPath(targetPath),
                value,
                targetPath,
                Icons.FaArchive,
                Vector4.One));
        }

        if (pickerType is "LevelAsset" or "PrefabAsset") {

            foreach (var file in EnumerateProjectDocuments(pickerType == "PrefabAsset")) {
                if (CollectionData.ShouldHideAssetPath(file, pickerType)) continue;

                var storedPath = AssetManager.GetStoredPath(file);
                var label = storedPath.Replace('\\', '/');
                label = CollectionData.GetLevelDisplayName(label);

                var assetGuid = pickerType == "PrefabAsset"
                    ? AssetManager.GetOrImport<PrefabAsset>(file)?.GUID
                    : AssetManager.GetOrImport<LevelAsset>(file)?.GUID;
                if (string.IsNullOrWhiteSpace(assetGuid)) continue;

                entries.Add(new PickerSearchEntry(
                    label,
                    storedPath,
                    assetGuid,
                    file,
                    GetPickerFileIcon(file),
                    GetPickerCategoryColor(CollectionData.GetKindForPickerType(pickerType) ?? CollectionAssetKind.Collection)));
            }

            return entries
                .DistinctBy(entry => entry.Value)
                .OrderBy(entry => entry.Label, new NaturalStringComparer()!)
                .ToArray();
        }

        foreach (var asset in GetNamedAssetsForPicker(pickerType)) {
            if (CollectionData.ShouldHideAssetPath(asset.Path, pickerType)) continue;

            var label = asset.Path.Replace('\\', '/');
            label = TrimPickerLabelExtension(label);
            var absolutePath = ResolveAbsoluteAssetReferencePath(asset.GUID, pickerType);
            entries.Add(new PickerSearchEntry(
                label,
                asset.Path.Replace('\\', '/'),
                asset.GUID,
                absolutePath,
                GetPickerFileIcon(absolutePath),
                GetPickerCategoryColor(CollectionData.GetKindForPickerType(pickerType) ?? CollectionAssetKind.Collection)));
        }

        return entries
            .DistinctBy(entry => entry.Value)
            .OrderBy(entry => entry.Label, new NaturalStringComparer()!)
            .ToArray();
    }

    private static IEnumerable<(string Name, string Path, string GUID)> GetNamedAssetsForPicker(string pickerType) =>
        TryGetPickerTypeMetadata(pickerType, out var metadata)
            ? metadata.GetNamedAssets()
            : [];

    private static IEnumerable<string> EnumerateProjectDocuments(bool prefabs) {

        if (!Directory.Exists(ScytheConfig.Current.Project)) yield break;

        foreach (var file in Directory.EnumerateFiles(ScytheConfig.Current.Project, "*", SearchOption.AllDirectories)) {
            if (CollectionData.IsSidecarMetaFile(file)) continue;

            var normalized = file.Replace('\\', '/');
            if (prefabs) {
                if (!CollectionData.IsPrefab(file)) continue;
            } else if (!normalized.Contains("/Levels/", StringComparison.OrdinalIgnoreCase) && !CollectionData.IsLevel(file)) continue;

            yield return file;
        }
    }


    private static string ResolvePickerAssetValue(string path, string pickerType) =>
        AssetManager.GetGuidForPickerType(path, pickerType);

    private static string GetPickerTypeForKind(CollectionAssetKind kind) =>
        _pickerCategoryMetadata.TryGetValue(kind, out var metadata) ? metadata.PickerType : "";

    private static Vector4 GetPickerCategoryColor(CollectionAssetKind kind) =>
        _pickerCategoryMetadata.TryGetValue(kind, out var metadata) ? metadata.Color : Colors.GuiText.ToVector4();

    private static string GetCategoryIcon(CollectionAssetKind kind) =>
        _pickerCategoryMetadata.TryGetValue(kind, out var metadata) ? metadata.Icon : Icons.FaArchive;

    private static Dictionary<string, Action<ObjectBrowser, string>> CreateAssetInspectorByExtension() =>
        new(StringComparer.OrdinalIgnoreCase) {
            [".png"] = static (browser, path) => browser.DrawImportedAsset<TextureAsset>(path, browser.DrawTextureAssetInspector),
            [".jpg"] = static (browser, path) => browser.DrawImportedAsset<TextureAsset>(path, browser.DrawTextureAssetInspector),
            [".jpeg"] = static (browser, path) => browser.DrawImportedAsset<TextureAsset>(path, browser.DrawTextureAssetInspector),
            [".tga"] = static (browser, path) => browser.DrawImportedAsset<TextureAsset>(path, browser.DrawTextureAssetInspector),
            [".bmp"] = static (browser, path) => browser.DrawImportedAsset<TextureAsset>(path, browser.DrawTextureAssetInspector),
            [".fbx"] = static (browser, path) => browser.DrawModelAssetFromPath(path),
            [".obj"] = static (browser, path) => browser.DrawModelAssetFromPath(path),
            [".gltf"] = static (browser, path) => browser.DrawModelAssetFromPath(path),
            [".iqm"] = static (browser, path) => browser.DrawModelAssetFromPath(path),
            [".cs"] = static (browser, path) => browser.DrawImportedAsset<ScriptAsset>(path, browser.DrawScriptAssetInspector)
        };

    private static Dictionary<string, PickerTypeMetadata> CreatePickerTypeMetadata() {

        var metadata = new Dictionary<string, PickerTypeMetadata>(StringComparer.Ordinal);

        AddPickerType<ShaderAsset>(metadata, "ShaderAsset", null, asset => Path.GetFileNameWithoutExtension(asset.File));
        AddPickerType<LevelAsset>(metadata, "LevelAsset", CollectionAssetKind.Level, asset => CollectionData.GetLevelDisplayName(asset.File));
        AddPickerType<PrefabAsset>(metadata, "PrefabAsset", CollectionAssetKind.Prefab, asset => CollectionData.GetLevelDisplayName(asset.File));
        AddPickerType<TextureAsset>(metadata, "TextureAsset", CollectionAssetKind.Texture, asset => Path.GetFileNameWithoutExtension(asset.File));
        AddPickerType<ModelAsset>(metadata, "ModelAsset", CollectionAssetKind.Model, asset => Path.GetFileNameWithoutExtension(asset.File));
        AddPickerType<AnimationAsset>(metadata, "AnimationAsset", CollectionAssetKind.Model, asset => Path.GetFileNameWithoutExtension(asset.File));
        AddPickerType<MaterialAsset>(metadata, "MaterialAsset", CollectionAssetKind.Material, asset => Path.GetFileNameWithoutExtension(asset.File));
        AddPickerType<ScriptAsset>(metadata, "ScriptAsset", CollectionAssetKind.Script, asset => Path.GetFileNameWithoutExtension(asset.File));

        return metadata;
    }

    private static Dictionary<CollectionAssetKind, PickerCategoryMetadata> CreatePickerCategoryMetadata() => new() {
        [CollectionAssetKind.Level] = new("LevelAsset", Colors.GuiCollectionLevel.ToVector4(), Icons.FaMap),
        [CollectionAssetKind.Material] = new("MaterialAsset", Colors.GuiCollectionMaterial.ToVector4(), Icons.FaFileImage),
        [CollectionAssetKind.Model] = new("ModelAsset", Colors.GuiCollectionModel.ToVector4(), Icons.FaCube),
        [CollectionAssetKind.Prefab] = new("PrefabAsset", Colors.GuiCollectionPrefab.ToVector4(), Icons.FaFile),
        [CollectionAssetKind.Script] = new("ScriptAsset", Colors.GuiCollectionScript.ToVector4(), Icons.FaFileCode),
        [CollectionAssetKind.Texture] = new("TextureAsset", Colors.GuiCollectionTexture.ToVector4(), Icons.FaFileImage)
    };

    private static void AddPickerType<TAsset>(IDictionary<string, PickerTypeMetadata> metadata, string pickerType, CollectionAssetKind? kind, Func<TAsset, string> displaySelector) where TAsset : Asset {

        metadata[pickerType] = new PickerTypeMetadata(
            kind,
            selectedValue => ResolveStoredPath<TAsset>(selectedValue),
            selectedValue => AssetManager.Get<TAsset>(selectedValue) is { } asset ? displaySelector(asset) : selectedValue,
            selectedValue => AssetManager.GetPath<TAsset>(selectedValue) ?? selectedValue,
            () => AssetManager.GetNames(pickerType)
        );
    }


    private static string ResolveStoredPath<TAsset>(string selectedValue) where TAsset : Asset =>
        AssetManager.GetPath<TAsset>(selectedValue) is { } path ? AssetManager.GetStoredPath(path) : "";





    private static bool TryDrawPickerFileThumbnail(string path, float startX, float iconWidth, float thumbnailSize) =>
        TryDrawPickerThumbnail(path, startX, iconWidth, thumbnailSize);


    private static Texture2D? GetPickerThumbnail(string path) {

        if (CollectionData.IsTexture(path)) return AssetManager.GetOrImport<TextureAsset>(path)?.Thumbnail;
        if (CollectionData.IsMaterial(path)) return AssetManager.GetOrImport<MaterialAsset>(path)?.Thumbnail;
        if (CollectionData.IsModel(path)) return AssetManager.GetOrImport<ModelAsset>(path)?.Thumbnail;
        if (CollectionData.IsLevel(path)) return AssetManager.GetOrImport<LevelAsset>(path)?.Thumbnail;
        if (CollectionData.IsPrefab(path)) return AssetManager.GetOrImport<PrefabAsset>(path)?.Thumbnail;

        return null;
    }

    private sealed class PickerBrowserState {
        public string CurrentPath { get; set; } = CollectionData.RootPath;
        public bool ShowChildCollections { get; set; }
        public CollectionAssetKind? ActiveCategory { get; set; }
        public Stack<PickerNavigationState> NavigationStack { get; } = [];
    }

    private readonly record struct InspectableProperty(PropertyInfo Property, string Label, string? PickerType, bool HasAssetBinding);
    private readonly record struct PickerTypeMetadata(CollectionAssetKind? Kind, Func<string, string> ResolveStoredPath, Func<string, string> GetDisplayValue, Func<string, string> GetTooltip, Func<IEnumerable<(string Name, string Path, string GUID)>> GetNamedAssets);
    private readonly record struct PickerCategoryMetadata(string PickerType, Vector4 Color, string Icon);
    private readonly record struct PickerNavigationState(string Path, CollectionAssetKind? ActiveCategory, bool ShowChildCollections);
    private readonly record struct PickerSearchEntry(string Label, string Tooltip, string Value, string? ThumbnailPath, string Icon, Vector4 Color);
}
