using System.Numerics;
using System.Reflection;
using EnumsNET;
using ImGuiNET;
using Raylib_cs;
using static ImGuiNET.ImGui;

internal class ObjectBrowser : Viewport {

    private int _propIndex;
    private readonly IEnumerable<Type> _addComponentTypes;
    private (string Name, string Path, string GUID)[] _foundAssets = [];
    private PickerSearchEntry[] _pickerEntries = [];
    private string _searchFilter = "";
    private bool _showAnimationFrames;
    private readonly Dictionary<string, int> _pendingTextureQuality = new();
    private readonly Dictionary<string, PickerBrowserState> _pickerStates = new();

    public ObjectBrowser() : base("Object") {

        AutoHideWhenEmpty = true;

        var hideComponents = new[] { "Transform" };

        _addComponentTypes = Assembly.GetExecutingAssembly().GetTypes().Where(t => t.IsSubclassOf(typeof(Component)) && !t.IsAbstract && !hideComponents.Contains(t.Name));
    }

    protected override void OnDraw() {

        _propIndex = 0;

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

    private static void DrawShadowedLabel(string label, bool highlighted = false) {

        AlignTextToFramePadding();
        PushFont(Fonts.ImMontserratRegular);
        if (highlighted) PushStyleColor(ImGuiCol.Text, Colors.Primary.ToVector4());
        var cleanLabel = Generators.SplitCamelCase(label);
        var screenPos = GetCursorScreenPos();
        var shadowColor = ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.2f));
        GetWindowDrawList().AddText(screenPos + new Vector2(1f, 1f), shadowColor, cleanLabel);
        TextUnformatted(cleanLabel);
        if (highlighted) PopStyleColor();
        PopFont();
        NextColumn();
    }

    private (bool changed, bool deactivated) DrawInspectorField(string id, ref object? value, Type type, List<object> targets, string? propName, string? pickerType = null, bool showResetButton = false, bool highlightOverride = false, object? resetValue = null, Action? applyOverride = null, Action? applyOverrideWithHistory = null) {

        var changed = false;
        var deactivated = false;
        PushItemWidth(-1); // Fill the entire column

        // Asset Picker Logic
        if (!string.IsNullOrEmpty(pickerType)) {

            PushFont(Fonts.ImFontAwesomeSmall);

            if (Button($"{Icons.FaSearch}##{id}_btn")) {

                var names = AssetManager.GetNames(pickerType);

                _foundAssets = names.ToArray();
                _pickerEntries = BuildPickerEntries(pickerType);
                _searchFilter = "";
                _pickerStates[id] = new PickerBrowserState();

                OpenPopup($"Picker_{id}");
            }

            if (IsItemActivated() && propName != null) targets.ForEach(t => History.StartRecording(t, propName));
            if (IsItemDeactivated()) deactivated = true;

            SameLine();

            if (Button($"{Icons.FaXMark}##{id}_clear")) {
                value = "";
                changed = true;
                deactivated = true;
            }

            if (IsItemActivated() && propName != null) targets.ForEach(t => History.StartRecording(t, propName));
            PopFont();
            SameLine();

            SetNextItemWidth(GetContentRegionAvail().X);
        }

        var resetButtonSize = GetFrameHeight();
        var availableWidth = GetContentRegionAvail().X;
        if (showResetButton) availableWidth = MathF.Max(1f, availableWidth - resetButtonSize - 4f);
        if (applyOverride != null) availableWidth = MathF.Max(1f, availableWidth - resetButtonSize - 4f);
        SetNextItemWidth(availableWidth);

        if (highlightOverride) {
            PushStyleColor(ImGuiCol.FrameBg, Colors.GuiFieldOverride.ToVector4());
            PushStyleColor(ImGuiCol.FrameBgHovered, Colors.GuiFieldOverrideHovered.ToVector4());
            PushStyleColor(ImGuiCol.FrameBgActive, Colors.GuiFieldOverrideActive.ToVector4());
        }

        // Field drawing
        if (type == typeof(string)) {

            var val = (string)(value ?? "");
            var display = GetAssetDisplayValue(val, pickerType);

            if (string.IsNullOrEmpty(display)) display = val;

            if (InputTextWithHint($"##{id}", "None", ref display, 512, string.IsNullOrEmpty(pickerType) ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.ReadOnly) && string.IsNullOrEmpty(pickerType)) {

                value = display;
                changed = true;
            }
        } else if (type == typeof(float)) {

            var val = (float)(value ?? 0f);

            if (InputFloat($"##{id}", ref val)) {

                value = val;
                changed = true;
            }
        } else if (type == typeof(int)) {

            var val = (int)(value ?? 0);

            if (id.Contains("is_")) {

                var bVal = val == 1;

                if (Checkbox($"##{id}", ref bVal)) {

                    value = bVal ? 1 : 0;
                    changed = true;
                }
            } else if (InputInt($"##{id}", ref val)) {

                value = val;
                changed = true;
            }
        } else if (type == typeof(bool)) {

            var val = (bool)(value ?? false);

            if (Checkbox($"##{id}", ref val)) {

                value = val;
                changed = true;
            }
        } else if (type == typeof(Vector3)) {

            var val = (Vector3)(value ?? Vector3.Zero);

            if (InputFloat3($"##{id}", ref val)) {
                value = val;
                changed = true;
            }
        } else if (type == typeof(Bool3)) {

            var val = (Bool3)(value ?? new Bool3(false, false, false));

            PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4, 0));

            if (Checkbox($"##{id}_x", ref val.X)) {
                value = val;
                changed = true;
            }

            SameLine();
            Text("X");
            SameLine();

            if (Checkbox($"##{id}_y", ref val.Y)) {
                value = val;
                changed = true;
            }

            SameLine();
            Text("Y");
            SameLine();

            if (Checkbox($"##{id}_z", ref val.Z)) {
                value = val;
                changed = true;
            }

            SameLine();
            Text("Z");

            PopStyleVar();
        } else if (type == typeof(Vector2)) {

            var val = (Vector2)(value ?? Vector2.Zero);

            if (InputFloat2($"##{id}", ref val)) {

                value = val;
                changed = true;
            }
        } else if (type == typeof(Color)) {

            var col = (Color)(value ?? Color.White);
            var v4 = col.ToVector4();

            if (ColorEdit4($"##{id}", ref v4, ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.NoInputs)) {

                value = v4.ToColor();
                changed = true;
            }
        } else if (type.IsEnum) {

            var val = (Enum)(value ?? Activator.CreateInstance(type)!);
            var names = Enums.GetNames(type, EnumMemberSelection.All).ToArray();
            var index = Array.IndexOf(names, val.ToString());

            if (Combo($"##{id}", ref index, names, names.Length)) {

                value = Enums.Parse(type, names[index]);
                changed = true;
            }
        }

        // History Logic inside Universal Control
        if (IsItemActivated() && propName != null) targets.ForEach(t => History.StartRecording(t, propName));

        if (IsItemDeactivated()) deactivated = true;

        if (IsItemHovered() && type == typeof(string) && !string.IsNullOrEmpty((string)value!)) SetTooltip(GetAssetTooltip((string)value!, pickerType));

        if (highlightOverride) PopStyleColor(3);

        if (showResetButton && highlightOverride) {

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

        if (applyOverride != null && highlightOverride) {

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

        // Picker Popup logic
        SetNextWindowSize(new Vector2(320, 0), ImGuiCond.Appearing);

        if (BeginPopup($"Picker_{id}")) {

            SetNextItemWidth(-1);
            InputTextWithHint("##filter", "Search...", ref _searchFilter, 128);

            if (SupportsCollectionPicker(pickerType))
                DrawCollectionAwarePickerPopup(id, pickerType!, ref value, targets, propName, ref changed, ref deactivated);
            else
                DrawFlatPickerPopup(ref value, targets, propName, ref changed, ref deactivated);

            EndPopup();
        }

        PopItemWidth();
        NextColumn();

        return (changed, deactivated);
    }

    private static void DrawSectionHeader(string title, string icon, Color color, out bool open, bool showRemove = false, Action? onRemove = null, bool defaultOpen = true, Component? comp = null) {

        var flags = ImGuiTreeNodeFlags.AllowOverlap | ImGuiTreeNodeFlags.SpanFullWidth;
        if (defaultOpen) flags |= ImGuiTreeNodeFlags.DefaultOpen;

        Spacing();
        var headerPos = GetCursorScreenPos();
        var headerSize = new Vector2(GetContentRegionAvail().X, GetFrameHeight());
        GetWindowDrawList().AddRectFilled(headerPos, headerPos + headerSize, GetColorU32(ImGuiCol.Header, 0.45f), 2.0f);

        PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 3));
        PushStyleColor(ImGuiCol.Header, new Vector4(0, 0, 0, 0));
        open = TreeNodeEx($"##{title}", flags);

        if (comp != null && BeginDragDropSource()) {

            LevelBrowser.DragComponent = comp;
            SetDragDropPayload("component", IntPtr.Zero, 0);
            Text(title);
            EndDragDropSource();
        }

        PopStyleColor();
        PopStyleVar();

        SameLine();
        SetCursorPosX(GetCursorPosX() - 7.5f);
        SetCursorPosY(GetCursorPosY() + 2.5f);
        PushFont(Fonts.ImFontAwesomeSmall);
        TextColored(color.ToVector4(), icon);
        PopFont();
        SameLine();
        PushFont(Fonts.ImMontserratRegular);
        Text(title);
        PopFont();

        if (showRemove && onRemove != null) {

            SameLine();
            var removeBtnX = GetContentRegionAvail().X + GetCursorPosX() - 22;
            SetCursorPosX(removeBtnX);
            if (SmallButton($"X##rem_{title}")) onRemove();
        }

        if (open) {

            Spacing();
            PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 8));
            Columns(2, $"##{title}_cols", false);
            SetColumnWidth(0, GetWindowWidth() * 0.3f); // Reduced label width
        }
    }

    private static void EndSection(bool open) {

        if (!open) return;

        Columns(1);
        PopStyleVar();
        TreePop();
        Spacing();
    }

    private void DrawProjectSettings() {

        PushID("ProjectSettings");
        DrawSectionHeader("Project", Icons.FaHouse, Colors.GuiText, out var open);

        if (open) {

            DrawShadowedLabel("Name");
            object? name = ProjectConfig.Current.Name;
            var (nameChanged, nameDeactivated) = DrawInspectorField("ProjectName", ref name, typeof(string), [ProjectConfig.Current], nameof(ProjectConfig.Name));

            if (nameChanged) {
                ProjectConfig.Current.Name = (string)name!;
                ProjectConfig.Current.Save();
            }

            if (nameDeactivated) History.StopRecording();

            DrawShadowedLabel("Startup Level");
            object? startupLevel = ProjectConfig.Current.StartupLevel;
            var (levelChanged, levelDeactivated) = DrawInspectorField("ProjectStartupLevel", ref startupLevel, typeof(string), [ProjectConfig.Current], nameof(ProjectConfig.StartupLevel), "LevelAsset");

            if (levelChanged) {
                ProjectConfig.Current.StartupLevel = (string)startupLevel!;
                ProjectConfig.Current.StartupLevelPath = AssetManager.GetPath<LevelAsset>(ProjectConfig.Current.StartupLevel) is { } path
                    ? AssetManager.GetStoredPath(path)
                    : "";
                ProjectConfig.Current.Save();
            }

            if (levelDeactivated) History.StopRecording();
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

            DrawShadowedLabel("Skybox");
            object? skybox = level.Skybox;
            var (skyboxChanged, skyboxDeactivated) = DrawInspectorField("LevelSkybox", ref skybox, typeof(string), [level], nameof(Level.Skybox), "TextureAsset");

            if (skyboxChanged) {
                level.Skybox = (string)skybox!;
                level.SkyboxPath = AssetManager.GetPath<TextureAsset>(level.Skybox) is { } path
                    ? AssetManager.GetStoredPath(path)
                    : "";
                level.IsDirty = true;
                Core.ApplyLevelVisualSettings();
            }

            if (skyboxDeactivated) History.StopRecording();

            DrawShadowedLabel("Background Color");
            object? backgroundColor = level.BackgroundColor;
            var (backgroundChanged, backgroundDeactivated) = DrawInspectorField("LevelBackgroundColor", ref backgroundColor, typeof(Color), [level], nameof(Level.BackgroundColor));

            if (backgroundChanged) {
                level.BackgroundColor = (Color)backgroundColor!;
                level.IsDirty = true;
            }

            if (backgroundDeactivated) History.StopRecording();

            DrawShadowedLabel("Ambient Color");
            object? ambientColor = level.AmbientColor;
            var (ambientChanged, ambientDeactivated) = DrawInspectorField("LevelAmbientColor", ref ambientColor, typeof(Color), [level], nameof(Level.AmbientColor));

            if (ambientChanged) {
                level.AmbientColor = (Color)ambientColor!;
                level.IsDirty = true;
                Core.ApplyLevelVisualSettings();
            }

            if (ambientDeactivated) History.StopRecording();
        }

        EndSection(open);
        PopID();
    }

    private void DrawFlatPickerPopup(ref object? value, List<object> targets, string? propName, ref bool changed, ref bool deactivated) {

        BeginChild("##files", new Vector2(0, 400));

        var nms = _foundAssets.Select(asset => asset.Name).ToList();

        for (var i = 0; i < _foundAssets.Length; i++) {

            var asset = _foundAssets[i];
            var path = asset.Path;
            var name = nms[i];

            if (!string.IsNullOrEmpty(_searchFilter) && !path.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase)) continue;

            if (Selectable($"{name}##{asset.GUID}")) {
                ApplyPickerValue(asset.GUID, ref value, targets, propName, ref changed, ref deactivated);
                CloseCurrentPopup();
            }

            if (string.IsNullOrEmpty(name) || nms.Count(x => x == name) <= 1) continue;

            SameLine();
            TextDisabled(path);
        }

        EndChild();
    }

    private void DrawCollectionAwarePickerPopup(string id, string pickerType, ref object? value, List<object> targets, string? propName, ref bool changed, ref bool deactivated) {

        if (!_pickerStates.TryGetValue(id, out var state)) {
            state = new PickerBrowserState();
            _pickerStates[id] = state;
        }

        if (!string.IsNullOrWhiteSpace(_searchFilter)) {

            BeginChild("##picker_search", new Vector2(0, 400));

            foreach (var entry in _pickerEntries.Where(entry =>
                         entry.Label.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase)
                         || entry.Tooltip.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))) {

                if (Selectable($"{entry.Label}##{entry.Value}")) {
                    ApplyPickerValue(entry.Value, ref value, targets, propName, ref changed, ref deactivated);
                    CloseCurrentPopup();
                }

                if (string.IsNullOrWhiteSpace(entry.Tooltip) || string.Equals(entry.Tooltip, entry.Label, StringComparison.OrdinalIgnoreCase)) continue;

                SameLine();
                TextDisabled(entry.Tooltip);
            }

            EndChild();
            return;
        }

        DrawPickerNavigationBar(state);
        BeginChild("##picker_browser", new Vector2(0, 400));

        var pickerKind = CollectionData.GetKindForPickerType(pickerType);

        if (pickerKind == null) {
            EndChild();
            return;
        }

        if (CollectionData.IsProjectRoot(state.CurrentPath)) {

            foreach (var collectionPath in EnumerateVisiblePickerCollections(state.CurrentPath, pickerType, CollectionAssetKind.Collection))
                DrawPickerCollectionEntry(collectionPath, pickerType, state, ref value, targets, propName, ref changed, ref deactivated);

            EndChild();
            return;
        }

        if (state.ShowChildCollections) {

            foreach (var collectionPath in EnumerateVisiblePickerCollections(state.CurrentPath, pickerType, CollectionAssetKind.Collection))
                DrawPickerCollectionEntry(collectionPath, pickerType, state, ref value, targets, propName, ref changed, ref deactivated);

            EndChild();
            return;
        }

        var childCollections = EnumerateVisiblePickerCollections(state.CurrentPath, pickerType, CollectionAssetKind.Collection).Count();
        if (childCollections > 0)
            DrawPickerVirtualEntry("Collections", childCollections, Colors.GuiCollection.ToVector4(), () => state.ShowChildCollections = true, Icons.FaArchive);

        var activeCategory = state.ActiveCategory ?? pickerKind.Value;
        var activePickerType = GetPickerTypeForKind(activeCategory);
        var typedCollections = EnumerateVisiblePickerCollections(state.CurrentPath, pickerType, activeCategory).ToList();
        var files = GetPickerFilesForCategory(state.CurrentPath, activePickerType);

        foreach (var collectionPath in typedCollections)
            DrawPickerCollectionEntry(collectionPath, pickerType, state, ref value, targets, propName, ref changed, ref deactivated);

        foreach (var filePath in files)
            DrawPickerFileEntry(filePath, activePickerType, ref value, targets, propName, ref changed, ref deactivated);

        EndChild();
    }

    private void DrawPickerNavigationBar(PickerBrowserState state) {

        var canGoUp = !CollectionData.IsProjectRoot(state.CurrentPath) || state.ShowChildCollections || state.ActiveCategory != null || state.NavigationStack.Count > 0;

        BeginDisabled(!canGoUp);
        PushFont(Fonts.ImFontAwesomeSmall);

        if (Button($"{Icons.FaLevelUp}##picker_up")) {

            if (state.ActiveCategory != null)
                state.ActiveCategory = null;
            else if (state.ShowChildCollections)
                state.ShowChildCollections = false;
            else if (state.NavigationStack.Count > 0) {
                var nav = state.NavigationStack.Pop();
                state.CurrentPath = nav.Path;
                state.ShowChildCollections = nav.ShowChildCollections;
                state.ActiveCategory = nav.ActiveCategory;
            }
            else if (CollectionData.IsBuiltInRoot(state.CurrentPath))
                state.CurrentPath = CollectionData.RootPath;
            else
                state.CurrentPath = Directory.GetParent(state.CurrentPath)?.FullName ?? CollectionData.RootPath;
        }

        PopFont();
        EndDisabled();

        SameLine();
        TextDisabled(GetPickerRelativePath(state));
    }

    private static string GetPickerRelativePath(PickerBrowserState state) {

        if (CollectionData.IsBuiltInRoot(state.CurrentPath)) {
            if (state.ShowChildCollections)
                return $"{CollectionData.BuiltInCollectionLabel}/Collections";

            if (state.ActiveCategory != null)
                return $"{CollectionData.BuiltInCollectionLabel}/{CollectionData.GetKindName(state.ActiveCategory.Value)}";

            return CollectionData.BuiltInCollectionLabel;
        }

        var relative = Path.GetRelativePath(CollectionData.RootPath, state.CurrentPath).Replace('\\', '/');
        if (relative == ".") relative = "";

        if (state.ShowChildCollections)
            return string.IsNullOrEmpty(relative) ? "Collections" : $"{relative}/Collections";

        if (state.ActiveCategory != null) {
            var categoryName = CollectionData.GetKindName(state.ActiveCategory.Value);
            return string.IsNullOrEmpty(relative) ? categoryName : $"{relative}/{categoryName}";
        }

        return string.IsNullOrEmpty(relative) ? "Collections" : relative;
    }

    private void DrawPickerVirtualEntry(string label, int count, Vector4 color, Action onClick, string icon) {

        const float iconWidth = 20f;
        var startX = GetCursorPosX();

        PushFont(Fonts.ImFontAwesomeSmall);
        DrawPickerIcon(icon, color, startX, iconWidth);
        PopFont();

        SameLine(startX + iconWidth + 5f);
        PushStyleColor(ImGuiCol.Text, color);
        var clicked = Selectable(label, false, ImGuiSelectableFlags.None, new Vector2(GetContentRegionAvail().X, 0f));
        PopStyleColor();
        DrawPickerRightAlignedCount(count, color);
        if (!clicked) return;

        onClick();
    }

    private void DrawPickerCollectionEntry(string collectionPath, string pickerType, PickerBrowserState state, ref object? value, List<object> targets, string? propName, ref bool changed, ref bool deactivated) {

        const float iconWidth = 20f;
        const float thumbnailSize = 16f;
        var startX = GetCursorPosX();
        var color = Vector4.One;

        PushFont(Fonts.ImFontAwesomeSmall);
        if (!TryDrawPickerCollectionThumbnail(collectionPath, startX, iconWidth, thumbnailSize))
            DrawPickerIcon(Icons.FaArchive, color, startX, iconWidth);
        PopFont();
        SameLine(startX + iconWidth + 5f);

        var label = CollectionData.GetCollectionDisplayName(collectionPath);
        PushStyleColor(ImGuiCol.Text, color);

        if (Selectable($"{label}##{collectionPath}", false, ImGuiSelectableFlags.None, new Vector2(GetContentRegionAvail().X, 0f))) {
            PopStyleColor();

            if (CollectionData.TryGetCollectionSelectionValue(collectionPath, pickerType, out var selectedValue)) {
                ApplyPickerValue(selectedValue, ref value, targets, propName, ref changed, ref deactivated);
                CloseCurrentPopup();
                return;
            }

            state.NavigationStack.Push(new PickerNavigationState(state.CurrentPath, state.ActiveCategory, state.ShowChildCollections));
            state.CurrentPath = collectionPath;
            state.ShowChildCollections = false;
            state.ActiveCategory = null;
            return;
        }

        PopStyleColor();
    }

    private void DrawPickerFileEntry(string path, string pickerType, ref object? value, List<object> targets, string? propName, ref bool changed, ref bool deactivated) {

        const float iconWidth = 20f;
        const float thumbnailSize = 16f;
        var startX = GetCursorPosX();
        var color = GetPickerCategoryColor(CollectionData.GetKindForPickerType(pickerType) ?? CollectionAssetKind.Collection);

        PushFont(Fonts.ImFontAwesomeSmall);
        if (!TryDrawPickerFileThumbnail(path, startX, iconWidth, thumbnailSize))
            DrawPickerIcon(GetPickerFileIcon(path), color, startX, iconWidth);
        PopFont();
        SameLine(startX + iconWidth + 5f);

        var label = CollectionData.GetNameWithoutExtension(path);
        if (!Selectable($"{label}##{path}", false, ImGuiSelectableFlags.None, new Vector2(GetContentRegionAvail().X, 0f))) return;

        var selectedValue = ResolvePickerAssetValue(path, pickerType);

        if (string.IsNullOrWhiteSpace(selectedValue)) return;

        ApplyPickerValue(selectedValue, ref value, targets, propName, ref changed, ref deactivated);
        CloseCurrentPopup();
    }

    private static void ApplyPickerValue(string selectedValue, ref object? value, List<object> targets, string? propName, ref bool changed, ref bool deactivated) {

        if (propName != null) targets.ForEach(t => History.StartRecording(t, propName));

        value = selectedValue;
        changed = true;
        deactivated = true;
    }

    private static List<string> GetPickerFilesForCategory(string currentPath, string pickerType) =>
        !Directory.Exists(currentPath)
            ? []
            : Directory.EnumerateFiles(currentPath)
                .Where(path => !CollectionData.IsSidecarMetaFile(path))
                .Where(path => CollectionData.IsPathCompatibleWithPicker(path, pickerType))
                .Where(path => !CollectionData.ShouldHideAssetPath(path, pickerType))
                .OrderBy(CollectionData.GetNameWithoutExtension, new NaturalStringComparer()!)
                .ToList();

    private static IEnumerable<string> EnumerateVisiblePickerCollections(string currentPath, string pickerType, CollectionAssetKind kind) =>
        GetPickerCollections(currentPath, kind).Where(collectionPath => CollectionHasPickerContent(collectionPath, pickerType));

    private static IEnumerable<string> GetPickerCollections(string currentPath, CollectionAssetKind kind) =>
        CollectionData.IsProjectRoot(currentPath)
            ? CollectionData.EnumerateRootCollections(kind)
            : CollectionData.EnumerateCollections(currentPath, kind);

    private static bool CollectionHasPickerContent(string collectionPath, string pickerType) {

        if (CollectionData.TryGetCollectionSelectionValue(collectionPath, pickerType, out _)) return true;

        if (GetPickerFilesForCategory(collectionPath, pickerType).Count > 0) return true;

        foreach (var childCollection in CollectionData.EnumerateCollections(collectionPath, CollectionAssetKind.Collection))
            if (CollectionHasPickerContent(childCollection, pickerType))
                return true;

        if (CollectionData.GetKindForPickerType(pickerType) is { } pickerKind)
            foreach (var typedCollection in CollectionData.EnumerateCollections(collectionPath, pickerKind))
                if (CollectionHasPickerContent(typedCollection, pickerType))
                    return true;

        return false;
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
    private void DrawAssetInspector(string path) {

        var ext = Path.GetExtension(path).ToLowerInvariant();

        if (CollectionData.IsLevel(path)) {

            var asset = AssetManager.GetOrImport<LevelAsset>(path);

            if (asset != null) DrawLevelAssetInspector(asset);
        } else if (CollectionData.IsMaterial(path)) {

            var asset = AssetManager.GetOrImport<MaterialAsset>(path);

            if (asset != null) DrawMaterialAssetInspector(asset);
        } else if (ext is ".png" or ".jpg" or ".jpeg" or ".tga" or ".bmp") {

            var asset = AssetManager.GetOrImport<TextureAsset>(path);

            if (asset != null) DrawTextureAssetInspector(asset);
        } else if (ext is ".fbx" or ".obj" or ".gltf" or ".iqm") {

            var asset = AssetManager.GetOrImport<ModelAsset>(path) ?? AssetManager.Get<ModelAsset>(Path.GetFileNameWithoutExtension(path));

            if (asset != null) DrawModelAssetInspector(asset);
        } else if (ext == ".cs") {

            var asset = AssetManager.GetOrImport<ScriptAsset>(path);

            if (asset != null) DrawScriptAssetInspector(asset);
        }
    }

    private void DrawLevelAssetInspector(LevelAsset levelAsset) {

        var path = levelAsset.File;
        var levelName = CollectionData.GetLevelDisplayName(path);

        PushID(levelAsset.GUID);
        DrawSectionHeader("Level Asset", Icons.FaMap, Colors.GuiCollectionLevel, out var open);

        if (open) {

            PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 8));
            Columns(2, "##level_asset_props", false);
            SetColumnWidth(0, GetWindowWidth() * 0.32f);

            DrawInfoRow("Name", levelName);

            DrawShadowedLabel("Skybox");
            object? skybox = levelAsset.Skybox;
            var (skyboxChanged, skyboxDeactivated) = DrawInspectorField("LevelAssetSkybox", ref skybox, typeof(string), [levelAsset], nameof(LevelAsset.Skybox), "TextureAsset");

            if (skyboxChanged) {
                levelAsset.Skybox = (string)skybox!;
                levelAsset.SkyboxPath = AssetManager.GetPath<TextureAsset>(levelAsset.Skybox) is { } resolvedPath
                    ? AssetManager.GetStoredPath(resolvedPath)
                    : "";
                levelAsset.SaveSettings();
                levelAsset.ApplyToActiveLevelIfOpen();
            }

            if (skyboxDeactivated) History.StopRecording();

            DrawShadowedLabel("Skybox Tint");
            object? tint = levelAsset.SkyboxTint;
            var (skyboxTintChanged, skyboxTintDeactivated) = DrawInspectorField("LevelAssetSkyboxTint", ref tint, typeof(Color), [levelAsset], nameof(LevelAsset.SkyboxTint));

            if (skyboxTintChanged) {
                levelAsset.SkyboxTint = (Color)tint!;
                levelAsset.SaveSettings();
                levelAsset.ApplyToActiveLevelIfOpen();
            }

            if (skyboxTintDeactivated) History.StopRecording();

            DrawShadowedLabel("Background Color");
            object? background = levelAsset.BackgroundColor;
            var (backgroundChanged, backgroundDeactivated) = DrawInspectorField("LevelAssetBackground", ref background, typeof(Color), [levelAsset], nameof(LevelAsset.BackgroundColor));

            if (backgroundChanged) {
                levelAsset.BackgroundColor = (Color)background!;
                levelAsset.SaveSettings();
                levelAsset.ApplyToActiveLevelIfOpen();
            }

            if (backgroundDeactivated) History.StopRecording();

            DrawShadowedLabel("Ambient Color");
            object? ambient = levelAsset.AmbientColor;
            var (ambientChanged, ambientDeactivated) = DrawInspectorField("LevelAssetAmbient", ref ambient, typeof(Color), [levelAsset], nameof(LevelAsset.AmbientColor));

            if (ambientChanged) {
                levelAsset.AmbientColor = (Color)ambient!;
                levelAsset.SaveSettings();
                levelAsset.ApplyToActiveLevelIfOpen();
            }

            if (ambientDeactivated) History.StopRecording();

            DrawShadowedLabel("Skybox Ambient");
            object? skyboxAmbient = levelAsset.SkyboxAmbientEnabled;
            var (skyboxAmbientChanged, skyboxAmbientDeactivated) = DrawInspectorField("LevelAssetSkyboxAmbientEnabled", ref skyboxAmbient, typeof(bool), [levelAsset], nameof(LevelAsset.SkyboxAmbientEnabled));

            if (skyboxAmbientChanged) {
                levelAsset.SkyboxAmbientEnabled = (bool)skyboxAmbient!;
                levelAsset.SaveSettings();
                levelAsset.ApplyToActiveLevelIfOpen();
            }

            if (skyboxAmbientDeactivated) History.StopRecording();

            DrawShadowedLabel("Skybox Ambient Intensity");
            object? skyboxAmbientIntensityValue = levelAsset.SkyboxAmbientIntensity;
            var (skyboxAmbientIntensityChanged, skyboxAmbientIntensityDeactivated) = DrawInspectorField("LevelAssetSkyboxAmbientIntensity", ref skyboxAmbientIntensityValue, typeof(float), [levelAsset], nameof(LevelAsset.SkyboxAmbientIntensity));

            if (skyboxAmbientIntensityChanged) {
                levelAsset.SkyboxAmbientIntensity = Math.Clamp((float)skyboxAmbientIntensityValue!, 0.0f, 1.0f);
                levelAsset.SaveSettings();
                levelAsset.ApplyToActiveLevelIfOpen();
            }

            if (skyboxAmbientIntensityDeactivated) History.StopRecording();

            Columns(1);
            PopStyleVar();
        }

        EndSection(open);
        PopID();
    }

    private void DrawModelAssetInspector(ModelAsset model) {

        PushID(model.GetHashCode());
        DrawSectionHeader("Model Asset", Icons.FaCube, Colors.GuiTypeModel, out var open);

        if (open) {
            PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 8));
            Columns(2, "##model_asset_props", false);
            SetColumnWidth(0, GetWindowWidth() * 0.32f);

            DrawInfoRow("Source Size", FormatFileSize(model.File));
            DrawInfoRow("Imported Size", FormatFileSize(model.ImportedFile));

            DrawShadowedLabel("Import Scale");

            object? scale = model.Settings.ImportScale;

            var (sChanged, sDeactivated) = DrawInspectorField("ImportScale", ref scale, typeof(float), [model], "Settings");

            if (sChanged) {

                model.Settings.ImportScale = (float)scale!;
                model.SaveSettings();
            }

            if (sDeactivated) History.StopRecording();

            for (var i = 0; i < model.Materials.Length; i++) {

                var name = i < model.Meshes.Count && !string.IsNullOrEmpty(model.Meshes[i].Name) ? model.Meshes[i].Name : $"Mesh {i}";
                DrawShadowedLabel(name);
                object? val = model.MaterialPaths[i];

                var (changed, deactivated) = DrawInspectorField($"MeshMat_{i}", ref val, typeof(string), [model], "Settings", "MaterialAsset");

                if (changed) model.ApplyMaterial(i, (string)val!);

                if (deactivated) History.StopRecording();
            }

            Columns(1);
            PopStyleVar();
        }

        EndSection(open);
        PopID();
    }

    private void DrawMaterialAssetInspector(MaterialAsset mat) {

        PushID(mat.GetHashCode());
        DrawSectionHeader("Material Asset", Icons.FaFileImage, Colors.GuiTypeModel, out var open);

        if (open) {

            DrawShadowedLabel("Shader");

            object? shader = mat.Data.Shader;
            var (shaderChanged, shaderDeactivated) = DrawInspectorField("Shader", ref shader, typeof(string), [mat], "Data", "ShaderAsset");

            if (shaderChanged) {

                mat.Data.Shader = (string)shader!;
                mat.Save();
                mat.ApplyChanges();
            }

            if (shaderDeactivated) History.StopRecording();

            var shaderName = string.IsNullOrEmpty(mat.Data.Shader) ? "Collection/pbr.vs" : mat.Data.Shader;
            var sa = AssetManager.Get<ShaderAsset>(shaderName);

            if (sa != null) {

                foreach (var prop in sa.Properties) {

                    PushID(prop.Name);
                    DrawShadowedLabel(prop.Name);

                    object? val = null;
                    var t = typeof(float);
                    string? picker = null;

                    switch (prop.Type) {

                        case "sampler2D":
                            val = mat.Data.Textures.GetValueOrDefault(prop.Name, mat == MaterialAsset.Default ? "" : MaterialAsset.Default.Data.Textures.GetValueOrDefault(prop.Name, ""));
                            t = typeof(string);
                            picker = "TextureAsset";

                            break;

                        case "float":
                            val = mat.Data.Floats.GetValueOrDefault(prop.Name, mat == MaterialAsset.Default ? 0f : MaterialAsset.Default.Data.Floats.GetValueOrDefault(prop.Name, 0f));
                            t = typeof(float);

                            break;

                        case "int":
                            val = mat.Data.Ints.GetValueOrDefault(prop.Name, mat == MaterialAsset.Default ? 0 : MaterialAsset.Default.Data.Ints.GetValueOrDefault(prop.Name, 0));
                            t = typeof(int);

                            break;

                        case "vec2":
                            val = mat.Data.Vectors.GetValueOrDefault(prop.Name, mat == MaterialAsset.Default ? Vector2.Zero : MaterialAsset.Default.Data.Vectors.GetValueOrDefault(prop.Name, Vector2.Zero));
                            t = typeof(Vector2);

                            break;

                        case "vec3":
                        case "vec4": {

                            if (prop.Name.Contains("color", StringComparison.OrdinalIgnoreCase) || prop.Name.Contains("albedo", StringComparison.OrdinalIgnoreCase) || prop.Name.Contains("emiss", StringComparison.OrdinalIgnoreCase)) {

                                val = mat.Data.Colors.GetValueOrDefault(prop.Name, mat == MaterialAsset.Default ? Color.White : MaterialAsset.Default.Data.Colors.GetValueOrDefault(prop.Name, Color.White));
                                t = typeof(Color);

                            } else {

                                val = prop.Type == "vec3" ? Vector3.Zero : Vector4.One;
                                t = prop.Type == "vec3" ? typeof(Vector3) : typeof(Vector4);
                            }

                            break;
                        }
                    }

                    var (propChanged, propDeactivated) = DrawInspectorField(prop.Name, ref val, t, [mat], "Data", picker);

                    if (val != null && propChanged) {

                        if (t == typeof(string))
                            mat.Data.Textures[prop.Name] = (string)val;
                        else if (t == typeof(float))
                            mat.Data.Floats[prop.Name] = (float)val;
                        else if (t == typeof(int))
                            mat.Data.Ints[prop.Name] = (int)val;
                        else if (t == typeof(Vector2))
                            mat.Data.Vectors[prop.Name] = (Vector2)val;
                        else if (t == typeof(Color)) mat.Data.Colors[prop.Name] = (Color)val;

                        mat.Save();
                        mat.ApplyChanges();
                    }

                    if (propDeactivated) History.StopRecording();

                    PopID();
                }
            }
        }

        EndSection(open);
        PopID();
    }

    private void DrawScriptAssetInspector(ScriptAsset scriptAsset) {

        PushID(scriptAsset.GetHashCode());
        DrawSectionHeader("Script Asset", Icons.FaCode, Color.White, out var open);

        if (open) {

            DrawInfoRow("Class", Path.GetFileNameWithoutExtension(scriptAsset.File));

            if (scriptAsset.ScriptType == null)
                DrawInfoRow("Status", "Type not loaded");
            else
                DrawScriptFieldRows([scriptAsset], scriptAsset, ScriptFieldStorageKind.Config);
        }

        EndSection(open);
        PopID();
    }

    private void DrawProperties(List<object> targets, bool separator, string title, bool defaultOpen = true) {

        if (targets.Count == 0) return;
        var first = targets[0];
        PushID(first.GetHashCode());

        var open = true;

        if (separator) {

            var icon = first is Component c ? c.LabelIcon : Icons.FaCube;
            var color = first is Component cc ? cc.LabelColor : Colors.GuiTypeModel;
            var isRemovable = first is Component and not Transform && targets.Count == 1;

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
                defaultOpen,
                first as Component
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

            foreach (var prop in first.GetType().GetProperties()) {

                var labelAttr = prop.GetCustomAttribute<LabelAttribute>();

                if (labelAttr == null) continue;

                var id = $"##prop_{_propIndex++}";
                var values = targets.Select(prop.GetValue).ToList();
                var allSame = values.All(v => Equals(v, values[0]));
                var val = allSame ? values[0] : null;

                var fileAttr = prop.GetCustomAttribute<FilePathAttribute>();
                var assetAttr = prop.GetCustomAttribute<FindAssetAttribute>();
                var picker = assetAttr?.TypeName ?? fileAttr?.Category;
                var (highlightOverride, resetValue) = GetPrefabOverrideState(first, prop);
                var applyOverride = GetPrefabApplyAction(first, prop, val);

                DrawShadowedLabel(labelAttr.Value, highlightOverride);

                var (changed, deactivated) = DrawInspectorField(id, ref val, prop.PropertyType, targets, prop.Name, picker, showResetButton: highlightOverride, highlightOverride: highlightOverride, resetValue: resetValue, applyOverride: applyOverride);

                if (changed) {

                    foreach (var t in targets) {

                        prop.SetValue(t, val);
                        SyncAssetReferencePath(t, prop, picker, val as string);
                        ApplyPrefabOverrideMarker(t, prop, val, resetValue);
                        if (t is Component comp && (fileAttr != null || assetAttr != null)) comp.UnloadAndQuit();
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

    private void DrawScriptFieldRows(List<object> targets, ScriptAsset asset, ScriptFieldStorageKind kind) {

        var fields = ScriptFieldUtility.GetFields(asset.ScriptType!, kind);
        if (fields.Length == 0) return;

        foreach (var field in fields) {

            var defaultValue = ScriptFieldUtility.GetCodeDefaultValue(asset.ScriptType, field);

            object? value;
            var picker = field.GetCustomAttribute<FindAssetAttribute>()?.TypeName ?? field.GetCustomAttribute<FilePathAttribute>()?.Category;
            var isOverridden = false;

            if (kind == ScriptFieldStorageKind.Config) {

                var assets = targets.Cast<ScriptAsset>().ToList();
                var values = assets.Select(scriptAsset => scriptAsset.GetConfigFieldValue(field)).ToList();
                value = values.All(val => ScriptFieldUtility.ValueEquals(val, values[0])) ? values[0] : null;
                isOverridden = values.Any(val => !ScriptFieldUtility.ValueEquals(val, defaultValue));
                DrawShadowedLabel(ScriptFieldUtility.GetLabel(field), isOverridden);

                var (changed, deactivated) = DrawInspectorField($"##script_cfg_{_propIndex++}", ref value, field.FieldType, targets, field.Name, picker, showResetButton: true, highlightOverride: isOverridden, resetValue: defaultValue);

                if (changed)
                    foreach (var scriptAsset in assets)
                        scriptAsset.SetConfigFieldValue(field, value);

                if (deactivated) History.StopRecording();
                continue;
            }

            var scripts = targets.Cast<Script>().ToList();
            var exposedValues = scripts.Select(script => script.GetExposeFieldValue(field, asset)).ToList();
            value = exposedValues.All(val => ScriptFieldUtility.ValueEquals(val, exposedValues[0])) ? exposedValues[0] : null;
            var (prefabOverride, prefabResetValue) = GetScriptExposePrefabOverrideState(scripts, asset, field);
            var resetValue = prefabOverride ? prefabResetValue : defaultValue;
            isOverridden = scripts.All(script => script.Obj.FindPrefabRoot() != null)
                ? prefabOverride
                : exposedValues.Any(val => !ScriptFieldUtility.ValueEquals(val, defaultValue));
            var applyOverride = GetScriptExposePrefabApplyAction(scripts, field);
            var applyOverrideWithHistory = GetScriptExposePrefabApplyHistoryAction(scripts, asset, field, picker, value);
            DrawShadowedLabel(ScriptFieldUtility.GetLabel(field), isOverridden);

            var (fieldChanged, fieldDeactivated) = DrawInspectorField($"##script_exp_{_propIndex++}", ref value, field.FieldType, targets, field.Name, picker, showResetButton: isOverridden, highlightOverride: isOverridden, resetValue: resetValue, applyOverride: applyOverride, applyOverrideWithHistory: applyOverrideWithHistory);

            if (fieldChanged)
                foreach (var script in scripts)
                    script.SetExposeFieldValue(field, value);

            if (fieldDeactivated) History.StopRecording();
        }
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

    private static (bool HighlightOverride, object? ResetValue) GetScriptExposePrefabOverrideState(List<Script> scripts, ScriptAsset asset, FieldInfo field) {

        if (scripts.Count == 0) return (false, null);
        if (!scripts.All(script => script.Obj.FindPrefabRoot() != null)) return (false, null);

        var sourceValues = new List<object?>();

        foreach (var script in scripts) {
            if (!PrefabUtility.TryGetSourceScriptFieldValue(script, field, out var sourceValue))
                return (false, null);

            sourceValues.Add(sourceValue);
        }

        var resetValue = sourceValues.All(val => ScriptFieldUtility.ValueEquals(val, sourceValues[0])) ? sourceValues[0] : null;

        for (var index = 0; index < scripts.Count; index++) {
            var currentValue = scripts[index].GetExposeFieldValue(field, asset);
            if (!ScriptFieldUtility.ValueEquals(currentValue, sourceValues[index]))
                return (true, resetValue);
        }

        return (false, resetValue);
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

        return pickerType switch {
            "LevelAsset" => AssetManager.GetPath<LevelAsset>(selectedValue) is { } path ? AssetManager.GetStoredPath(path) : "",
            "PrefabAsset" => AssetManager.GetPath<PrefabAsset>(selectedValue) is { } path ? AssetManager.GetStoredPath(path) : "",
            "ShaderAsset" => AssetManager.GetPath<ShaderAsset>(selectedValue) is { } path ? AssetManager.GetStoredPath(path) : "",
            "TextureAsset" => AssetManager.GetPath<TextureAsset>(selectedValue) is { } path ? AssetManager.GetStoredPath(path) : "",
            "ModelAsset" => AssetManager.GetPath<ModelAsset>(selectedValue) is { } path ? AssetManager.GetStoredPath(path) : "",
            "AnimationAsset" => AssetManager.GetPath<AnimationAsset>(selectedValue) is { } path ? AssetManager.GetStoredPath(path) : "",
            "MaterialAsset" => AssetManager.GetPath<MaterialAsset>(selectedValue) is { } path ? AssetManager.GetStoredPath(path) : "",
            "ScriptAsset" => AssetManager.GetPath<ScriptAsset>(selectedValue) is { } path ? AssetManager.GetStoredPath(path) : "",
            _ => ""
        };
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

    private static Action? GetScriptExposePrefabApplyAction(List<Script> scripts, FieldInfo field) {

        if (scripts.Count != 1) return null;

        var script = scripts[0];
        return PrefabUtility.TryGetSourceScriptFieldValue(script, field, out _)
            ? () => PrefabUtility.ApplyScriptExposeFieldToPrefab(script, field, script.GetAsset() is { } asset ? script.GetExposeFieldValue(field, asset) : null)
            : null;
    }

    private static Action? GetScriptExposePrefabApplyHistoryAction(List<Script> scripts, ScriptAsset asset, FieldInfo field, string? pickerType, object? value) {

        if (scripts.Count != 1) return null;

        var script = scripts[0];
        if (!PrefabUtility.TryGetSourcePrefabFile(script.Obj, out var prefabFile) || !File.Exists(prefabFile)) return null;
        if (!PrefabUtility.TryGetSourceScriptFieldValue(script, field, out _)) return null;

        return () => {
            var beforeLocalValue = CloneApplyHistoryValue(script.GetExposeFieldValue(field, asset));
            using var transaction = History.Begin($"Apply {field.Name} To Prefab");
            transaction.CapturePath(prefabFile);
            transaction.After(
                redo: () => {
                    if (!PrefabUtility.RefreshSourcePrefabFile(prefabFile)) return;
                    script.SetExposeFieldValue(field, value);
                    script.SetPrefabOverride(nameof(Script.ExposedValues), false);
                },
                undo: () => {
                    if (!PrefabUtility.RefreshSourcePrefabFile(prefabFile)) return;
                    script.SetExposeFieldValue(field, beforeLocalValue);
                    script.SetPrefabOverride(nameof(Script.ExposedValues), true);
                }
            );
            PrefabUtility.ApplyScriptExposeFieldToPrefab(script, field, value);
            if (transaction.Commit()) Notifications.Show(transaction.Description);
        };
    }

    private static bool IsPrefabBoundTarget(object target) => target switch {
        Obj obj => obj.FindPrefabRoot() != null,
        Transform transform => transform.Obj.FindPrefabRoot() != null,
        Component component => component.Obj.FindPrefabRoot() != null,
        _ => false
    };

    private void DrawTextureAssetInspector(TextureAsset texture) {

        PushID(texture.GetHashCode());
        DrawSectionHeader("Texture Asset", Icons.FaFileImage, Colors.GuiTypeModel, out var open);

        if (open) {
            var isBusy = AssetManager.IsTextureImportInProgress(texture.GUID);
            PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 8));
            Columns(2, "##texture_asset_props", false);
            SetColumnWidth(0, GetWindowWidth() * 0.32f);

            DrawInfoRow("Source Resolution", $"{texture.SourceWidth} x {texture.SourceHeight}");
            DrawInfoRow("Imported Resolution", $"{texture.ImportedWidth} x {texture.ImportedHeight}");
            DrawInfoRow("Source Size", FormatFileSize(texture.SourceFileSize));
            DrawInfoRow("Imported Size", FormatFileSize(texture.ImportedFileSize));
            DrawInfoRow("Status", isBusy ? "Importing..." : "Ready");

            BeginDisabled(isBusy);

            DrawShadowedLabel("Format");
            var formatOptions = new[] { "Source", "Png", "Jpeg", "WebP", "Avif" };
            var selectedFormat = Array.IndexOf(formatOptions, texture.ImportSettings.Format);
            if (selectedFormat < 0) selectedFormat = 0;
            SetNextItemWidth(GetContentRegionAvail().X);
            if (Combo("##texture_format", ref selectedFormat, formatOptions, formatOptions.Length)) {
                History.StartRecording(texture, nameof(TextureAsset.ImportSettings));
                texture.ImportSettings.Format = formatOptions[selectedFormat];
                texture.SaveMeta();
                AssetManager.ReimportTextureAsync(texture);
                History.StopRecording();
            }
            NextColumn();

            var effectiveFormat = TextureImportProcessor.GetEffectiveFormat(texture.File, texture.ImportSettings);
            var usesResizeFilter = texture.ImportSettings.MaxSize > 0;
            var usesCompression = TextureImportProcessor.UsesCompression(effectiveFormat);
            var usesQuality = TextureImportProcessor.UsesQuality(effectiveFormat);

            var maxSizeOptions = new[] { 0, 32, 64, 128, 256, 512, 1024, 2048, 4096 };
            var maxSizeLabels = new[] { "Original", "32", "64", "128", "256", "512", "1024", "2048", "4096" };
            var selectedMaxSize = Array.IndexOf(maxSizeOptions, texture.ImportSettings.MaxSize);
            if (selectedMaxSize < 0) selectedMaxSize = 0;

            DrawShadowedLabel("Max Size");
            SetNextItemWidth(GetContentRegionAvail().X);
            if (Combo("##texture_max_size", ref selectedMaxSize, maxSizeLabels, maxSizeLabels.Length)) {
                History.StartRecording(texture, nameof(TextureAsset.ImportSettings));
                texture.ImportSettings.MaxSize = maxSizeOptions[selectedMaxSize];
                texture.SaveMeta();
                AssetManager.ReimportTextureAsync(texture);
                History.StopRecording();
            }
            NextColumn();

            DrawShadowedLabel("Resize Filter");
            BeginDisabled(!usesResizeFilter);
            var resizeOptions = new[] { "Nearest", "Bilinear", "Bicubic", "Lanczos" };
            var selectedResize = Array.IndexOf(resizeOptions, texture.ImportSettings.ResizeFilter);
            if (selectedResize < 0) selectedResize = 1;
            SetNextItemWidth(GetContentRegionAvail().X);
            if (Combo("##texture_resize_filter", ref selectedResize, resizeOptions, resizeOptions.Length)) {
                History.StartRecording(texture, nameof(TextureAsset.ImportSettings));
                texture.ImportSettings.ResizeFilter = resizeOptions[selectedResize];
                texture.SaveMeta();
                AssetManager.ReimportTextureAsync(texture);
                History.StopRecording();
            }
            EndDisabled();
            NextColumn();

            DrawShadowedLabel("Compression");
            BeginDisabled(!usesCompression);
            var compressionOptions = new[] { "Fast", "Balanced", "Best" };
            var selectedCompression = Array.IndexOf(compressionOptions, texture.ImportSettings.Compression);
            if (selectedCompression < 0) selectedCompression = 1;
            SetNextItemWidth(GetContentRegionAvail().X);
            if (Combo("##texture_compression", ref selectedCompression, compressionOptions, compressionOptions.Length)) {
                History.StartRecording(texture, nameof(TextureAsset.ImportSettings));
                texture.ImportSettings.Compression = compressionOptions[selectedCompression];
                texture.SaveMeta();
                AssetManager.ReimportTextureAsync(texture);
                History.StopRecording();
            }
            EndDisabled();
            NextColumn();

            DrawShadowedLabel("Quality");
            BeginDisabled(!usesQuality);
            if (!_pendingTextureQuality.TryGetValue(texture.GUID, out var quality))
                quality = texture.ImportSettings.Quality;
            SetNextItemWidth(GetContentRegionAvail().X);
            if (SliderInt("##texture_quality", ref quality, 1, 100))
                _pendingTextureQuality[texture.GUID] = quality;

            if (IsItemActivated()) History.StartRecording(texture, nameof(TextureAsset.ImportSettings));

            if (IsItemDeactivatedAfterEdit()) {

                texture.ImportSettings.Quality = quality;
                _pendingTextureQuality[texture.GUID] = quality;
                texture.SaveMeta();
                AssetManager.ReimportTextureAsync(texture);
                History.StopRecording();
            }
            EndDisabled();
            NextColumn();

            DrawShadowedLabel("Texture Filter");
            var textureFilterOptions = new[] { "Point", "Bilinear", "Trilinear", "Anisotropic 4x", "Anisotropic 8x", "Anisotropic 16x" };
            var selectedTextureFilter = Array.IndexOf(textureFilterOptions, texture.ImportSettings.TextureFilter);
            if (selectedTextureFilter < 0) selectedTextureFilter = 1;
            SetNextItemWidth(GetContentRegionAvail().X);
            if (Combo("##texture_filter", ref selectedTextureFilter, textureFilterOptions, textureFilterOptions.Length)) {
                History.StartRecording(texture, nameof(TextureAsset.ImportSettings));
                texture.ImportSettings.TextureFilter = textureFilterOptions[selectedTextureFilter];
                texture.SaveMeta();
                AssetManager.ApplyTextureFilterAsync(texture);
                History.StopRecording();
            }
            NextColumn();

            EndDisabled();
            Columns(1);
            PopStyleVar();
        }

        EndSection(open);
        PopID();
    }

    private static string GetAssetDisplayValue(string value, string? pickerType) {

        if (string.IsNullOrWhiteSpace(value)) return "";
        if (string.IsNullOrWhiteSpace(pickerType)) return Path.GetFileNameWithoutExtension(value);
        if (SupportsCollectionPicker(pickerType) && CollectionData.TryGetSelectionCollectionInfo(value, pickerType, out var display, out _)) return display;

        return pickerType switch {
            "ShaderAsset" => AssetManager.Get<ShaderAsset>(value) is { } asset ? Path.GetFileNameWithoutExtension(asset.File) : value,
            "LevelAsset" => AssetManager.Get<LevelAsset>(value) is { } levelAsset ? CollectionData.GetLevelDisplayName(levelAsset.File) : value,
            "PrefabAsset" => AssetManager.Get<PrefabAsset>(value) is { } prefabAsset ? CollectionData.GetLevelDisplayName(prefabAsset.File) : value,
            "TextureAsset" => AssetManager.Get<TextureAsset>(value) is { } asset ? Path.GetFileNameWithoutExtension(asset.File) : value,
            "ModelAsset" => AssetManager.Get<ModelAsset>(value) is { } asset ? Path.GetFileNameWithoutExtension(asset.File) : value,
            "AnimationAsset" => AssetManager.Get<AnimationAsset>(value) is { } asset ? Path.GetFileNameWithoutExtension(asset.File) : value,
            "MaterialAsset" => AssetManager.Get<MaterialAsset>(value) is { } asset ? Path.GetFileNameWithoutExtension(asset.File) : value,
            "ScriptAsset" => AssetManager.Get<ScriptAsset>(value) is { } asset ? Path.GetFileNameWithoutExtension(asset.File) : value,
            _ => Path.GetFileNameWithoutExtension(value)
        };
    }

    private static string GetAssetTooltip(string value, string? pickerType) {

        if (string.IsNullOrWhiteSpace(pickerType)) return value;
        if (SupportsCollectionPicker(pickerType) && CollectionData.TryGetSelectionCollectionInfo(value, pickerType, out _, out var tooltip)) return tooltip;

        return pickerType switch {
            "LevelAsset" => AssetManager.GetPath<LevelAsset>(value) ?? value,
            "PrefabAsset" => AssetManager.GetPath<PrefabAsset>(value) ?? value,
            "ShaderAsset" => AssetManager.GetPath<ShaderAsset>(value) ?? value,
            "TextureAsset" => AssetManager.GetPath<TextureAsset>(value) ?? value,
            "ModelAsset" => AssetManager.GetPath<ModelAsset>(value) ?? value,
            "AnimationAsset" => AssetManager.GetPath<AnimationAsset>(value) ?? value,
            "MaterialAsset" => AssetManager.GetPath<MaterialAsset>(value) ?? value,
            "ScriptAsset" => AssetManager.GetPath<ScriptAsset>(value) ?? value,
            _ => value
        };
    }

    private static void DrawInfoRow(string label, string value) {

        DrawShadowedLabel(label);
        TextDisabled(value);
        NextColumn();
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

    private static string FormatFileSize(string path) {

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "-";

        return FormatFileSize(new FileInfo(path).Length);
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

    private static bool SupportsCollectionPicker(string? pickerType) =>
        !string.IsNullOrWhiteSpace(pickerType) && CollectionData.GetKindForPickerType(pickerType) != null;

    private static PickerSearchEntry[] BuildPickerEntries(string pickerType) {

        var entries = new List<PickerSearchEntry>();

        foreach (var collectionPath in CollectionData.EnumerateAllCollections()) {
            if (!CollectionData.TryGetCollectionSelectionValue(collectionPath, pickerType, out var value)) continue;

            var logicalPath = CollectionData.GetLogicalCollectionPath(collectionPath);
            var targetPath = CollectionData.GetResolvedTargetPath(collectionPath);
            entries.Add(new PickerSearchEntry(logicalPath, targetPath == null ? logicalPath : AssetManager.GetStoredPath(targetPath), value));
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

                entries.Add(new PickerSearchEntry(label, storedPath, assetGuid));
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
            entries.Add(new PickerSearchEntry(label, asset.Path.Replace('\\', '/'), asset.GUID));
        }

        return entries
            .DistinctBy(entry => entry.Value)
            .OrderBy(entry => entry.Label, new NaturalStringComparer()!)
            .ToArray();
    }

    private static IEnumerable<(string Name, string Path, string GUID)> GetNamedAssetsForPicker(string pickerType) =>
        AssetManager.GetNames(pickerType);

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

    private static string TrimPickerLabelExtension(string value) {

        if (AssetPaths.IsMaterial(value) || AssetPaths.IsPrefab(value) || AssetPaths.IsLevel(value)) return value[..^4];

        return Path.ChangeExtension(value, null) ?? value;
    }

    private static string ResolvePickerAssetValue(string path, string pickerType) =>
        AssetManager.GetGuidForPickerType(path, pickerType);

    private static string GetPickerTypeForKind(CollectionAssetKind kind) => kind switch {
        CollectionAssetKind.Level => "LevelAsset",
        CollectionAssetKind.Material => "MaterialAsset",
        CollectionAssetKind.Model => "ModelAsset",
        CollectionAssetKind.Prefab => "PrefabAsset",
        CollectionAssetKind.Script => "ScriptAsset",
        CollectionAssetKind.Texture => "TextureAsset",
        _ => ""
    };

    private static Vector4 GetPickerCategoryColor(CollectionAssetKind kind) => kind switch {
        CollectionAssetKind.Level => Colors.GuiCollectionLevel.ToVector4(),
        CollectionAssetKind.Material => Colors.GuiCollectionMaterial.ToVector4(),
        CollectionAssetKind.Model => Colors.GuiCollectionModel.ToVector4(),
        CollectionAssetKind.Prefab => Colors.GuiCollectionPrefab.ToVector4(),
        CollectionAssetKind.Script => Colors.GuiCollectionScript.ToVector4(),
        CollectionAssetKind.Texture => Colors.GuiCollectionTexture.ToVector4(),
        _ => Colors.GuiText.ToVector4()
    };

    private static string GetCategoryIcon(CollectionAssetKind kind) => kind switch {
        CollectionAssetKind.Level => Icons.FaMap,
        CollectionAssetKind.Material => Icons.FaFileImage,
        CollectionAssetKind.Model => Icons.FaCube,
        CollectionAssetKind.Prefab => Icons.FaFile,
        CollectionAssetKind.Script => Icons.FaFileCode,
        CollectionAssetKind.Texture => Icons.FaFileImage,
        _ => Icons.FaArchive
    };

    private static string GetPickerFileIcon(string path) {

        if (CollectionData.IsScript(path)) return Icons.FaFileCode;
        if (CollectionData.IsLevel(path)) return Icons.FaFlag;
        if (CollectionData.IsMaterial(path) || CollectionData.IsTexture(path)) return Icons.FaFileImage;
        if (CollectionData.IsModel(path)) return Icons.FaCube;

        return Icons.FaFile;
    }

    private static void DrawPickerIcon(string icon, Vector4 color, float startX, float iconWidth) {

        var iconSize = CalcTextSize(icon);
        SetCursorPosX(startX + (iconWidth - iconSize.X) * 0.5f);
        PushStyleColor(ImGuiCol.Text, color);
        Text(icon);
        PopStyleColor();
    }

    private static void DrawPickerRightAlignedCount(int count, Vector4 color) {

        var text = count.ToString();
        var textSize = CalcTextSize(text);
        var min = GetItemRectMin();
        var max = GetItemRectMax();
        const float rightPadding = 10f;
        const float countColumnWidth = 24f;
        var columnLeft = max.X - rightPadding - countColumnWidth;
        var pos = new Vector2(columnLeft + (countColumnWidth - textSize.X) * 0.5f, min.Y + (GetItemRectSize().Y - textSize.Y) * 0.5f);
        color.W = 0.72f;

        GetWindowDrawList().AddText(pos, ColorConvertFloat4ToU32(color), text);
    }

    private static bool TryDrawPickerCollectionThumbnail(string collectionPath, float startX, float iconWidth, float thumbnailSize) {

        var targetPath = CollectionData.GetResolvedTargetPath(collectionPath);
        if (string.IsNullOrWhiteSpace(targetPath)) return false;

        return TryDrawPickerThumbnail(targetPath, startX, iconWidth, thumbnailSize);
    }

    private static bool TryDrawPickerFileThumbnail(string path, float startX, float iconWidth, float thumbnailSize) =>
        TryDrawPickerThumbnail(path, startX, iconWidth, thumbnailSize);

    private static bool TryDrawPickerThumbnail(string path, float startX, float iconWidth, float thumbnailSize) {

        var tex = GetPickerThumbnail(path);
        if (!tex.HasValue || tex.Value.Id == 0) return false;

        var texture = tex.Value;
        var ratio = texture.Width / (float)texture.Height;
        var drawW = thumbnailSize;
        var drawH = thumbnailSize;

        if (texture.Width > texture.Height)
            drawH = drawW / ratio;
        else
            drawW = drawH * ratio;

        SetCursorPosX(startX + (iconWidth - drawW) * 0.5f);
        Image((IntPtr)texture.Id, new Vector2(drawW, drawH));
        return true;
    }

    private static Texture2D? GetPickerThumbnail(string path) {

        if (CollectionData.IsTexture(path)) return AssetManager.GetOrImport<TextureAsset>(path)?.Thumbnail;
        if (CollectionData.IsMaterial(path)) return AssetManager.GetOrImport<MaterialAsset>(path)?.Thumbnail;
        if (CollectionData.IsModel(path)) return AssetManager.GetOrImport<ModelAsset>(path)?.Thumbnail;

        return null;
    }

    private sealed class PickerBrowserState {
        public string CurrentPath { get; set; } = CollectionData.RootPath;
        public bool ShowChildCollections { get; set; }
        public CollectionAssetKind? ActiveCategory { get; set; }
        public Stack<PickerNavigationState> NavigationStack { get; } = [];
    }

    private readonly record struct PickerNavigationState(string Path, CollectionAssetKind? ActiveCategory, bool ShowChildCollections);
    private readonly record struct PickerSearchEntry(string Label, string Tooltip, string Value);
}
