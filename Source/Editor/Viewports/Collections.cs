using System.Numerics;
using ImGuiNET;
using Newtonsoft.Json;
using Raylib_cs;
using static ImGuiNET.ImGui;

namespace Viewports;

internal class Collections : Viewport {
    private readonly string _collectionsRoot;
    private const string ChildCollectionsLabel = "Collections";
    private const string ProjectLabel = "Project";
    private const string BuiltInLabel = CollectionData.BuiltInCollectionLabel;
    private const string AddPopupId = "Add Item";
    private const string CollectionEntryDragDropType = "collection_entry";

    private string _currentPath;
    private string? _selectedPath;
    private bool _showChildCollections;
    private CollectionCategory? _activeCategory;
    private readonly Stack<NavigationState> _navigationStack = [];

    private string _newItemName = "";
    private bool _showAddPopup;
    private bool _showAddTypePopup;
    private bool _openDeletePopup;
    private CreateItemType _createItemType = CreateItemType.Collection;
    private string? _renamingPath;
    private string _renameName = "";
    private string _renameSuffix = "";
    private bool _requestRenameFocus;
    private string? _deleteTargetPath;
    private bool _deleteTargetIsDirectory;
    private Vector2 _deletePopupPosition;
    private bool _entryClickedThisFrame;
    private bool _hideEmptyCategories = true;
    private string? _draggedEntryPath;
    private bool _draggedEntryIsDirectory;

    private string RelativePath {
        get {
            if (CollectionData.IsBuiltInRoot(_currentPath)) {
                if (_showChildCollections)
                    return $"{BuiltInLabel}/{ChildCollectionsLabel}";

                if (_activeCategory == null) return BuiltInLabel;
                return $"{BuiltInLabel}/{_activeCategory.Value.Name}";
            }

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
        new("Fonts", CollectionData.IsFont, Icons.FaFile),
        new("Levels", CollectionData.IsLevel, Icons.FaMap),
        new("Materials", CollectionData.IsMaterial, Icons.FaFileImage),
        new("Models", CollectionData.IsModel, Icons.FaCube),
        new("Prefabs", CollectionData.IsPrefab, Icons.FaFile),
        new("Scripts", CollectionData.IsScript, Icons.FaFileCode),
        new("Shaders", CollectionData.IsShader, Icons.FaCode),
        new("Textures", CollectionData.IsTexture, Icons.FaFileImage)
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
        _navigationStack.Clear();

        var parent = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(parent)) return;

        if (Directory.Exists(parent)) _currentPath = parent;
    }

    public bool CanDeleteSelectedAsset =>
        !string.IsNullOrWhiteSpace(_selectedPath) && (File.Exists(_selectedPath) || Directory.Exists(_selectedPath));

    public void DeleteSelectedAsset() {

        if (!CanDeleteSelectedAsset || string.IsNullOrWhiteSpace(_selectedPath)) return;

        OpenDeletePopup(_selectedPath, Directory.Exists(_selectedPath), centerOnViewport: true);
    }

    protected override void OnDraw() {

        Validate();
        if (IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && IsKeyPressed(ImGuiKey.F2)) StartRenameSelected();
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

        var isRoot = IsAtCollectionsRoot;

        BeginDisabled(isRoot && _activeCategory == null);

        PushFont(Fonts.ImFontAwesomeNormal);

        if (Button(Icons.FaLevelUp)) {

            if (_activeCategory != null) {

                _activeCategory = null;
                _showChildCollections = false;

            } else if (_showChildCollections) {

                _showChildCollections = false;

            } else if (_navigationStack.Count > 0) {

                var state = _navigationStack.Pop();
                _currentPath = state.Path;
                _activeCategory = state.Category;
                _showChildCollections = state.ShowChildCollections;

            } else if (CollectionData.IsBuiltInRoot(_currentPath)) {

                _currentPath = _collectionsRoot;

            } else {

                var parent = Directory.GetParent(_currentPath);
                if (parent != null && IsUnderCollectionsRoot(parent.FullName)) _currentPath = parent.FullName;
            }
        }

        PopFont();

        EndDisabled();

        HandleUpDropTarget();

        if (IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) SetTooltip("Up");

        SameLine();
        PushFont(Fonts.ImFontAwesomeNormal);

        if (Button(_hideEmptyCategories ? Icons.FaEyeSlash : Icons.FaEye))
            _hideEmptyCategories = !_hideEmptyCategories;

        PopFont();

        if (IsItemHovered())
            SetTooltip(_hideEmptyCategories ? "Show empty types" : "Hide empty types");

        if (string.IsNullOrEmpty(RelativePath)) return;

        SameLine();
        TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), RelativePath);
    }

    private void DrawBrowser() {
        _entryClickedThisFrame = false;

        PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 4));
        if (!BeginChild("Browser", new Vector2(0, 0), ImGuiChildFlags.None, ImGuiWindowFlags.None)) {
            PopStyleVar();
            return;
        }

        Dummy(new Vector2(0, 2));

        if (_showChildCollections) {

            foreach (var collection in GetCollectionEntries(CollectionEntryKind.Collection)) DrawCollectionEntry(collection);

        } else if (_activeCategory == null) {

            foreach (var entry in GetBrowserEntries()) DrawBrowserEntry(entry);

        } else {

            var activeCategory = _activeCategory.Value;
            foreach (var collection in GetCollectionEntriesForCategory(activeCategory)) DrawCollectionEntry(collection);
            foreach (var file in GetFilesForCategory(activeCategory)) DrawFileEntry(file);
        }

        if (IsWindowHovered() && IsMouseReleased(ImGuiMouseButton.Left) && !IsAnyItemHovered() && !_entryClickedThisFrame) {

            Editor.SetSelectedAsset(null);
            LevelBrowser.SelectObject(null);
        }

        if (IsMouseReleased(ImGuiMouseButton.Left)) {
            _draggedEntryPath = null;
            _draggedEntryIsDirectory = false;
        }

        EndChild();
        PopStyleVar();
    }

    private IEnumerable<BrowserEntry> GetBrowserEntries() {

        var collections = GetCollectionEntries(CollectionEntryKind.Collection);
        var categories = GetCategoryStates()
            .Where(state => !_hideEmptyCategories || state.Count > 0)
            .Select(BrowserEntry.CreateCategory);

        if (IsAtCollectionsRoot)
            return new[] { BrowserEntry.CreateProject() }
                .Concat(Directory.Exists(CollectionData.BuiltInRootPath) ? new[] { BrowserEntry.CreateCollection(CollectionData.BuiltInRootPath) } : [])
                .Concat(collections.Select(BrowserEntry.CreateCollection))
                .Concat(categories);

        var collectionCount = GetCollectionEntries(CollectionEntryKind.Collection).Count();
        var collectionEntries = (_hideEmptyCategories && collectionCount == 0 ? [] : new[] { BrowserEntry.CreateCollectionGroup(collectionCount) });

        return collectionEntries
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
        GetCollectionEntryKind(category) == CollectionEntryKind.Collection && (category.Name is "Fonts" or "Shaders")
            ? []
            : GetCollectionEntries(GetCollectionEntryKind(category));

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

        if (entry.Kind == BrowserEntryKind.Project) {

            DrawProjectEntry();
            return;
        }

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

    private void DrawProjectEntry() {

        const float iconWidth = 20f;
        var startX = GetCursorPosX();
        var color = Colors.GuiText.ToVector4();
        var isSelected = Editor.ProjectSettingsSelected;

        PushFont(Fonts.ImFontAwesomeNormal);
        DrawIcon(Icons.FaHouse, color, startX, iconWidth);
        PopFont();

        SameLine(startX + iconWidth + 5f);

        PushStyleColor(ImGuiCol.Text, color);
        var clicked = Selectable(ProjectLabel, isSelected, ImGuiSelectableFlags.None, new Vector2(GetContentRegionAvail().X - 6f, 0f));
        PopStyleColor();

        if (isSelected) DrawSelectionHighlight();
        if (!clicked) return;

        _entryClickedThisFrame = true;
        Editor.SelectProjectSettings();
    }

    private void DrawCollectionEntry(string path) {

        var name = CollectionData.GetCollectionDisplayName(path);
        const float iconWidth = 20f;
        const float thumbnailSize = 16f;
        var startX = GetCursorPosX();
        var isBuiltIn = CollectionData.IsBuiltInRoot(path);
        var color = isBuiltIn ? Vector4.One : GetCollectionColor();

        PushFont(Fonts.ImFontAwesomeNormal);

        if (!TryDrawCollectionThumbnail(path, startX, iconWidth, thumbnailSize))
            DrawIcon(Icons.FaArchive, color, startX, iconWidth);

        PopFont();

        SameLine(startX + iconWidth + 5f);
        var isSelected = string.Equals(_selectedPath, path, StringComparison.OrdinalIgnoreCase);
        var clicked = DrawRenamableEntry(path, name, color, isSelected);
        DrawEntryDragSource(path, name, isDirectory: true);
        var dropped = HandleCollectionDropTarget(path);

        if (!isBuiltIn) DrawEntryContextMenu(path, isDirectory: true);

        if (dropped) return;
        if (!clicked) return;

        _entryClickedThisFrame = true;
        _navigationStack.Push(new NavigationState(_currentPath, _activeCategory, _showChildCollections));
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
            : Colors.GuiCollectionMuted.ToVector4();

        DrawIcon(Icons.FaArchive, color, startX, iconWidth);
        PopFont();

        SameLine(startX + iconWidth + 5f);

        if (count == 0) BeginDisabled();

        PushStyleColor(ImGuiCol.Text, color);
        var clicked = Selectable(ChildCollectionsLabel, _showChildCollections, ImGuiSelectableFlags.None, new Vector2(GetContentRegionAvail().X - 6f, 0f));
        PopStyleColor();
        DrawRightAlignedCount(count, color);

        if (count == 0) EndDisabled();
        if (!clicked) return;

        _entryClickedThisFrame = true;
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
            : Colors.GuiCollectionMuted.ToVector4();

        DrawIcon(state.Category.Icon, color, startX, iconWidth);
        PopFont();

        SameLine(startX + iconWidth + 5f);

        if (state.Count == 0) BeginDisabled();

        var isSelected = _activeCategory?.Name == state.Category.Name;
        PushStyleColor(ImGuiCol.Text, color);
        var clicked = Selectable(state.Category.Name, isSelected, ImGuiSelectableFlags.None, new Vector2(GetContentRegionAvail().X - 6f, 0f));
        PopStyleColor();

        DrawRightAlignedCount(state.Count, color);

        if (isSelected) DrawSelectionHighlight();
        if (state.Count == 0) EndDisabled();

        if (!clicked) return;

        _entryClickedThisFrame = true;
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
        var clicked = DrawRenamableEntry(path, name, color, isSelected);
        DrawEntryDragSource(path, name, isDirectory: false);
        DrawEntryContextMenu(path, isDirectory: false);
        var doubleClicked = IsItemHovered() && IsMouseDoubleClicked(ImGuiMouseButton.Left);

        if (doubleClicked && (CollectionData.IsLevel(path) || CollectionData.IsPrefab(path))) {
            _entryClickedThisFrame = true;
            Editor.OpenLevel(path);
            return;
        }
        if (!clicked) return;

        _entryClickedThisFrame = true;
        LevelBrowser.SelectObject(null);
        Editor.SetSelectedAsset(path);
    }

    public void StartRenameSelected() {

        if (string.IsNullOrWhiteSpace(_selectedPath) || (!File.Exists(_selectedPath) && !Directory.Exists(_selectedPath))) return;

        StartRename(_selectedPath, Directory.Exists(_selectedPath));
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
                TryCreatePopupItem();
            }

            Spacing();
            Separator();
            Spacing();

            if (Button("Create", new Vector2(120, 0))) {
                TryCreatePopupItem();
            }

            SameLine();

            if (Button("Cancel", new Vector2(120, 0))) {

                _showAddPopup = false;
                CloseCurrentPopup();
            }

            Modal.End();
        }

        DrawDeletePopup();
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

        var isBuiltInRoot = CollectionData.IsBuiltInRoot(path);

        if (!isBuiltInRoot && MenuItem("Rename")) StartRename(path, isDirectory);
        if (!isBuiltInRoot && isDirectory && !IsAtCollectionsRoot && BeginMenu("Set As")) {
            if (MenuItem("Collection")) SetCollectionType(path, CollectionEntryKind.Collection);
            foreach (var category in Categories) {
                if (MenuItem(category.Name[..^1])) SetCollectionType(path, GetCollectionEntryKind(category));
            }
            EndMenu();
        }
        if (!isBuiltInRoot && !isDirectory && CollectionPathMenu.DrawProjectDirectoryMenu("Move To", destination => MoveAssetTo(path, destination), Path.GetDirectoryName(path))) { }
        if (!isBuiltInRoot && !isDirectory && !IsAtCollectionsRoot && HasCollectionTargetCandidate(path) && MenuItem("Set as Target")) SetCollectionTarget(path);
        if (!isBuiltInRoot && MenuItem("Delete")) OpenDeletePopup(path, isDirectory);

        EndPopup();
    }

    private void StartRename(string path, bool isDirectory) {

        _renamingPath = path;
        _renameSuffix = isDirectory ? "" : GetRenameSuffix(path);
        _renameName = isDirectory ? Path.GetFileName(path) : GetNameWithoutExtension(path);
        _requestRenameFocus = true;
    }

    private bool DrawRenamableEntry(string path, string displayName, Vector4 color, bool isSelected) {

        if (string.Equals(_renamingPath, path, StringComparison.OrdinalIgnoreCase)) {
            DrawRenameInput(path);
            return false;
        }

        PushStyleColor(ImGuiCol.Text, color);
        var clicked = Selectable($"{displayName}##{path}", isSelected, ImGuiSelectableFlags.None, new Vector2(GetContentRegionAvail().X - 6f, 0f));
        PopStyleColor();

        if (isSelected) DrawSelectionHighlight();

        return clicked;
    }

    private static void DrawSelectionHighlight() {
        var drawList = GetWindowDrawList();
        var min = GetItemRectMin();
        var max = GetItemRectMax();

        // Contract slightly to avoid edge clipping
        min.X += 1f;
        max.X -= 1f;

        var primaryColor = Colors.Primary.ToVector4();
        var bgColor = primaryColor;
        bgColor.W = 0.08f;

        drawList.AddRectFilled(min, max, ColorConvertFloat4ToU32(bgColor), 4f);
        drawList.AddRect(min, max, ColorConvertFloat4ToU32(primaryColor), 4f, ImDrawFlags.None, 1.5f);
    }

    private void DrawRenameInput(string path) {

        SetNextItemWidth(GetContentRegionAvail().X);

        if (_requestRenameFocus) {
            SetKeyboardFocusHere();
            _requestRenameFocus = false;
        }

        var submitted = InputText($"##rename_{path}", ref _renameName, 128, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
        if (submitted) ApplyRename();
        if (IsItemActive() && IsKeyPressed(ImGuiKey.Escape)) _renamingPath = null;
        if (IsItemDeactivated() && !submitted) _renamingPath = null;
    }

    private void DrawEntryDragSource(string path, string displayName, bool isDirectory) {

        if (!CanDragEntry(path)) return;
        if (!BeginDragDropSource()) return;

        _draggedEntryPath = path;
        _draggedEntryIsDirectory = isDirectory;
        DragDropPayload.Data = path;

        SetDragDropPayload(CollectionEntryDragDropType, IntPtr.Zero, 0);
        Text($"Move {displayName}");
        EndDragDropSource();
    }

    private bool HandleCollectionDropTarget(string destinationCollectionPath) {

        if (!CanAcceptEntryDrop(destinationCollectionPath)) return false;
        if (!BeginDragDropTarget()) return false;

        AcceptDragDropPayload(CollectionEntryDragDropType);

        var dropped = false;

        if (CanMoveDraggedEntryTo(destinationCollectionPath)) {
            GetWindowDrawList().AddRect(GetItemRectMin(), GetItemRectMax(), GetColorU32(Colors.Primary.ToVector4()), 4f, ImDrawFlags.None, 1.5f);

            if (IsMouseReleased(ImGuiMouseButton.Left))
                dropped = MoveDraggedEntryTo(destinationCollectionPath);
        }

        EndDragDropTarget();
        return dropped;
    }

    private void HandleUpDropTarget() {

        var destinationDirectory = GetUpMoveDestinationDirectory();
        if (string.IsNullOrWhiteSpace(destinationDirectory)) return;
        if (!BeginDragDropTarget()) return;

        AcceptDragDropPayload(CollectionEntryDragDropType);

        if (CanMoveDraggedEntryTo(destinationDirectory)) {
            GetWindowDrawList().AddRect(GetItemRectMin(), GetItemRectMax(), GetColorU32(Colors.Primary.ToVector4()), 4f, ImDrawFlags.None, 1.5f);

            if (IsMouseReleased(ImGuiMouseButton.Left))
                MoveDraggedEntryTo(destinationDirectory);
        }

        EndDragDropTarget();
    }

    private void OpenCreatePopup(CreateItemType type) {

        _createItemType = type;
        _newItemName = "";
        _showAddPopup = true;
        _showAddTypePopup = false;
    }

    private void OpenDeletePopup(string path, bool isDirectory, bool centerOnViewport = false) {

        _deleteTargetPath = path;
        _deleteTargetIsDirectory = isDirectory;
        _deletePopupPosition = centerOnViewport
            ? GetMainViewport().GetCenter()
            : GetMousePos() + new Vector2(0f, 4f);
        _openDeletePopup = true;
    }

    private void TryCreatePopupItem() {

        if (!CreateItem(_createItemType, _newItemName)) return;

        _showAddPopup = false;
        CloseCurrentPopup();
    }

    private void SetCollectionTarget(string path) {

        if (IsAtCollectionsRoot) {
            Notifications.Show("Set target failed: Open a collection first.");
            return;
        }

        try {
            EnsureCollectionSettings(_currentPath);
            var settingsPath = GetCollectionSettingsPath(_currentPath);
            var settings = ReadCollectionSettings(_currentPath);
            settings.TargetPath = Path.GetRelativePath(_currentPath, path).Replace('\\', '/');
            JsonFile.WriteIndented(settingsPath, settings);
            Notifications.Show($"Collection target set to '{Path.GetFileName(path)}'.");
        } catch (Exception e) {
            Notifications.Show($"Set target failed: {e.Message}");
        }
    }

    private void SetCollectionType(string path, CollectionEntryKind kind) {

        try {
            EnsureCollectionSettings(path);
            var settingsPath = GetCollectionSettingsPath(path);
            var settings = ReadCollectionSettings(path);
            settings.Type = GetCollectionTypeName(kind);
            JsonFile.WriteIndented(settingsPath, settings);
            Notifications.Show($"Collection type set to '{GetCollectionTypeName(kind)}'.");
        } catch (Exception e) {
            Notifications.Show($"Set collection type failed: {e.Message}");
        }
    }

    private void ApplyRename() {

        if (string.IsNullOrWhiteSpace(_renamingPath) || string.IsNullOrWhiteSpace(_renameName)) return;

        var sourcePath = _renamingPath;
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
                using var transaction = History.Begin($"Rename {Path.GetFileName(sourcePath)}");
                transaction.CapturePath(sourcePath);
                transaction.CapturePath(newPath);
                transaction.After(
                    redo: () => OnDirectoryPathMoved(sourcePath, newPath),
                    undo: () => OnDirectoryPathMoved(newPath, sourcePath)
                );
                MoveDirectoryFileSystem(sourcePath, newPath);
                OnDirectoryPathMoved(sourcePath, newPath);
                if (transaction.Commit()) Notifications.Show(transaction.Description);
            } catch (Exception e) {
                Notifications.Show($"Rename failed: {e.Message}");
            }

        } else if (File.Exists(sourcePath)) {

            var sidecarPath = GetSidecarMetaPathFor(sourcePath);
            var newSidecarPath = GetSidecarMetaPathFor(newPath);
            var hasSidecar = !CollectionData.IsLevel(sourcePath) && File.Exists(sidecarPath);

            if (File.Exists(newPath) || Directory.Exists(newPath) || hasSidecar && File.Exists(newSidecarPath)) {
                Notifications.Show($"Rename failed: '{Path.GetFileName(newPath)}' already exists.");
                return;
            }

            try {
                using var transaction = History.Begin($"Rename {Path.GetFileName(sourcePath)}");
                transaction.CapturePath(sourcePath);
                transaction.CapturePath(newPath);
                if (hasSidecar) {
                    transaction.CapturePath(sidecarPath);
                    transaction.CapturePath(newSidecarPath);
                }

                transaction.After(
                    redo: () => OnFilePathMoved(sourcePath, newPath),
                    undo: () => OnFilePathMoved(newPath, sourcePath)
                );
                MoveFileSystem(sourcePath, newPath, hasSidecar);
                OnFilePathMoved(sourcePath, newPath);
                if (transaction.Commit()) Notifications.Show(transaction.Description);
            } catch (Exception e) {
                Notifications.Show($"Rename failed: {e.Message}");
            }
        }

        _renamingPath = null;
        _requestRenameFocus = false;
    }

    private static void MoveDirectoryFileSystem(string sourcePath, string targetPath) {

        Directory.Move(sourcePath, targetPath);
    }

    private static void OnDirectoryPathMoved(string sourcePath, string targetPath) {

        Editor.OnCollectionPathMoved(sourcePath, targetPath);
        SyncSelectionAfterPathMove(sourcePath, targetPath, isDirectory: true);
    }

    private static void MoveFileSystem(string sourcePath, string targetPath, bool hasSidecar) {

        File.Move(sourcePath, targetPath);

        if (hasSidecar) {
            var sourceSidecarPath = GetSidecarMetaPathFor(sourcePath);
            var targetSidecarPath = GetSidecarMetaPathFor(targetPath);

            if (File.Exists(sourceSidecarPath))
                File.Move(sourceSidecarPath, targetSidecarPath);
        }
    }

    private static void OnFilePathMoved(string sourcePath, string targetPath) {

        if (CollectionData.IsLevel(sourcePath) || CollectionData.IsPrefab(sourcePath))
            Editor.OnDocumentPathMoved(sourcePath, targetPath);

        SyncSelectionAfterPathMove(sourcePath, targetPath, isDirectory: false);
    }

    private static void SyncSelectionAfterPathMove(string sourcePath, string targetPath, bool isDirectory) {

        var selectedPath = Editor.SelectedAssetPath;
        if (string.IsNullOrWhiteSpace(selectedPath)) return;

        if (isDirectory) {
            var remappedPath = RemapNestedPath(selectedPath, sourcePath, targetPath);
            if (remappedPath != null) Editor.SetSelectedAsset(remappedPath);
            return;
        }

        if (string.Equals(selectedPath, sourcePath, StringComparison.OrdinalIgnoreCase))
            Editor.SetSelectedAsset(targetPath);
    }

    private static string? RemapNestedPath(string path, string oldRoot, string newRoot) {

        var fullPath = Path.GetFullPath(path);
        var fullOldRoot = Path.GetFullPath(oldRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(fullPath, fullOldRoot, StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(newRoot);

        var prefix = fullOldRoot + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var suffix = fullPath[prefix.Length..];
        return Path.Combine(Path.GetFullPath(newRoot), suffix);
    }

    private void DeleteTarget() {

        if (string.IsNullOrWhiteSpace(_deleteTargetPath)) return;

        var targetPath = _deleteTargetPath;
        var selectedBeforeDelete = _selectedPath;

        if (_deleteTargetIsDirectory) {
            History.RecordPathChange(
                $"Delete {Path.GetFileName(targetPath)}",
                () => {
                    if (Directory.Exists(targetPath)) Directory.Delete(targetPath, recursive: true);
                },
                targetPath
            );

            if (string.Equals(selectedBeforeDelete, targetPath, StringComparison.OrdinalIgnoreCase))
                Editor.SetSelectedAsset(null);

            Notifications.Show($"Collection '{Path.GetFileName(targetPath)}' deleted.");

        } else {

            var sidecarPath = GetSidecarMetaPath(targetPath);
            History.RecordPathChange(
                $"Delete {Path.GetFileName(targetPath)}",
                () => {
                    if (File.Exists(targetPath)) File.Delete(targetPath);
                    if (!string.IsNullOrWhiteSpace(sidecarPath) && File.Exists(sidecarPath)) File.Delete(sidecarPath);
                },
                sidecarPath == null ? [targetPath] : [targetPath, sidecarPath]
            );

            if (string.Equals(selectedBeforeDelete, targetPath, StringComparison.OrdinalIgnoreCase)) Editor.SetSelectedAsset(null);
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
            var sidecarPath = type == CreateItemType.Collection ? null : GetSidecarMetaPathFor(path);

            History.RecordPathChange(
                $"Create {GetCreateItemLabel(type)} {Path.GetFileName(path)}",
                () => CreateItemAtPath(type, trimmedName, path),
                sidecarPath == null ? [path] : [path, sidecarPath]
            );

            Notifications.Show($"{GetCreateItemLabel(type)} '{Path.GetFileName(path)}' created.");
            return true;
        } catch (Exception e) {
            Notifications.Show($"{GetCreateItemLabel(type)} creation failed: {e.Message}");
            return false;
        }
    }

    private void CreateItemAtPath(CreateItemType type, string trimmedName, string path) {

        switch (type) {
            case CreateItemType.Collection:
                Directory.CreateDirectory(path);
                EnsureCollectionSettings(path);
                break;
            case CreateItemType.Level:
                File.WriteAllText(path, $$"""
                                         {
                                           "Root": {
                                             "Name": "{{trimmedName}}",
                                             "Children": {}
                                           }
                                         }
                                         """);
                break;
            case CreateItemType.Material:
                JsonFile.WriteIndented(path, new MaterialAsset.MaterialData { GUID = Guid.NewGuid().ToString("N") });
                break;
            case CreateItemType.Script:
                File.WriteAllText(path, BuildScriptTemplate(trimmedName));
                break;
            case CreateItemType.Prefab:
                File.WriteAllText(path, $$"""
                                         {
                                           "GUID": "{{Guid.NewGuid():N}}",
                                           "Root": {
                                             "Children": {}
                                           }
                                         }
                                         """);
                break;
        }
    }

    private bool IsUnderCollectionsRoot(string path) {

        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootPath = Path.GetFullPath(_collectionsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var builtInRootPath = Path.GetFullPath(CollectionData.BuiltInRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return fullPath.Equals(rootPath, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(rootPath + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || fullPath.Equals(builtInRootPath, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(builtInRootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(builtInRootPath + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCategoryFolderName(string? name) =>
        !string.IsNullOrEmpty(name) && Categories.Any(category => string.Equals(category.Name, name, StringComparison.OrdinalIgnoreCase));

    private void EnsureCollectionSettings(string collectionPath) {

        if (!Directory.Exists(collectionPath)) return;
        if (CollectionData.IsRoot(collectionPath)) return;

        var settingsPath = GetCollectionSettingsPath(collectionPath);
        if (File.Exists(settingsPath)) return;

        JsonFile.WriteIndented(settingsPath, new CollectionSettings());
    }

    private static string GetCollectionSettingsPath(string collectionPath) => Path.Combine(collectionPath, "Collection.json");

    private static CollectionSettings ReadCollectionSettings(string collectionPath) {

        var settingsPath = GetCollectionSettingsPath(collectionPath);
        if (!File.Exists(settingsPath)) return new CollectionSettings();

        return JsonFile.ReadOrDefault(settingsPath, new CollectionSettings());
    }

    private CollectionEntryKind GetCollectionEntryKind(string collectionPath) {

        if (IsAtCollectionsRoot && string.Equals(Path.GetFullPath(collectionPath), Path.GetFullPath(_collectionsRoot), StringComparison.OrdinalIgnoreCase))
            return CollectionEntryKind.Collection;

        var settings = ReadCollectionSettings(collectionPath);
        return ParseCollectionEntryKind(settings.Type);
    }

    private static CollectionEntryKind GetCollectionEntryKind(CollectionCategory category) => category.Name switch {
        "Fonts" => CollectionEntryKind.Collection,
        "Levels" => CollectionEntryKind.Level,
        "Materials" => CollectionEntryKind.Material,
        "Models" => CollectionEntryKind.Model,
        "Prefabs" => CollectionEntryKind.Prefab,
        "Scripts" => CollectionEntryKind.Script,
        "Shaders" => CollectionEntryKind.Collection,
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

        if (!AssetPaths.IsJson(path)) return false;

        var assetPath = path[..^5];
        return File.Exists(assetPath);
    }

    private static string? GetSidecarMetaPath(string path) => Directory.Exists(path) ? null : File.Exists(GetSidecarMetaPathFor(path)) ? GetSidecarMetaPathFor(path) : null;

    private static string GetSidecarMetaPathFor(string path) => path + ".json";

    private static string GetFileIcon(string path) {

        if (CollectionData.IsShader(path)) return Icons.FaCode;
        if (CollectionData.IsFont(path)) return Icons.FaFile;
        if (CollectionData.IsScript(path)) return Icons.FaFileCode;
        if (CollectionData.IsLevel(path)) return Icons.FaFlag;
        if (CollectionData.IsMaterial(path)) return Icons.FaFileImage;
        if (CollectionData.IsTexture(path)) return Icons.FaFileImage;
        if (CollectionData.IsPrefab(path)) return Icons.FaFile;
        if (CollectionData.IsModel(path)) return Icons.FaCube;

        return Icons.FaFile;
    }

    private static Vector4 GetFileColor(string path) {

        if (CollectionData.IsShader(path)) return Colors.Primary.ToVector4();
        if (CollectionData.IsFont(path)) return Colors.GuiText.ToVector4();
        if (CollectionData.IsLevel(path)) return Colors.GuiCollectionLevel.ToVector4();
        if (CollectionData.IsMaterial(path)) return Colors.GuiCollectionMaterial.ToVector4();
        if (CollectionData.IsTexture(path)) return Colors.GuiCollectionTexture.ToVector4();
        if (CollectionData.IsScript(path)) return Colors.GuiCollectionScript.ToVector4();
        if (CollectionData.IsPrefab(path)) return Colors.GuiCollectionPrefab.ToVector4();
        if (CollectionData.IsModel(path)) return Colors.GuiCollectionModel.ToVector4();

        return Colors.GuiText.ToVector4();
    }

    private static Vector4 GetCollectionColor() => Colors.GuiCollection.ToVector4();

    private static Vector4 GetCategoryColor(CollectionCategory category) => category.Name switch {
        "Fonts" => Colors.GuiText.ToVector4(),
        "Levels" => Colors.GuiCollectionLevel.ToVector4(),
        "Materials" => Colors.GuiCollectionMaterial.ToVector4(),
        "Textures" => Colors.GuiCollectionTexture.ToVector4(),
        "Scripts" => Colors.GuiCollectionScript.ToVector4(),
        "Prefabs" => Colors.GuiCollectionPrefab.ToVector4(),
        "Models" => Colors.GuiCollectionModel.ToVector4(),
        "Shaders" => Colors.Primary.ToVector4(),
        _ => Colors.GuiText.ToVector4()
    };

    private static string GetNameWithoutExtension(string path) => CollectionData.GetNameWithoutExtension(path);

    private static string GetRenameSuffix(string path) {

        var name = Path.GetFileName(path);

        if (CollectionData.IsLevel(path)) return ".lvl";
        if (CollectionData.IsMaterial(path)) return ".mat";
        if (CollectionData.IsPrefab(path)) return ".pre";

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
        CreateItemType.Level => ".lvl",
        CreateItemType.Material => ".mat",
        CreateItemType.Script => ".cs",
        CreateItemType.Prefab => ".pre",
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
        if (string.IsNullOrWhiteSpace(settings.TargetPath)) return null;

        var targetPath = Path.GetFullPath(Path.Combine(collectionPath, settings.TargetPath));
        if (!File.Exists(targetPath)) return null;

        return GetThumbnail(targetPath);
    }

    private static Texture2D? GetThumbnail(string path) {

        if (CollectionData.IsTexture(path)) {

            var textureAsset = AssetManager.GetOrImport<TextureAsset>(path);
            if (textureAsset is { Thumbnail: not null }) return textureAsset.Thumbnail.Value;

            return null;
        }

        if (CollectionData.IsMaterial(path)) return AssetManager.GetOrImport<MaterialAsset>(path)?.Thumbnail;
        if (CollectionData.IsModel(path)) return AssetManager.GetOrImport<ModelAsset>(path)?.Thumbnail;

        return null;
    }

    private static bool HasCollectionTargetCandidate(string path) => CollectionData.IsTexture(path) || CollectionData.IsMaterial(path) || CollectionData.IsModel(path) || CollectionData.IsScript(path) || CollectionData.IsLevel(path) || CollectionData.IsPrefab(path);

    private bool MoveDraggedEntryTo(string destinationDirectory) {

        if (string.IsNullOrWhiteSpace(_draggedEntryPath)) return false;
        return MoveEntryTo(_draggedEntryPath, _draggedEntryIsDirectory, destinationDirectory);
    }

    private bool CanMoveDraggedEntryTo(string destinationDirectory) {

        if (string.IsNullOrWhiteSpace(_draggedEntryPath)) return false;
        return CanMoveEntryTo(_draggedEntryPath, _draggedEntryIsDirectory, destinationDirectory);
    }

    private bool MoveEntryTo(string sourcePath, bool isDirectory, string destinationDirectory) =>
        isDirectory
            ? MoveDirectoryTo(sourcePath, destinationDirectory)
            : MoveAssetTo(sourcePath, destinationDirectory);

    private bool CanMoveEntryTo(string sourcePath, bool isDirectory, string destinationDirectory) {

        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(destinationDirectory)) return false;
        if (!Directory.Exists(destinationDirectory)) return false;
        if (IsBuiltInPath(sourcePath) || IsBuiltInPath(destinationDirectory)) return false;

        var fullSourcePath = Path.GetFullPath(sourcePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullDestinationDirectory = Path.GetFullPath(destinationDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var sourceParent = Path.GetDirectoryName(fullSourcePath);

        if (string.Equals(sourceParent, fullDestinationDirectory, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!isDirectory) return File.Exists(sourcePath);
        if (!Directory.Exists(sourcePath)) return false;
        if (string.Equals(fullSourcePath, fullDestinationDirectory, StringComparison.OrdinalIgnoreCase)) return false;

        return !fullDestinationDirectory.StartsWith(fullSourcePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               && !fullDestinationDirectory.StartsWith(fullSourcePath + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private bool MoveDirectoryTo(string sourcePath, string destinationDirectory) {

        try {
            if (!CanMoveEntryTo(sourcePath, isDirectory: true, destinationDirectory)) return false;

            var directoryName = Path.GetFileName(sourcePath);
            var existingNames = Directory.EnumerateFileSystemEntries(destinationDirectory)
                .Select(Path.GetFileName)
                .OfType<string>()
                .ToList();
            var finalName = Directory.Exists(Path.Combine(destinationDirectory, directoryName))
                ? Generators.AvailableName(directoryName, existingNames)
                : directoryName;
            var destinationPath = Path.Combine(destinationDirectory, finalName);

            using var transaction = History.Begin($"Move {Path.GetFileName(sourcePath)}");
            transaction.CapturePath(sourcePath);
            transaction.CapturePath(destinationPath);
            transaction.After(
                redo: () => OnDirectoryPathMoved(sourcePath, destinationPath),
                undo: () => OnDirectoryPathMoved(destinationPath, sourcePath)
            );
            MoveDirectoryFileSystem(sourcePath, destinationPath);
            OnDirectoryPathMoved(sourcePath, destinationPath);
            if (!transaction.Commit()) return false;

            Notifications.Show($"Moved '{Path.GetFileName(sourcePath)}' to '{AssetManager.GetStoredPath(destinationDirectory)}'.");
            return true;

        } catch (Exception e) {
            Notifications.Show($"Move failed: {e.Message}");
            return false;
        }
    }

    private bool MoveAssetTo(string sourcePath, string destinationDirectory) {

        try {
            if (!CanMoveEntryTo(sourcePath, isDirectory: false, destinationDirectory)) return false;

            var fileName = Path.GetFileName(sourcePath);
            var baseName = CollectionData.GetNameWithoutExtension(sourcePath);
            var suffix = GetRenameSuffix(sourcePath);
            var existingNames = Directory.EnumerateFiles(destinationDirectory)
                .Select(CollectionData.GetNameWithoutExtension)
                .ToList();
            var finalBaseName = File.Exists(Path.Combine(destinationDirectory, fileName))
                ? Generators.AvailableName(baseName, existingNames)
                : baseName;
            var destinationPath = Path.Combine(destinationDirectory, finalBaseName + suffix);

            if (Path.GetFullPath(sourcePath).Equals(Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
                return false;

            var sourceSidecar = GetSidecarMetaPathFor(sourcePath);
            var destinationSidecar = GetSidecarMetaPathFor(destinationPath);
            var hasSidecar = !CollectionData.IsLevel(sourcePath) && !CollectionData.IsPrefab(sourcePath) && File.Exists(sourceSidecar);

            using var transaction = History.Begin($"Move {Path.GetFileName(sourcePath)}");
            transaction.CapturePath(sourcePath);
            transaction.CapturePath(destinationPath);
            if (hasSidecar) {
                transaction.CapturePath(sourceSidecar);
                transaction.CapturePath(destinationSidecar);
            }

            transaction.After(
                redo: () => OnFilePathMoved(sourcePath, destinationPath),
                undo: () => OnFilePathMoved(destinationPath, sourcePath)
            );
            MoveFileSystem(sourcePath, destinationPath, hasSidecar);
            OnFilePathMoved(sourcePath, destinationPath);
            if (!transaction.Commit()) return false;

            Notifications.Show($"Moved '{Path.GetFileName(sourcePath)}' to '{AssetManager.GetStoredPath(destinationDirectory)}'.");
            return true;

        } catch (Exception e) {
            Notifications.Show($"Move failed: {e.Message}");
            return false;
        }
    }

    private bool CanDragEntry(string path) =>
        (File.Exists(path) || Directory.Exists(path))
        && !CollectionData.IsRoot(path)
        && !IsBuiltInPath(path);

    private bool CanAcceptEntryDrop(string destinationDirectory) =>
        Directory.Exists(destinationDirectory)
        && !IsBuiltInPath(destinationDirectory);

    private string? GetUpMoveDestinationDirectory() {

        if (CollectionData.IsRoot(_currentPath)) return null;

        var parent = Directory.GetParent(_currentPath);
        return parent != null && IsUnderCollectionsRoot(parent.FullName) && !IsBuiltInPath(parent.FullName)
            ? parent.FullName
            : null;
    }

    private static bool IsBuiltInPath(string path) {

        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var builtInRootPath = Path.GetFullPath(CollectionData.BuiltInRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return fullPath.Equals(builtInRootPath, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(builtInRootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(builtInRootPath + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct CollectionCategory(string Name, Func<string, bool> Match, string Icon);
    private readonly record struct CategoryState(CollectionCategory Category, int Count);
    private readonly record struct BrowserEntry(string Name, BrowserEntryKind Kind, bool IsActive, int Count, string? EntryPath, CategoryState? CategoryState) {

        public static BrowserEntry CreateProject() => new(ProjectLabel, BrowserEntryKind.Project, true, 0, null, null);
        public static BrowserEntry CreateCollection(string path) => new(CollectionData.GetCollectionDisplayName(path), BrowserEntryKind.Collection, true, 0, path, null);
        public static BrowserEntry CreateCollectionGroup(int count) => new(ChildCollectionsLabel, BrowserEntryKind.CollectionGroup, count > 0, count, null, null);
        public static BrowserEntry CreateCategory(CategoryState state) => new(state.Category.Name, BrowserEntryKind.Category, state.Count > 0, state.Count, null, state);
    }

    private enum BrowserEntryKind {
        Project,
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
        public string TargetPath { get; set; } = "";
        public string Type { get; set; } = "Collection";
    }

    private readonly record struct NavigationState(string Path, CollectionCategory? Category, bool ShowChildCollections);

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
