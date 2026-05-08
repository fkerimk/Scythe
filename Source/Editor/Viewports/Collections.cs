using System.Numerics;
using ImGuiNET;
using Newtonsoft.Json;
using Raylib_cs;
using static ImGuiNET.ImGui;

namespace Viewports;

internal class Collections : Viewport {

    private readonly string _collectionsRoot;
    private const string ChildCollectionsLabel = "Collections";
    private const string AddPopupId = "Add Item";

    private string _currentPath;
    private string? _selectedPath;
    private bool _showChildCollections;
    private CollectionCategory? _activeCategory;

    private string _newItemName = "";
    private bool _showAddPopup;
    private bool _showAddTypePopup;
    private bool _showRenamePopup;
    private bool _openDeletePopup;
    private CreateItemType _createItemType = CreateItemType.Collection;
    private string? _renameTargetPath;
    private string _renameName = "";
    private string _renameSuffix = "";
    private string? _deleteTargetPath;
    private bool _deleteTargetIsDirectory;
    private Vector2 _deletePopupPosition;

    private string RelativePath {
        get {
            var relative = Path.GetRelativePath(_collectionsRoot, _currentPath);
            if (relative == ".") relative = "";

            if (_showChildCollections)
                return string.IsNullOrEmpty(relative) ? ChildCollectionsLabel : $"{relative.Replace('\\', '/')}/{ChildCollectionsLabel}";

            if (_activeCategory == null) return relative;
            return string.IsNullOrEmpty(relative) ? _activeCategory.Value.Name : $"{relative.Replace('\\', '/')}/{_activeCategory.Value.Name}";
        }
    }

    private bool IsAtCollectionsRoot =>
        Path.GetFullPath(_currentPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Equals(Path.GetFullPath(_collectionsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private static readonly CollectionCategory[] Categories = [
        new("Levels", IsLevel, Icons.FaMap),
        new("Materials", IsMaterial, Icons.FaFileImage),
        new("Models", IsModel, Icons.FaCube),
        new("Prefabs", IsPrefab, Icons.FaFile),
        new("Scripts", IsScript, Icons.FaFileCode),
        new("Textures", IsTexture, Icons.FaFileImage)
    ];

    public Collections() : base("Collections") {

        _collectionsRoot = Path.Combine(ScytheConfig.Current.Project, "Collections");
        _currentPath = _collectionsRoot;

        if (!Directory.Exists(_collectionsRoot)) Directory.CreateDirectory(_collectionsRoot);
    }

    public void SyncExternalSelection(string? path) {

        _selectedPath = path;

        if (string.IsNullOrEmpty(path)) return;
        if (!IsUnderCollectionsRoot(path)) return;

        _showChildCollections = false;
        _activeCategory = Categories.FirstOrDefault(category => category.Match(path));

        var parent = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(parent)) return;

        if (Directory.Exists(parent)) _currentPath = parent;
    }

    protected override void OnDraw() {

        Validate();
        DrawToolbar();
        Separator();
        DrawBrowser();
        DrawPopups();
    }

    private void Validate() {

        if (!Directory.Exists(_collectionsRoot)) Directory.CreateDirectory(_collectionsRoot);
        if (!IsAtCollectionsRoot) EnsureCollectionSettings(_currentPath);
    }

    private void DrawToolbar() {

        PushFont(Fonts.ImFontAwesomeNormal);

        if (Button(Icons.FaPlus)) {

            _showAddTypePopup = true;
        }

        PopFont();

        if (IsItemHovered()) SetTooltip("Add");

        if (_showAddTypePopup) OpenPopup("Add Menu");

        if (BeginPopup("Add Menu")) {

            if (MenuItem("Collection")) OpenCreatePopup(CreateItemType.Collection);

            BeginDisabled(IsAtCollectionsRoot);

            if (MenuItem("Level")) OpenCreatePopup(CreateItemType.Level);
            if (MenuItem("Material")) OpenCreatePopup(CreateItemType.Material);
            if (MenuItem("Script")) OpenCreatePopup(CreateItemType.Script);
            if (MenuItem("Prefab")) OpenCreatePopup(CreateItemType.Prefab);

            EndDisabled();
            _showAddTypePopup = false;
            EndPopup();
        }

        SameLine();

        var isRoot = Path.GetFullPath(_currentPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Equals(Path.GetFullPath(_collectionsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

        BeginDisabled(isRoot && _activeCategory == null);

        PushFont(Fonts.ImFontAwesomeNormal);

        if (Button(Icons.FaLevelUp)) {

            if (_activeCategory != null) {

                _activeCategory = null;
                _showChildCollections = false;

            } else if (_showChildCollections) {

                _showChildCollections = false;

            } else {

                var parent = Directory.GetParent(_currentPath);
                if (parent != null && IsUnderCollectionsRoot(parent.FullName)) _currentPath = parent.FullName;
            }
        }

        PopFont();

        EndDisabled();

        if (IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) SetTooltip("Up");

        if (string.IsNullOrEmpty(RelativePath)) return;

        SameLine();
        TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), RelativePath);
    }

    private void DrawBrowser() {

        if (!BeginChild("Browser")) return;

        if (_showChildCollections) {

            foreach (var collection in GetCollectionEntries(CollectionEntryKind.Collection)) DrawCollectionEntry(collection);

        } else if (_activeCategory == null) {

            foreach (var entry in GetBrowserEntries()) DrawBrowserEntry(entry);

        } else {

            var activeCategory = _activeCategory.Value;
            foreach (var collection in GetCollectionEntriesForCategory(activeCategory)) DrawCollectionEntry(collection);
            foreach (var file in GetFilesForCategory(activeCategory)) DrawFileEntry(file);
        }

        if (IsWindowHovered() && IsMouseReleased(ImGuiMouseButton.Left) && !IsAnyItemHovered()) {

            Editor.SetSelectedAsset(null);
            LevelBrowser.SelectObject(null);
        }

        EndChild();
    }

    private IEnumerable<BrowserEntry> GetBrowserEntries() {

        var collections = GetCollectionEntries(CollectionEntryKind.Collection);

        if (IsAtCollectionsRoot) return collections.Select(BrowserEntry.CreateCollection).OrderBy(entry => entry.Name, new NaturalStringComparer()!);

        var collectionEntry = BrowserEntry.CreateCollectionGroup(GetCollectionEntries(CollectionEntryKind.Collection).Count());
        var categories = GetCategoryStates().Select(BrowserEntry.CreateCategory);

        return new[] { collectionEntry }
            .Concat(categories)
            .OrderByDescending(entry => entry.IsActive)
            .ThenBy(entry => entry.Name, new NaturalStringComparer()!);
    }

    private IEnumerable<string> GetCollectionEntries(CollectionEntryKind kind) =>
        Directory.EnumerateDirectories(_currentPath)
            .Select(path => {
                EnsureCollectionSettings(path);
                return path;
            })
            .Where(path => GetCollectionEntryKind(path) == kind)
            .Where(path => !IsCategoryFolderName(Path.GetFileName(path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(Path.GetFileName, new NaturalStringComparer()!);

    private IEnumerable<string> GetCollectionEntriesForCategory(CollectionCategory category) =>
        GetCollectionEntries(GetCollectionEntryKind(category));

    private IEnumerable<CategoryState> GetCategoryStates() =>
        Categories
            .Select(category => new CategoryState(category, CountFilesForCategory(category)))
            .OrderBy(state => state.Category.Name, new NaturalStringComparer()!);

    private int CountFilesForCategory(CollectionCategory category) =>
        GetFilesForCategory(category).Count() + GetCollectionEntriesForCategory(category).Count();

    private IEnumerable<string> GetFilesForCategory(CollectionCategory category) {

        return Directory.EnumerateFiles(_currentPath)
            .Where(path => !IsSidecarMetaFile(path))
            .Where(category.Match)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetNameWithoutExtension, new NaturalStringComparer()!);
    }

    private void DrawBrowserEntry(BrowserEntry entry) {

        if (entry.Kind == BrowserEntryKind.Collection) {

            DrawCollectionEntry(entry.EntryPath!);
            return;
        }

        if (entry.Kind == BrowserEntryKind.CollectionGroup) {

            DrawCollectionGroupEntry(entry.Count);
            return;
        }

        DrawCategoryEntry(entry.CategoryState!.Value);
    }

    private void DrawCollectionEntry(string path) {

        var name = Path.GetFileName(path);
        const float iconWidth = 20f;
        const float thumbnailSize = 16f;
        var startX = GetCursorPosX();
        var color = GetCollectionColor();

        PushFont(Fonts.ImFontAwesomeNormal);

        if (!TryDrawCollectionThumbnail(path, startX, iconWidth, thumbnailSize))
            DrawIcon(Icons.FaFolder, color, startX, iconWidth);

        PopFont();

        SameLine(startX + iconWidth + 5f);
        PushStyleColor(ImGuiCol.Text, color);
        var clicked = Selectable(name, false, ImGuiSelectableFlags.None, new Vector2(GetContentRegionAvail().X, 0f));
        PopStyleColor();
        DrawEntryContextMenu(path, isDirectory: true);

        if (!clicked) return;

        _currentPath = path;
        _showChildCollections = false;
        _activeCategory = null;
        Editor.SetSelectedAsset(null);
    }

    private void DrawCollectionGroupEntry(int count) {

        const float iconWidth = 20f;
        var startX = GetCursorPosX();

        PushFont(Fonts.ImFontAwesomeNormal);

        var color = count > 0
            ? GetCollectionColor()
            : new Vector4(0.45f, 0.45f, 0.45f, 1f);

        DrawIcon(Icons.FaFolder, color, startX, iconWidth);
        PopFont();

        SameLine(startX + iconWidth + 5f);

        if (count == 0) BeginDisabled();

        PushStyleColor(ImGuiCol.Text, color);
        var clicked = Selectable(ChildCollectionsLabel, _showChildCollections, ImGuiSelectableFlags.None, new Vector2(GetContentRegionAvail().X, 0f));
        PopStyleColor();
        DrawRightAlignedCount(count, color);

        if (count == 0) EndDisabled();
        if (!clicked) return;

        _showChildCollections = true;
        _activeCategory = null;
        Editor.SetSelectedAsset(null);
    }

    private void DrawCategoryEntry(CategoryState state) {

        const float iconWidth = 20f;
        var startX = GetCursorPosX();

        PushFont(Fonts.ImFontAwesomeNormal);

        var color = state.Count > 0
            ? GetCategoryColor(state.Category)
            : new Vector4(0.45f, 0.45f, 0.45f, 1f);

        DrawIcon(state.Category.Icon, color, startX, iconWidth);
        PopFont();

        SameLine(startX + iconWidth + 5f);

        if (state.Count == 0) BeginDisabled();

        var isSelected = _activeCategory?.Name == state.Category.Name;
        if (isSelected) {
            PushStyleColor(ImGuiCol.Header, Colors.GuiButtonActive.ToVector4());
            PushStyleColor(ImGuiCol.HeaderHovered, Colors.GuiButtonActive.ToVector4());
            PushStyleColor(ImGuiCol.HeaderActive, Colors.GuiButtonActive.ToVector4());
        }

        PushStyleColor(ImGuiCol.Text, color);
        var clicked = Selectable(state.Category.Name, isSelected, ImGuiSelectableFlags.None, new Vector2(GetContentRegionAvail().X, 0f));
        PopStyleColor();
        DrawRightAlignedCount(state.Count, color);

        if (isSelected) PopStyleColor(3);
        if (state.Count == 0) EndDisabled();

        if (!clicked) return;

        _showChildCollections = false;
        _activeCategory = state.Category;
        Editor.SetSelectedAsset(null);
    }

    private void DrawFileEntry(string path) {

        var name = GetNameWithoutExtension(path);
        var startX = GetCursorPosX();

        const float iconWidth = 20f;
        const float thumbnailSize = 16f;

        PushFont(Fonts.ImFontAwesomeNormal);

        if (!TryDrawThumbnail(path, startX, iconWidth, thumbnailSize))
            DrawFileIcon(path, startX, iconWidth, GetFileColor(path));

        PopFont();

        SameLine(startX + iconWidth + 5f);
        var isSelected = string.Equals(_selectedPath, path, StringComparison.OrdinalIgnoreCase);
        var color = GetFileColor(path);

        if (isSelected) {
            PushStyleColor(ImGuiCol.Header, Colors.GuiButtonActive.ToVector4());
            PushStyleColor(ImGuiCol.HeaderHovered, Colors.GuiButtonActive.ToVector4());
            PushStyleColor(ImGuiCol.HeaderActive, Colors.GuiButtonActive.ToVector4());
        }

        PushStyleColor(ImGuiCol.Text, color);
        var clicked = Selectable(name, isSelected, ImGuiSelectableFlags.None, new Vector2(GetContentRegionAvail().X, 0f));
        PopStyleColor();
        DrawEntryContextMenu(path, isDirectory: false);

        if (isSelected) PopStyleColor(3);
        if (!clicked) return;

        LevelBrowser.SelectObject(null);
        Editor.SetSelectedAsset(path);
    }

    private bool TryDrawThumbnail(string path, float startX, float iconWidth, float thumbnailSize) {

        var thumbTex = GetThumbnail(path);

        if (!thumbTex.HasValue || thumbTex.Value.Id == 0) return false;

        var tex = thumbTex.Value;
        float w = tex.Width;
        float h = tex.Height;

        var ratio = w / h;
        var drawW = thumbnailSize;
        var drawH = thumbnailSize;

        if (w > h)
            drawH = drawW / ratio;
        else
            drawW = drawH * ratio;

        SetCursorPosX(startX + (iconWidth - drawW) * 0.5f);
        Image((IntPtr)tex.Id, new Vector2(drawW, drawH));

        return true;
    }

    private void DrawFileIcon(string path, float startX, float iconWidth, Vector4 color) {

        var icon = GetFileIcon(path);
        var iconSize = CalcTextSize(icon);
        SetCursorPosX(startX + (iconWidth - iconSize.X) * 0.5f);
        PushStyleColor(ImGuiCol.Text, color);
        Text(icon);
        PopStyleColor();
    }

    private void DrawIcon(string icon, Vector4 color, float startX, float iconWidth) {

        var iconSize = CalcTextSize(icon);
        SetCursorPosX(startX + (iconWidth - iconSize.X) * 0.5f);
        PushStyleColor(ImGuiCol.Text, color);
        Text(icon);
        PopStyleColor();
    }

    private void DrawRightAlignedCount(int count, Vector4 color) {

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

    private void DrawPopups() {

        var addPopupInstanceId = $"{AddPopupId}###{_createItemType}";

        if (_showAddPopup) OpenPopup(addPopupInstanceId);

        if (Modal.Begin(addPopupInstanceId, ref _showAddPopup)) {

            Text($"Enter {GetCreateItemLabel(_createItemType).ToLowerInvariant()} name:");

            if (IsWindowAppearing()) SetKeyboardFocusHere();

            if (InputText("##name", ref _newItemName, 64, ImGuiInputTextFlags.EnterReturnsTrue)) {

                if (CreateItem(_createItemType, _newItemName)) {
                    _showAddPopup = false;
                    CloseCurrentPopup();
                }
            }

            Spacing();
            Separator();
            Spacing();

            if (Button("Create", new Vector2(120, 0))) {

                if (CreateItem(_createItemType, _newItemName)) {
                    _showAddPopup = false;
                    CloseCurrentPopup();
                }
            }

            SameLine();

            if (Button("Cancel", new Vector2(120, 0))) {

                _showAddPopup = false;
                CloseCurrentPopup();
            }

            Modal.End();
        }

        DrawRenamePopup();
        DrawDeletePopup();
    }

    private void DrawRenamePopup() {

        if (_showRenamePopup) OpenPopup("Rename Item");

        if (!Modal.Begin("Rename Item", ref _showRenamePopup)) return;

        Text("Enter new name:");

        if (IsWindowAppearing()) SetKeyboardFocusHere();

        if (InputText("##rename", ref _renameName, 128, ImGuiInputTextFlags.EnterReturnsTrue)) {

            ApplyRename();
            _showRenamePopup = false;
            CloseCurrentPopup();
        }

        if (!string.IsNullOrEmpty(_renameSuffix))
            TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), $"Extension: {_renameSuffix}");

        Spacing();
        Separator();
        Spacing();

        if (Button("Rename", new Vector2(120, 0))) {

            ApplyRename();
            _showRenamePopup = false;
            CloseCurrentPopup();
        }

        SameLine();

        if (Button("Cancel", new Vector2(120, 0))) {

            _showRenamePopup = false;
            CloseCurrentPopup();
        }

        Modal.End();
    }

    private void DrawDeletePopup() {

        if (_openDeletePopup) {
            OpenPopup("Delete Confirm");
            _openDeletePopup = false;
        }

        if (!Modal.BeginPopup("Delete Confirm", _deletePopupPosition)) return;

        Text("Are you sure?");
        TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), _deleteTargetIsDirectory ? "This collection will be deleted permanently." : "This file will be deleted permanently.");

        Spacing();

        if (Button("Delete", new Vector2(120, 0))) {

            DeleteTarget();
            CloseCurrentPopup();
        }

        SameLine();

        if (Button("Cancel", new Vector2(120, 0))) CloseCurrentPopup();

        Modal.End();
    }

    private void DrawEntryContextMenu(string path, bool isDirectory) {

        if (!BeginPopupContextItem($"Context::{path}")) return;

        if (MenuItem("Rename")) OpenRenamePopup(path, isDirectory);
        if (isDirectory && BeginMenu("Set As")) {
            if (MenuItem("Collection")) SetCollectionType(path, CollectionEntryKind.Collection);
            foreach (var category in Categories) {
                if (MenuItem(category.Name[..^1])) SetCollectionType(path, GetCollectionEntryKind(category));
            }
            EndMenu();
        }
        if (!isDirectory && HasCollectionPreviewCandidate(path) && MenuItem("Set as Collection Preview")) SetCollectionPreview(path);
        if (MenuItem("Delete")) OpenDeletePopup(path, isDirectory);

        EndPopup();
    }

    private void OpenRenamePopup(string path, bool isDirectory) {

        _renameTargetPath = path;
        _renameSuffix = isDirectory ? "" : GetRenameSuffix(path);
        _renameName = isDirectory ? Path.GetFileName(path) : GetNameWithoutExtension(path);
        _showRenamePopup = true;
    }

    private void OpenCreatePopup(CreateItemType type) {

        _createItemType = type;
        _newItemName = "";
        _showAddPopup = true;
        _showAddTypePopup = false;
    }

    private void OpenDeletePopup(string path, bool isDirectory) {

        _deleteTargetPath = path;
        _deleteTargetIsDirectory = isDirectory;
        _deletePopupPosition = GetMousePos() + new Vector2(0f, 4f);
        _openDeletePopup = true;
    }

    private void SetCollectionPreview(string path) {

        if (IsAtCollectionsRoot) {
            Notifications.Show("Set preview failed: Open a collection first.");
            return;
        }

        try {
            EnsureCollectionSettings(_currentPath);
            var settingsPath = GetCollectionSettingsPath(_currentPath);
            var settings = ReadCollectionSettings(_currentPath);
            settings.PreviewPath = Path.GetRelativePath(_currentPath, path).Replace('\\', '/');
            File.WriteAllText(settingsPath, JsonConvert.SerializeObject(settings, Formatting.Indented));
            Notifications.Show($"Collection preview set to '{Path.GetFileName(path)}'.");
        } catch (Exception e) {
            Notifications.Show($"Set preview failed: {e.Message}");
        }
    }

    private void SetCollectionType(string path, CollectionEntryKind kind) {

        try {
            EnsureCollectionSettings(path);
            var settingsPath = GetCollectionSettingsPath(path);
            var settings = ReadCollectionSettings(path);
            settings.Type = GetCollectionTypeName(kind);
            File.WriteAllText(settingsPath, JsonConvert.SerializeObject(settings, Formatting.Indented));
            Notifications.Show($"Collection type set to '{GetCollectionTypeName(kind)}'.");
        } catch (Exception e) {
            Notifications.Show($"Set collection type failed: {e.Message}");
        }
    }

    private void ApplyRename() {

        if (string.IsNullOrWhiteSpace(_renameTargetPath) || string.IsNullOrWhiteSpace(_renameName)) return;

        var sourcePath = _renameTargetPath;
        var parentDir = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrEmpty(parentDir)) return;

        var newPath = Path.Combine(parentDir, _renameName.Trim() + _renameSuffix);
        if (string.Equals(sourcePath, newPath, StringComparison.OrdinalIgnoreCase)) return;

        if (Directory.Exists(sourcePath)) {

            if (File.Exists(newPath) || Directory.Exists(newPath)) {
                Notifications.Show($"Rename failed: '{Path.GetFileName(newPath)}' already exists.");
                return;
            }

            try {
                Directory.Move(sourcePath, newPath);
                Notifications.Show($"Collection renamed to '{Path.GetFileName(newPath)}'.");
            } catch (Exception e) {
                Notifications.Show($"Rename failed: {e.Message}");
            }

        } else if (File.Exists(sourcePath)) {

            var sidecarPath = GetSidecarMetaPathFor(sourcePath);
            var newSidecarPath = GetSidecarMetaPathFor(newPath);
            var hasSidecar = File.Exists(sidecarPath);

            if (File.Exists(newPath) || Directory.Exists(newPath) || hasSidecar && File.Exists(newSidecarPath)) {
                Notifications.Show($"Rename failed: '{Path.GetFileName(newPath)}' already exists.");
                return;
            }

            var movedMain = false;
            var movedSidecar = false;

            try {
                File.Move(sourcePath, newPath);
                movedMain = true;

                if (hasSidecar) {
                    File.Move(sidecarPath, newSidecarPath);
                    movedSidecar = true;
                }

                if (string.Equals(_selectedPath, sourcePath, StringComparison.OrdinalIgnoreCase)) Editor.SetSelectedAsset(newPath);
                Notifications.Show($"File renamed to '{Path.GetFileName(newPath)}'.");
            } catch (Exception e) {
                try {
                    if (movedSidecar && File.Exists(newSidecarPath) && !File.Exists(sidecarPath)) File.Move(newSidecarPath, sidecarPath);
                    if (movedMain && File.Exists(newPath) && !File.Exists(sourcePath)) File.Move(newPath, sourcePath);
                } catch {
                    // Best-effort rollback only.
                }

                Notifications.Show($"Rename failed: {e.Message}");
            }
        }
    }

    private void DeleteTarget() {

        if (string.IsNullOrWhiteSpace(_deleteTargetPath)) return;

        var targetPath = _deleteTargetPath;

        if (_deleteTargetIsDirectory) {

            if (Directory.Exists(targetPath)) Directory.Delete(targetPath, true);
            Notifications.Show($"Collection '{Path.GetFileName(targetPath)}' deleted.");

        } else {

            var sidecarPath = GetSidecarMetaPath(targetPath);

            if (File.Exists(targetPath)) File.Delete(targetPath);
            if (sidecarPath != null && File.Exists(sidecarPath)) File.Delete(sidecarPath);

            if (string.Equals(_selectedPath, targetPath, StringComparison.OrdinalIgnoreCase)) Editor.SetSelectedAsset(null);
            Notifications.Show($"File '{Path.GetFileName(targetPath)}' deleted.");
        }
    }

    private bool CreateItem(CreateItemType type, string name) {

        if (string.IsNullOrWhiteSpace(name)) return false;

        var trimmedName = name.Trim();
        var path = Path.Combine(_currentPath, trimmedName + GetCreateItemSuffix(type));

        if (Directory.Exists(path) || File.Exists(path)) {
            Notifications.Show($"{GetCreateItemLabel(type)} creation failed: '{Path.GetFileName(path)}' already exists.");
            return false;
        }

        try {
            switch (type) {
                case CreateItemType.Collection:
                    Directory.CreateDirectory(path);
                    EnsureCollectionSettings(path);
                    Notifications.Show($"Collection '{trimmedName}' created.");
                    return true;
                case CreateItemType.Level:
                    File.WriteAllText(path, $$"""
                                             {
                                               "Root": {
                                                 "Name": "{{trimmedName}}",
                                                 "Children": {}
                                               }
                                             }
                                             """);
                    Notifications.Show($"Level '{Path.GetFileName(path)}' created.");
                    return true;
                case CreateItemType.Material:
                    File.WriteAllText(path, JsonConvert.SerializeObject(new MaterialAsset.MaterialData { GUID = Guid.NewGuid().ToString("N") }, Formatting.Indented));
                    Notifications.Show($"Material '{Path.GetFileName(path)}' created.");
                    return true;
                case CreateItemType.Script:
                    File.WriteAllText(path, BuildScriptTemplate(trimmedName));
                    Notifications.Show($"Script '{Path.GetFileName(path)}' created.");
                    return true;
                case CreateItemType.Prefab:
                    File.WriteAllText(path, "{}");
                    Notifications.Show($"Prefab '{Path.GetFileName(path)}' created.");
                    return true;
                default:
                    return false;
            }
        } catch (Exception e) {
            Notifications.Show($"{GetCreateItemLabel(type)} creation failed: {e.Message}");
            return false;
        }
    }

    private bool IsUnderCollectionsRoot(string path) {

        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootPath = Path.GetFullPath(_collectionsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return fullPath.Equals(rootPath, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(rootPath + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCategoryFolderName(string? name) =>
        !string.IsNullOrEmpty(name) && Categories.Any(category => string.Equals(category.Name, name, StringComparison.OrdinalIgnoreCase));

    private void EnsureCollectionSettings(string collectionPath) {

        if (!Directory.Exists(collectionPath)) return;
        if (string.Equals(Path.GetFullPath(collectionPath), Path.GetFullPath(_collectionsRoot), StringComparison.OrdinalIgnoreCase)) return;

        var settingsPath = GetCollectionSettingsPath(collectionPath);
        if (File.Exists(settingsPath)) return;

        File.WriteAllText(settingsPath, JsonConvert.SerializeObject(new CollectionSettings(), Formatting.Indented));
    }

    private static string GetCollectionSettingsPath(string collectionPath) => Path.Combine(collectionPath, "Collection.json");

    private static CollectionSettings ReadCollectionSettings(string collectionPath) {

        var settingsPath = GetCollectionSettingsPath(collectionPath);
        if (!File.Exists(settingsPath)) return new CollectionSettings();

        return JsonConvert.DeserializeObject<CollectionSettings>(File.ReadAllText(settingsPath)) ?? new CollectionSettings();
    }

    private CollectionEntryKind GetCollectionEntryKind(string collectionPath) {

        if (IsAtCollectionsRoot && string.Equals(Path.GetFullPath(collectionPath), Path.GetFullPath(_collectionsRoot), StringComparison.OrdinalIgnoreCase))
            return CollectionEntryKind.Collection;

        var settings = ReadCollectionSettings(collectionPath);
        return ParseCollectionEntryKind(settings.Type);
    }

    private static CollectionEntryKind GetCollectionEntryKind(CollectionCategory category) => category.Name switch {
        "Levels" => CollectionEntryKind.Level,
        "Materials" => CollectionEntryKind.Material,
        "Models" => CollectionEntryKind.Model,
        "Prefabs" => CollectionEntryKind.Prefab,
        "Scripts" => CollectionEntryKind.Script,
        "Textures" => CollectionEntryKind.Texture,
        _ => CollectionEntryKind.Collection
    };

    private static CollectionEntryKind ParseCollectionEntryKind(string? type) => type switch {
        "Levels" => CollectionEntryKind.Level,
        "Materials" => CollectionEntryKind.Material,
        "Models" => CollectionEntryKind.Model,
        "Prefabs" => CollectionEntryKind.Prefab,
        "Scripts" => CollectionEntryKind.Script,
        "Textures" => CollectionEntryKind.Texture,
        _ => CollectionEntryKind.Collection
    };

    private static string GetCollectionTypeName(CollectionEntryKind kind) => kind switch {
        CollectionEntryKind.Level => "Levels",
        CollectionEntryKind.Material => "Materials",
        CollectionEntryKind.Model => "Models",
        CollectionEntryKind.Prefab => "Prefabs",
        CollectionEntryKind.Script => "Scripts",
        CollectionEntryKind.Texture => "Textures",
        _ => "Collection"
    };

    private static bool IsSidecarMetaFile(string path) {

        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;

        var assetPath = path[..^5];
        return File.Exists(assetPath);
    }

    private static string? GetSidecarMetaPath(string path) => Directory.Exists(path) ? null : File.Exists(GetSidecarMetaPathFor(path)) ? GetSidecarMetaPathFor(path) : null;

    private static string GetSidecarMetaPathFor(string path) => path + ".json";

    private static string GetFileIcon(string path) {

        if (IsScript(path)) return Icons.FaFileCode;
        if (IsLevel(path)) return Icons.FaFlag;
        if (IsMaterial(path)) return Icons.FaFileImage;
        if (IsTexture(path)) return Icons.FaFileImage;
        if (IsPrefab(path)) return Icons.FaFile;
        if (IsModel(path)) return Icons.FaCube;

        return Icons.FaFile;
    }

    private static Vector4 GetFileColor(string path) {

        if (IsLevel(path)) return new Vector4(0.93f, 0.59f, 0.37f, 1f);
        if (IsMaterial(path)) return new Vector4(0.95f, 0.77f, 0.33f, 1f);
        if (IsTexture(path)) return new Vector4(0.47f, 0.88f, 0.62f, 1f);
        if (IsScript(path)) return new Vector4(0.47f, 0.78f, 0.95f, 1f);
        if (IsPrefab(path)) return new Vector4(0.82f, 0.58f, 0.93f, 1f);
        if (IsModel(path)) return new Vector4(0.52f, 0.86f, 0.96f, 1f);

        return new Vector4(0.8f, 0.8f, 0.8f, 1f);
    }

    private static Vector4 GetCollectionColor() => new(1f, 0.8f, 0.2f, 1f);

    private static Vector4 GetCategoryColor(CollectionCategory category) => category.Name switch {
        "Levels" => new Vector4(0.93f, 0.59f, 0.37f, 1f),
        "Materials" => new Vector4(0.95f, 0.77f, 0.33f, 1f),
        "Textures" => new Vector4(0.47f, 0.88f, 0.62f, 1f),
        "Scripts" => new Vector4(0.47f, 0.78f, 0.95f, 1f),
        "Prefabs" => new Vector4(0.82f, 0.58f, 0.93f, 1f),
        "Models" => new Vector4(0.52f, 0.86f, 0.96f, 1f),
        _ => new Vector4(0.75f, 0.9f, 1f, 1f)
    };

    private static bool IsLevel(string path) => path.EndsWith(".level.json", StringComparison.OrdinalIgnoreCase);
    private static bool IsMaterial(string path) => path.EndsWith(".material.json", StringComparison.OrdinalIgnoreCase);
    private static bool IsTexture(string path) => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase);
    private static bool IsScript(string path) => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
    private static bool IsPrefab(string path) => path.EndsWith(".prefab.json", StringComparison.OrdinalIgnoreCase);

    private static bool IsModel(string path) {

        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".fbx" or ".obj" or ".gltf" or ".glb" or ".iqm";
    }

    private static string GetNameWithoutExtension(string path) {

        var name = Path.GetFileName(path);

        if (IsLevel(path)) return name[..^11];
        if (IsMaterial(path)) return name[..^14];
        if (IsPrefab(path)) return name[..^12];

        return Path.GetFileNameWithoutExtension(name);
    }

    private static string GetRenameSuffix(string path) {

        var name = Path.GetFileName(path);

        if (IsLevel(path)) return name[^11..];
        if (IsMaterial(path)) return name[^14..];
        if (IsPrefab(path)) return name[^12..];

        return Path.GetExtension(path);
    }

    private static string GetCreateItemLabel(CreateItemType type) => type switch {
        CreateItemType.Collection => "Collection",
        CreateItemType.Level => "Level",
        CreateItemType.Material => "Material",
        CreateItemType.Script => "Script",
        CreateItemType.Prefab => "Prefab",
        _ => "Item"
    };

    private static string GetCreateItemSuffix(CreateItemType type) => type switch {
        CreateItemType.Collection => "",
        CreateItemType.Level => ".level.json",
        CreateItemType.Material => ".material.json",
        CreateItemType.Script => ".cs",
        CreateItemType.Prefab => ".prefab.json",
        _ => ""
    };

    private static string BuildScriptTemplate(string name) {

        var className = ToIdentifier(name);

        return $$"""
                 internal class {{className}} : ScytheScript {

                     public override void Start() {
                     }

                     public override void Loop(float dt) {
                     }
                 }
                 """;
    }

    private static string ToIdentifier(string value) {

        Span<char> buffer = stackalloc char[value.Length == 0 ? 1 : value.Length + 1];
        var count = 0;

        foreach (var c in value) {
            if (!char.IsLetterOrDigit(c) && c != '_') continue;

            if (count == 0 && char.IsDigit(c)) buffer[count++] = '_';
            buffer[count++] = c;
        }

        if (count == 0) return "NewScript";
        return new string(buffer[..count]);
    }

    private bool TryDrawCollectionThumbnail(string collectionPath, float startX, float iconWidth, float thumbnailSize) {

        var thumbTex = GetCollectionThumbnail(collectionPath);

        if (!thumbTex.HasValue || thumbTex.Value.Id == 0) return false;

        var tex = thumbTex.Value;
        float w = tex.Width;
        float h = tex.Height;

        var ratio = w / h;
        var drawW = thumbnailSize;
        var drawH = thumbnailSize;

        if (w > h)
            drawH = drawW / ratio;
        else
            drawW = drawH * ratio;

        SetCursorPosX(startX + (iconWidth - drawW) * 0.5f);
        Image((IntPtr)tex.Id, new Vector2(drawW, drawH));

        return true;
    }

    private Texture2D? GetCollectionThumbnail(string collectionPath) {

        EnsureCollectionSettings(collectionPath);

        var settings = ReadCollectionSettings(collectionPath);
        if (string.IsNullOrWhiteSpace(settings.PreviewPath)) return null;

        var previewPath = Path.GetFullPath(Path.Combine(collectionPath, settings.PreviewPath));
        if (!File.Exists(previewPath)) return null;

        return GetThumbnail(previewPath);
    }

    private static Texture2D? GetThumbnail(string path) {

        if (IsTexture(path)) {

            var textureAsset = AssetManager.GetOrImport<TextureAsset>(path);
            if (textureAsset is { Thumbnail: not null }) return textureAsset.Thumbnail.Value;

            return null;
        }

        if (IsMaterial(path)) return AssetManager.GetOrImport<MaterialAsset>(path)?.Thumbnail;
        if (IsModel(path)) return AssetManager.GetOrImport<ModelAsset>(path)?.Thumbnail;

        return null;
    }

    private static bool HasCollectionPreviewCandidate(string path) => IsTexture(path) || IsMaterial(path) || IsModel(path);

    private readonly record struct CollectionCategory(string Name, Func<string, bool> Match, string Icon);
    private readonly record struct CategoryState(CollectionCategory Category, int Count);
    private readonly record struct BrowserEntry(string Name, BrowserEntryKind Kind, bool IsActive, int Count, string? EntryPath, CategoryState? CategoryState) {

        public static BrowserEntry CreateCollection(string path) => new(System.IO.Path.GetFileName(path), BrowserEntryKind.Collection, true, 0, path, null);
        public static BrowserEntry CreateCollectionGroup(int count) => new(ChildCollectionsLabel, BrowserEntryKind.CollectionGroup, count > 0, count, null, null);
        public static BrowserEntry CreateCategory(CategoryState state) => new(state.Category.Name, BrowserEntryKind.Category, state.Count > 0, state.Count, null, state);
    }

    private enum BrowserEntryKind {
        Collection,
        CollectionGroup,
        Category
    }

    private enum CreateItemType {
        Collection,
        Level,
        Material,
        Script,
        Prefab
    }

    private sealed class CollectionSettings {
        public string PreviewPath { get; set; } = "";
        public string Type { get; set; } = "Collection";
    }

    private enum CollectionEntryKind {
        Collection,
        Level,
        Material,
        Model,
        Prefab,
        Script,
        Texture
    }
}
