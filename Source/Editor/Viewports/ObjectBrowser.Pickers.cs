using System.Numerics;
using System.Reflection;
using EnumsNET;
using ImGuiNET;
using Raylib_cs;
using static ImGuiNET.ImGui;

internal partial class ObjectBrowser {

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

    private void DrawScenePickerPopup(Type targetType, ref object? value, List<object> targets, string? propName, ref bool changed, ref bool deactivated) {

        if (Core.ActiveLevel == null) {
            TextDisabled("No active level.");
            return;
        }

        BeginChild("##scene_picker", new Vector2(0, 400));

        if (!string.IsNullOrWhiteSpace(_searchFilter)) {
            foreach (var entry in EnumerateScenePickerEntries(Core.ActiveLevel.Root, targetType)
                         .Where(entry => entry.Label.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase)
                                         || entry.Tooltip.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))) {
                if (Selectable($"{entry.Label}##{entry.Id}")) {
                    ApplyPickerValue(SceneReferenceValue.FromTarget(entry.Target), ref value, targets, propName, ref changed, ref deactivated);
                    CloseCurrentPopup();
                }

                if (string.IsNullOrWhiteSpace(entry.Tooltip) || string.Equals(entry.Tooltip, entry.Label, StringComparison.OrdinalIgnoreCase)) continue;

                SameLine();
                TextDisabled(entry.Tooltip);
            }

            EndChild();
            return;
        }

        TextDisabled("Level");
        Indent();
        DrawScenePickerObjectNode(Core.ActiveLevel.Root, targetType, ref value, targets, propName, ref changed, ref deactivated);
        Unindent();
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

    private static void ApplyPickerValue(object? selectedValue, ref object? value, List<object> targets, string? propName, ref bool changed, ref bool deactivated) {

        if (propName != null) targets.ForEach(t => History.StartRecording(t, propName));

        value = selectedValue;
        changed = true;
        deactivated = true;
    }

    private void DrawScenePickerObjectNode(Obj obj, Type targetType, ref object? value, List<object> targets, string? propName, ref bool changed, ref bool deactivated) {

        foreach (var child in obj.ChildEntries.Values.Where(child => SceneObjectHasPickerContent(child, targetType))) {
            var childHasComponents = GetSceneComponentTargets(child, targetType).Any();
            var childHasObjects = child.ChildEntries.Values.Any(grandChild => SceneObjectHasPickerContent(grandChild, targetType));
            var flags = (childHasComponents || childHasObjects ? ImGuiTreeNodeFlags.None : ImGuiTreeNodeFlags.Leaf)
                        | ImGuiTreeNodeFlags.SpanFullWidth;
            var open = TreeNodeEx($"{child.Name}##tree_{GetSceneReferenceId(child)}", flags);

            if (IsItemClicked() && targetType == typeof(Obj)) {
                ApplyPickerValue(SceneReferenceValue.FromTarget(child), ref value, targets, propName, ref changed, ref deactivated);
                CloseCurrentPopup();
                if (open && (flags & ImGuiTreeNodeFlags.Leaf) == 0)
                    TreePop();
                return;
            }

            if (!open || (flags & ImGuiTreeNodeFlags.Leaf) != 0) continue;

            foreach (var componentTarget in GetSceneComponentTargets(child, targetType)) {
                if (!Selectable($"{GetSceneTargetLabel(componentTarget)}##cmp_pick_{GetSceneReferenceId(componentTarget)}", false, ImGuiSelectableFlags.None, new Vector2(GetContentRegionAvail().X, 0f))) continue;

                ApplyPickerValue(SceneReferenceValue.FromTarget(componentTarget), ref value, targets, propName, ref changed, ref deactivated);
                CloseCurrentPopup();
                TreePop();
                return;
            }

            DrawScenePickerObjectNode(child, targetType, ref value, targets, propName, ref changed, ref deactivated);
            TreePop();
        }
    }

    private static bool SceneObjectHasPickerContent(Obj obj, Type targetType) =>
        targetType == typeof(Obj)
        || GetSceneComponentTargets(obj, targetType).Any()
        || obj.ChildEntries.Values.Any(child => SceneObjectHasPickerContent(child, targetType));

    private static IEnumerable<object> GetSceneComponentTargets(Obj obj, Type targetType) {

        foreach (var component in obj.ComponentEntries.Values) {
            if (targetType.IsInstanceOfType(component)) {
                yield return component;
                continue;
            }

            if (component is not Script script) continue;
            var scriptType = script.Instance?.GetType() ?? script.GetAsset()?.ScriptType;

            if (scriptType != null && typeof(ScytheScript).IsAssignableFrom(targetType) && targetType.IsAssignableFrom(scriptType))
                yield return script;
        }
    }

    private static IEnumerable<(string Id, string Label, string Tooltip, object Target)> EnumerateScenePickerEntries(Obj root, Type targetType) {

        foreach (var child in root.ChildEntries.Values)
            foreach (var entry in EnumerateScenePickerEntriesRecursive(child, targetType))
                yield return entry;
    }

    private static IEnumerable<(string Id, string Label, string Tooltip, object Target)> EnumerateScenePickerEntriesRecursive(Obj obj, Type targetType) {

        if (targetType == typeof(Obj)) {
            var path = string.Join("/", obj.GetPathFromRoot());
            yield return (GetSceneReferenceId(obj), obj.Name, path, obj);
        }

        foreach (var componentTarget in GetSceneComponentTargets(obj, targetType)) {
            var label = GetSceneTargetLabel(componentTarget);
            var tooltip = GetSceneTargetTooltip(componentTarget);
            yield return (GetSceneReferenceId(componentTarget), label, tooltip, componentTarget);
        }

        foreach (var child in obj.ChildEntries.Values)
            foreach (var entry in EnumerateScenePickerEntriesRecursive(child, targetType))
                yield return entry;
    }

    private static string GetSceneReferenceId(object target) => target switch {
        Obj obj => string.Join("/", obj.GetPathFromRoot()),
        ScytheScript script => $"{string.Join("/", script.Obj.GetPathFromRoot())}/{script.GetType().FullName}",
        Script script => $"{string.Join("/", script.Obj.GetPathFromRoot())}/Script:{script.Obj.ComponentEntries.GetOccurrenceIndex(script)}:{script.GetAsset()?.ScriptType?.FullName}",
        Component component => $"{string.Join("/", component.Obj.GetPathFromRoot())}/{component.GetType().FullName}:{component.Obj.ComponentEntries.GetOccurrenceIndex(component)}",
        SceneReferenceValue reference => $"{string.Join("/", reference.Path.Select(segment => segment.Name))}/{reference.ComponentType}:{reference.ComponentOccurrence}:{reference.ScriptType}",
        _ => target.GetHashCode().ToString()
    };

    private static string GetSceneTargetLabel(object target) => target switch {
        ScytheScript script => script.GetType().Name,
        Script script => script.GetAsset()?.ScriptType?.Name ?? "Script",
        Component component => component.GetType().Name,
        SceneReferenceValue reference when !string.IsNullOrWhiteSpace(reference.ScriptType) => GetSceneScriptTypeName(reference.ScriptType),
        SceneReferenceValue reference => reference.ComponentType ?? "Component",
        _ => "Target"
    };

    private static string GetSceneTargetTooltip(object target) => target switch {
        ScytheScript script => $"{string.Join("/", script.Obj.GetPathFromRoot())}/{script.GetType().Name}",
        Script script => $"{string.Join("/", script.Obj.GetPathFromRoot())}/{GetSceneTargetLabel(script)}",
        Component component => $"{string.Join("/", component.Obj.GetPathFromRoot())}/{component.GetType().Name}",
        SceneReferenceValue reference => $"{string.Join("/", reference.Path.Select(segment => segment.Name))}/{GetSceneTargetLabel(reference)}",
        _ => ""
    };

    private static string GetSceneScriptTypeName(string? typeName) {

        if (string.IsNullOrWhiteSpace(typeName)) return "Script";

        var lastDot = typeName.LastIndexOf('.');
        return lastDot >= 0 && lastDot < typeName.Length - 1 ? typeName[(lastDot + 1)..] : typeName;
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

    private static string TrimPickerLabelExtension(string value) {

        if (AssetPaths.IsMaterial(value) || AssetPaths.IsPrefab(value) || AssetPaths.IsLevel(value)) return value[..^4];

        return Path.ChangeExtension(value, null) ?? value;
    }

    private static bool TryGetPickerTypeMetadata(string? pickerType, out PickerTypeMetadata metadata) {

        if (!string.IsNullOrWhiteSpace(pickerType) && _pickerTypeMetadata.TryGetValue(pickerType, out metadata))
            return true;

        metadata = default!;
        return false;
    }

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
}
