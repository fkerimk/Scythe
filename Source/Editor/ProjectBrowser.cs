using System;
using System.Numerics;
using ImGuiNET;
using Newtonsoft.Json;
using Raylib_cs;
using static ImGuiNET.ImGui;

internal class ProjectBrowser : Viewport {

    private string _currentPath, _searchFilter = "";

    private const float ThumbnailSize = 64f, Padding = 16f;

    // Selection & Drag Logic
    private HashSet<string> _selectedPaths = [];
    private string? _selectionAnchor; // For Shift-Selection
    private bool _isBoxSelecting;
    private Vector2 _boxSelectStart;
    private HashSet<string> _preBoxSelection = []; // Selection before box drag started

    // Interaction Flags
    private bool _ignoreMouseRelease; // To prevent click-after-drag
    private bool _itemClickedThisFrame;

    // Rename State
    private string? _renamingPath;
    private string _renamingExtension = "";
    private string _renameBuffer = "";
    private bool _requestRenameFocus;
    private bool _setRenameSelection;

    // Search
    private string _lastSearch = "";
    private readonly List<string> _cachedSearchResults = [];

    public string? SelectedFile => Editor.SelectedAssetPath;

    public void SyncExternalSelection(string? path) {

        _selectedPaths.Clear();

        if (string.IsNullOrEmpty(path)) {

            _selectionAnchor = null;
            return;
        }

        _selectedPaths.Add(path);
        _selectionAnchor = path;
    }

    public ProjectBrowser() : base("Project") {

        _currentPath = ScytheConfig.Current.Project;

        if (!Directory.Exists(_currentPath)) Directory.CreateDirectory(_currentPath);

        // Settings
        GetIO().MouseDoubleClickTime = 0.5f; // Windows standard
    }

    private static string GetPath() => Path.Join(ScytheConfig.Current.Project, "Project", "ProjectBrowser.json");

    public void Load() {

        var path = GetPath();

        if (!File.Exists(path)) return;

        SafeExec.Try(() => {

                var settings = JsonConvert.DeserializeObject<ProjectBrowserSettings>(File.ReadAllText(path));

                if (settings == null) return;

                var absPath = Path.Join(ScytheConfig.Current.Project, settings.CurrentPath);

                if (Directory.Exists(absPath)) _currentPath = absPath;
            }
        );
    }

    public void Save() {

        var path = GetPath();
        var dir = Path.GetDirectoryName(path);

        if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var relPath = Path.GetRelativePath(ScytheConfig.Current.Project, _currentPath);
        var settings = new ProjectBrowserSettings { CurrentPath = relPath };

        File.WriteAllText(path, JsonConvert.SerializeObject(settings, Formatting.Indented));
    }

    private class ProjectBrowserSettings {
        public string CurrentPath { get; init; } = "";
    }

    protected override void OnDraw() {

        // Top Bar: Navigation -> Path -> Spacer -> Search (Right)
        PushFont(Fonts.ImFontAwesomeNormal);

        var isRoot = Path.GetFullPath(_currentPath).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(ScytheConfig.Current.Project).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

        BeginDisabled(isRoot);

        if (Button(Icons.FaLevelUp)) {

            var parent = Directory.GetParent(_currentPath);
            if (parent != null) _currentPath = parent.FullName;
        }

        EndDisabled();

        PopFont();
        if (IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) SetTooltip("Up");

        SameLine();
        if (Button("Home")) _currentPath = ScytheConfig.Current.Project;

        SameLine();

        var rootName = new DirectoryInfo(ScytheConfig.Current.Project).Name;
        var relativePath = Path.GetRelativePath(ScytheConfig.Current.Project, _currentPath);
        if (relativePath == ".") relativePath = "";

        var displayPath = string.IsNullOrEmpty(relativePath) ? rootName : $"{rootName}/{relativePath.Replace('\\', '/')}";

        TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), displayPath);

        // Right Align Search
        var avail = GetContentRegionAvail().X;
        const float searchWidth = 200f;

        var offset = avail - searchWidth;
        if (offset > 0)
            SameLine(GetCursorPosX() + offset);
        else
            SameLine();

        // Search Icon
        PushFont(Fonts.ImFontAwesomeSmall);
        SetCursorPosY(GetCursorPosY() + 4f); // Center vertically approx
        Text(Icons.FaSearch);
        PopFont();
        SameLine();

        SetNextItemWidth(searchWidth - 45f); // Adjust for icon and X button
        if (InputTextWithHint("##search", "Search...", ref _searchFilter, 64)) UpdateSearch();

        // Clear Button
        if (!string.IsNullOrEmpty(_searchFilter)) {

            SameLine();

            if (Button("X")) {

                _searchFilter = "";
                UpdateSearch();
            }
        }

        Separator();

        // Split View: Tree | Content
        if (!BeginTable("ProjectBrowserLayout", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV)) return;

        TableSetupColumn("Tree", ImGuiTableColumnFlags.WidthFixed, 200f);
        TableSetupColumn("Files", ImGuiTableColumnFlags.WidthStretch);

        // Left Panel: Directory Tree
        TableNextColumn();
        BeginChild("TreeRegion");
        DrawDirectoryTree(ScytheConfig.Current.Project);
        EndChild();

        // Right Panel: Grid Content
        TableNextColumn();
        BeginChild("ContentRegion");
        DrawContentGrid(_currentPath);
        EndChild();

        EndTable();
    }

    private void UpdateSearch() {

        if (_searchFilter == _lastSearch) return;

        _lastSearch = _searchFilter;
        _cachedSearchResults.Clear();

        if (string.IsNullOrWhiteSpace(_searchFilter)) return;

        SafeExec.Try(() => {

                var allFiles = Directory.EnumerateFileSystemEntries(_currentPath, "*", SearchOption.AllDirectories);

                foreach (var f in allFiles) {

                    if (!f.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase)) continue;

                    _cachedSearchResults.Add(f);

                    if (_cachedSearchResults.Count > 500) break; // Limit
                }
            }
        );
    }

    private void DrawDirectoryTree(string rootPath) {

        var full = Path.GetFullPath(rootPath);

        if (!Directory.Exists(rootPath)) return;

        var name = Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(name)) name = rootPath;

        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (_currentPath == rootPath) flags |= ImGuiTreeNodeFlags.Selected;
        if (rootPath == ScytheConfig.Current.Project) flags |= ImGuiTreeNodeFlags.DefaultOpen;

        var hasSubDirs = false;

        SafeExec.Try(() => hasSubDirs = Directory.EnumerateDirectories(rootPath).Any());

        if (!hasSubDirs) flags |= ImGuiTreeNodeFlags.Leaf;

        // Draw the node skeleton (Arrow + Selection Highlight)
        var isNodeOpen = TreeNodeEx("###" + rootPath, flags);

        // Handle Interaction
        if (IsItemClicked() && !IsItemToggledOpen()) _currentPath = rootPath;

        HandleDropTarget(rootPath);

        // Draw Icon and Text on top of the node
        SameLine();
        SetCursorPosX(GetCursorPosX() - 10f); // Pull closer to arrow

        var icon = Icons.FaFolder;
        var folderColor = new Vector4(1f, 0.8f, 0.2f, 1f);

        if (full.Equals(Path.GetFullPath(ScytheConfig.Current.Project), StringComparison.OrdinalIgnoreCase))
            icon = Icons.FaHouse;
        else if (full.Equals(Path.GetFullPath(Path.Join(ScytheConfig.Current.Project, "Levels")), StringComparison.OrdinalIgnoreCase))
            icon = Icons.FaMap;
        else if (full.Equals(Path.GetFullPath(Path.Join(ScytheConfig.Current.Project, "Scripts")), StringComparison.OrdinalIgnoreCase)) icon = Icons.FaCode;

        PushFont(Fonts.ImFontAwesomeNormal);
        TextColored(folderColor, icon);
        PopFont();

        SameLine();
        SetCursorPosX(GetCursorPosX() - 4f); // Pull closer to icon
        Text(name);

        if (BeginPopupContextItem("TreeCMS_" + rootPath)) {

            _currentPath = rootPath; // Switch to this folder

            if (BeginMenu("Create")) {
                DrawCreateMenu();
                EndMenu();
            }

            if (MenuItem("New Folder")) CreateNewFolder();

            Separator();

            if (MenuItem("Delete")) {
                _selectedPaths.Clear();
                _selectedPaths.Add(rootPath);
                DeleteSelectedItems();
            }

            if (MenuItem("Rename")) StartRename(rootPath);

            EndPopup();
        }

        if (!isNodeOpen) return;

        if (hasSubDirs)
            foreach (var dir in Directory.GetDirectories(rootPath))
                DrawDirectoryTree(dir);

        TreePop();
    }

    private void DrawContentGrid(string path) {

        if (!Directory.Exists(path)) return;

        _itemClickedThisFrame = false;
        _ignoreMouseRelease = false;

        // Handle Box Selection State
        if (_isBoxSelecting) {

            if (!IsMouseDown(ImGuiMouseButton.Left)) {

                _isBoxSelecting = false;
                _ignoreMouseRelease = true;
                _preBoxSelection.Clear();
            } else
                _selectedPaths = [.._preBoxSelection];
        }

        var avail = GetContentRegionAvail();
        var availWidth = avail.X;

        // Calculate Box Rect for Intersection Logic
        var boxRect = new Rect();

        if (_isBoxSelecting) {

            var mousePos = GetMousePos();
            var min = new Vector2(Math.Min(_boxSelectStart.X, mousePos.X), Math.Min(_boxSelectStart.Y, mousePos.Y));
            var max = new Vector2(Math.Max(_boxSelectStart.X, mousePos.X), Math.Max(_boxSelectStart.Y, mousePos.Y));
            boxRect = new Rect(min.X, min.Y, max.X - min.X, max.Y - min.Y);
        }

        // Draw Background Button (Capture empty space clicks)
        SetNextItemAllowOverlap();
        InvisibleButton("##grid_bg", avail);
        var bgClicked = IsItemClicked(ImGuiMouseButton.Left) && !_itemClickedThisFrame;

        // Background Context Menu
        if (BeginPopupContextItem("GridCMS", ImGuiPopupFlags.MouseButtonRight)) {

            if (BeginMenu("Create")) {

                DrawCreateMenu();
                EndMenu();
            }

            if (MenuItem("New Folder")) CreateNewFolder();

            EndPopup();
        }

        // Start Box Selection
        if (IsItemActive() && IsMouseDragging(ImGuiMouseButton.Left)) {

            if (!_isBoxSelecting) {

                _isBoxSelecting = true;
                _boxSelectStart = GetMousePos();
                _preBoxSelection = new HashSet<string>(_selectedPaths);

                if (!GetIO().KeyCtrl) {

                    _selectedPaths.Clear();
                    _preBoxSelection.Clear();
                }
            }
        }

        // Reset Cursor to draw items on top
        SetCursorScreenPos(GetItemRectMin());

        // Content
        IEnumerable<string> entries = string.IsNullOrWhiteSpace(_searchFilter) ? Directory.GetFileSystemEntries(_currentPath) : _cachedSearchResults;

        // Sort
        var entriesList = entries.OrderByDescending(Directory.Exists).ThenBy(Path.GetFileName, new NaturalStringComparer()!).ToList();

        var columns = (int)(availWidth / (ThumbnailSize + Padding));
        if (columns < 1) columns = 1;

        Columns(columns, "Grid", false);

        var searchActive = !string.IsNullOrWhiteSpace(_searchFilter);

        foreach (var entryPath in entriesList) {

            var full = Path.GetFullPath(entryPath);

            // Skip meta files (e.g. Pistol.fbx.json)
            if (full.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) {
                var assetPath = full[..^5];

                if (File.Exists(assetPath)) continue;
            }

            var isDirectory = Directory.Exists(entryPath);

            if (DrawGridItem(entryPath, isDirectory, boxRect, out var doubleClicked)) {

                // Shift Selection
                if (GetIO().KeyShift) HandleShiftSelection(entryPath, entriesList);

                // Double Click Nav
                if (searchActive && doubleClicked) {

                    var parent = Path.GetDirectoryName(entryPath);

                    if (parent != null && Directory.Exists(parent)) {

                        _currentPath = parent;
                        _searchFilter = "";
                        _lastSearch = "";
                    }
                }
            }

            NextColumn();
        }

        Columns(1);

        // Clear Selection if BG Clicked
        if (bgClicked && !_itemClickedThisFrame && !_isBoxSelecting && !_ignoreMouseRelease && !GetIO().KeyCtrl) {

            _selectedPaths.Clear();
            Editor.SetSelectedAsset(null);
        }

        // Draw Box Overlay
        if (_isBoxSelecting) {

            var drawList = GetWindowDrawList();
            drawList.AddRectFilled(boxRect.Min, boxRect.Max, GetColorU32(ImGuiCol.TextSelectedBg, 0.2f));
            drawList.AddRect(boxRect.Min, boxRect.Max, GetColorU32(ImGuiCol.TextSelectedBg, 0.8f));
        }

        // Keyboard Shortcuts
        if (IsWindowFocused()) {

            if (IsKeyPressed(ImGuiKey.Delete) && _selectedPaths.Count > 0) DeleteSelectedItems();
            if (IsKeyPressed(ImGuiKey.F2) && _selectedPaths.Count == 1) StartRename(_selectedPaths.First());
        }

        SyncSelectedAsset();
    }

    private void SyncSelectedAsset() {

        if (_selectedPaths.Count != 1) {

            if (Editor.SelectedAssetPath != null) Editor.SetSelectedAsset(null);
            return;
        }

        var selectedPath = _selectedPaths.First();

        if (Directory.Exists(selectedPath)) {

            if (Editor.SelectedAssetPath != null) Editor.SetSelectedAsset(null);
            return;
        }

        if (!string.Equals(Editor.SelectedAssetPath, selectedPath, StringComparison.OrdinalIgnoreCase))
            Editor.SetSelectedAsset(selectedPath);
    }

    private bool DrawGridItem(string path, bool isDirectory, Rect selectBox, out bool doubleClicked) {

        var itemClicked = false;
        doubleClicked = false;

        var displayName = GetNameWithoutExtension(path);

        if (displayName.Length > 24) displayName = displayName[..24] + "...";

        PushID(path);

        var drawList = GetWindowDrawList();
        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);

        const float padding = 4f;

        var cellStartScreen = GetCursorScreenPos();
        var contentStartScreen = cellStartScreen + new Vector2(padding, padding);
        SetCursorScreenPos(contentStartScreen);

        BeginGroup();

        var groupStartX = GetCursorPosX();

        // Calculate Icon Size Reference (Standardize to Font Height)
        PushFont(Fonts.ImFontAwesomeLarge);
        var standardIconSize = CalcTextSize(isDirectory ? Icons.FaFolder : Icons.FaFile);
        var maxDim = Math.Max(standardIconSize.X, standardIconSize.Y);

        var texId = IntPtr.Zero;
        var thumbTex = GetThumbnail(path);

        if (thumbTex.HasValue) texId = (IntPtr)thumbTex.Value.Id;

        if (texId != IntPtr.Zero) {

            var tex = thumbTex!.Value;
            float w = tex.Width;
            float h = tex.Height;

            // Scale to fit within the standard Icon Size
            var ratio = w / h;
            var drawW = maxDim;
            var drawH = maxDim;

            if (w > h)
                drawH = drawW / ratio;
            else
                drawW = drawH * ratio;

            // Center in the Cell
            var imgOffsetX = (ThumbnailSize - drawW) * 0.5f;
            if (imgOffsetX < 0) imgOffsetX = 0;

            SetCursorPosX(groupStartX + imgOffsetX);
            Image(texId, new Vector2(drawW, drawH));

        } else {

            var icon = Icons.FaFolder;
            var folderColor = new Vector4(1f, 0.8f, 0.2f, 1f);

            if (isDirectory) {

                var full = Path.GetFullPath(path);
                if (full.Equals(Path.GetFullPath(ScytheConfig.Current.Project), StringComparison.OrdinalIgnoreCase))
                    icon = Icons.FaHouse;
                else if (full.Equals(Path.GetFullPath(Path.Join(ScytheConfig.Current.Project, "Levels")), StringComparison.OrdinalIgnoreCase))
                    icon = Icons.FaMap;
                else if (full.Equals(Path.GetFullPath(Path.Join(ScytheConfig.Current.Project, "Scripts")), StringComparison.OrdinalIgnoreCase)) icon = Icons.FaCode;

            } else {

                if (IsScript(path))
                    icon = Icons.FaFileCode;
                else if (IsLevel(path))
                    icon = Icons.FaFlag;
                else if (IsMaterial(path))
                    icon = Icons.FaFileImage;
                else if (IsModel(path))
                    icon = Icons.FaCube;
                else
                    icon = Icons.FaFile;
            }

            var iconSize = CalcTextSize(icon);

            var iconOffset = (ThumbnailSize - iconSize.X) * 0.5f;
            if (iconOffset < 0) iconOffset = 0;
            SetCursorPosX(groupStartX + iconOffset);

            if (isDirectory) PushStyleColor(ImGuiCol.Text, folderColor);
            Text(icon);
            if (isDirectory) PopStyleColor();
        }

        PopFont();

        // Text
        if (_renamingPath == path) {

            // Rename Input
            SetNextItemWidth(ThumbnailSize);
            SetCursorPosX(groupStartX);

            if (_requestRenameFocus) {

                SetKeyboardFocusHere();
                _requestRenameFocus = false;
            }

            // Callback to handle initial selection (exclude extension)
            unsafe {

                int Callback(ImGuiInputTextCallbackData* data) {

                    if (!_setRenameSelection) return 0;

                    data->SelectionStart = 0;
                    data->SelectionEnd = data->BufTextLen;
                    data->CursorPos = data->BufTextLen;
                    _setRenameSelection = false;

                    return 0;
                }

                if (InputText("##rename", ref _renameBuffer, 128, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CallbackAlways, Callback)) ConfirmRename();
            }

            // Cancel on Escape
            if (IsItemActive() && IsKeyPressed(ImGuiKey.Escape)) CancelRename();

            // Cancel on clicking away
            if (IsItemDeactivated())
                if (_renamingPath != null)
                    CancelRename();
        } else {

            var textSize = CalcTextSize(displayName);

            if (textSize.X <= ThumbnailSize) {

                var textOffset = (ThumbnailSize - textSize.X) * 0.5f;
                SetCursorPosX(groupStartX + textOffset);
                Text(displayName);

            } else {

                SetCursorPosX(groupStartX);
                PushTextWrapPos(groupStartX + ThumbnailSize);
                TextWrapped(displayName);
                PopTextWrapPos();
            }
        }

        EndGroup();

        var groupHeight = GetItemRectSize().Y;
        var boxMax = new Vector2(cellStartScreen.X + ThumbnailSize + 2 * padding, cellStartScreen.Y + groupHeight + 2 * padding);
        var itemRect = new Rect(cellStartScreen.X, cellStartScreen.Y, boxMax.X - cellStartScreen.X, boxMax.Y - cellStartScreen.Y);

        // Interaction
        SetCursorScreenPos(cellStartScreen);
        InvisibleButton("btn", boxMax - cellStartScreen);

        var hovered = IsItemHovered();
        var isSelected = _selectedPaths.Contains(path);

        // Selection Logic via Click

        if (IsItemClicked(ImGuiMouseButton.Left)) {

            if (!_isBoxSelecting) {

                _itemClickedThisFrame = true;
                itemClicked = true;

                // Skip local selection logic and let the Caller (DrawContentGrid) handle Range Selection.
                if (!GetIO().KeyShift) {

                    _selectionAnchor = path; // Update Anchor

                    // Ctrl Click
                    if (GetIO().KeyCtrl) {

                        if (isSelected)
                            _selectedPaths.Remove(path);
                        else
                            _selectedPaths.Add(path);

                    } else {

                        // If NOT already selected, select immediately (and clear others)
                        if (!isSelected) {

                            _selectedPaths.Clear();
                            _selectedPaths.Add(path);
                        }

                        // If ALREADY selected, we wait for Release (handled below)
                    }
                }
            }
        }

        // Handle "Click on Selected" -> Deselect others on Release if NOT Dragged
        if (IsItemHovered() && IsMouseReleased(ImGuiMouseButton.Left)) {

            if (!_isBoxSelecting && !GetIO().KeyCtrl && !GetIO().KeyShift && isSelected) {

                // Check if it was a drag
                if (GetMouseDragDelta(ImGuiMouseButton.Left).Length() < 2.0f) {

                    _selectedPaths.Clear();
                    _selectedPaths.Add(path);
                }
            }
        }

        if (IsItemHovered() && IsMouseDoubleClicked(ImGuiMouseButton.Left)) {

            doubleClicked = true;

            if (isDirectory) {
                // Immediate navigation for directories

                _itemClickedThisFrame = true;
                _currentPath = path;
                _selectedPaths.Clear();
            } else {

                if (IsScript(path))
                    Editor.OpenScript(path);
                else if (IsLevel(path)) Editor.OpenLevel(path);
            }
        }

        // Context Menu
        if (BeginPopupContextItem($"ItemCMS_{path}")) {

            // Select on right click if not selected
            if (!_selectedPaths.Contains(path)) {

                _selectedPaths.Clear();
                _selectedPaths.Add(path);
            }

            if (isDirectory) {

                if (BeginMenu("Create")) {

                    var oldPath = _currentPath;
                    _currentPath = path;
                    DrawCreateMenu();
                    _currentPath = oldPath;

                    EndMenu();
                }

                if (MenuItem("New Folder")) {

                    var oldPath = _currentPath;
                    _currentPath = path;
                    CreateNewFolder();
                    _currentPath = oldPath;
                }

                Separator();
            }

            if (MenuItem("Delete")) DeleteSelectedItems();
            if (MenuItem("Rename")) StartRename(path);

            EndPopup();
        }

        if (IsItemClicked(ImGuiMouseButton.Right)) _itemClickedThisFrame = true;

        // Drag Source
        if (BeginDragDropSource()) {

            // If dragging unselected item, select it (exclusive)
            if (!_selectedPaths.Contains(path)) {

                _selectedPaths.Clear();
                _selectedPaths.Add(path);
            }

            // Mark as interacting so BG doesn't clear
            _itemClickedThisFrame = true;

            var files = string.Join("|", _selectedPaths);
            SetDragDropPayload("MOVE_FILES", IntPtr.Zero, 0);

            DragDropPayload.Data = files; // Use a helper static

            Text($"{_selectedPaths.Count} items");
            EndDragDropSource();
        }

        // Drop Target
        if (isDirectory) HandleDropTarget(path);

        // Box Selection Logic
        if (_isBoxSelecting && itemRect.Intersects(selectBox)) {

            _selectedPaths.Add(path);
            isSelected = true; // Visual Update
        }

        // Draw Background
        drawList.ChannelsSetCurrent(0);

        if (isSelected)
            drawList.AddRectFilled(cellStartScreen, boxMax, GetColorU32(ImGuiCol.ButtonActive), 4f);
        else if (hovered) drawList.AddRectFilled(cellStartScreen, boxMax, GetColorU32(ImGuiCol.HeaderHovered), 4f);

        drawList.ChannelsMerge();

        PopID();

        return itemClicked;
    }

    private unsafe void HandleDropTarget(string targetPath) {

        if (!BeginDragDropTarget()) return;

        var payload = AcceptDragDropPayload("MOVE_FILES");

        // Execute Move
        if (payload.NativePtr != null) MoveSelectedFiles(targetPath);

        EndDragDropTarget();
    }

    private void MoveSelectedFiles(string targetDir) {

        // Verify we are moving _selectedPaths
        var validPaths = _selectedPaths.ToList(); // Copy
        _selectedPaths.Clear();

        var moves = new List<(string Src, string Dest)>();

        // Calculate Moves
        foreach (var src in validPaths) {

            if (src == targetDir) continue;

            var name = Path.GetFileName(src);
            var dest = Path.Combine(targetDir, name);

            // Check strict validity
            if (src == dest) continue;
            if (File.Exists(dest) || Directory.Exists(dest)) continue; // Skip collisions

            moves.Add((src, dest));
        }

        if (moves.Count == 0) return;

        // Perform & Record
        History.StartRecording(this, $"Move {moves.Count} items");

        PerformMove(moves);

        History.SetUndoAction(() => PerformMove(moves.Select(m => (m.Dest, m.Src))));
        History.SetRedoAction(() => PerformMove(moves));

        History.StopRecording();

        return;

        void PerformMove(IEnumerable<(string s, string d)> items) {

            foreach (var (s, d) in items) {

                SafeExec.Try(() => {

                        if (Directory.Exists(s))
                            Directory.Move(s, d);
                        else if (File.Exists(s)) File.Move(s, d);
                    }
                );
            }
        }
    }

    private void DeleteSelectedItems() {

        var pathsToDelete = _selectedPaths.ToList();
        _selectedPaths.Clear();

        if (pathsToDelete.Count == 0) return;

        // Ensure trash dir exists
        PathUtil.ValidateDir("Trash", out var trashPath);

        var backups = new List<(string Original, string Backup, bool IsDir)>();

        // Backup
        foreach (var path in pathsToDelete) {

            if (!File.Exists(path) && !Directory.Exists(path)) continue;

            var isDir = Directory.Exists(path);
            var backupName = Guid.NewGuid().ToString();
            var backupPath = Path.Join(trashPath, backupName);

            SafeExec.Try(() => {

                    if (isDir)
                        CopyDirectory(path, backupPath);
                    else
                        File.Copy(path, backupPath);

                    backups.Add((path, backupPath, isDir));
                }
            );
        }

        // Perform & Record
        History.Execute($"Delete {backups.Count} items", RedoDelete, UndoDelete);

        return;

        // Define Actions
        void UndoDelete() {

            foreach (var b in backups) {

                SafeExec.Try(() => {

                        if (b.IsDir) {

                            if (Directory.Exists(b.Backup) && !Directory.Exists(b.Original)) CopyDirectory(b.Backup, b.Original);

                        } else {

                            if (File.Exists(b.Backup) && !File.Exists(b.Original)) File.Copy(b.Backup, b.Original);
                        }
                    }
                );
            }
        }

        void RedoDelete() {

            foreach (var b in backups) RecyclePath(b.Original);
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir) {

        var dir = new DirectoryInfo(sourceDir);

        if (!dir.Exists) return;

        Directory.CreateDirectory(destinationDir);

        foreach (var file in dir.GetFiles()) {

            var targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath);
        }

        foreach (var subDir in dir.GetDirectories()) {

            var newDestinationDir = Path.Combine(destinationDir, subDir.Name);
            CopyDirectory(subDir.FullName, newDestinationDir);
        }
    }

    private static void RecyclePath(string path) {
        SafeExec.Try(() => {

                // Generic fallback: Permanent Delete
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
                else
                    File.Delete(path);
            }
        );
    }

    private void CreateNewFile(string name, string extension, Func<string, string>? content = null) {

        var existingNames = Directory.GetFileSystemEntries(_currentPath).Select(GetNameWithoutExtension);
        name = Generators.AvailableName(name, existingNames);

        var fileName = name + extension;
        var fullPath = Path.Combine(_currentPath, fileName);

        var contentStr = content?.Invoke(name) ?? "";
        History.Execute($"Create {name}", redo: () => File.WriteAllText(fullPath, contentStr), undo: () => RecyclePath(fullPath));

        _selectedPaths.Clear();
        _selectedPaths.Add(fullPath);
        StartRename(fullPath);
    }

    private void CreateNewFolder(string name = "New Folder") {

        var existingNames = Directory.GetFileSystemEntries(_currentPath).Select(Path.GetFileName);
        name = Generators.AvailableName(name, existingNames!);

        var fullPath = Path.Combine(_currentPath, name);

        History.Execute($"Create Folder {name}", redo: () => Directory.CreateDirectory(fullPath), undo: () => RecyclePath(fullPath));

        _selectedPaths.Clear();
        _selectedPaths.Add(fullPath);
        StartRename(fullPath);
    }

    private void DrawCreateMenu() {

        if (MenuItem("Script"))
            CreateNewFile(
                "Script",
                ".lua",
                name => $"""
                         print("{name} is working, yay!")

                         function loop()
                             -- loop is called once per frame
                         end
                         """
            );

        if (MenuItem("Level"))
            CreateNewFile(
                "NewLevel",
                ".level.json",
                name => $$"""
                          {
                            "Root": {
                               "Name": "{{name}}",
                               "Children": {}
                            }
                          }
                          """
            );

        if (MenuItem("Material"))
            CreateNewFile(
                "Material",
                ".material.json",
                _ => JsonConvert.SerializeObject(new MaterialAsset.MaterialData { GUID = Guid.NewGuid().ToString("N") }, Formatting.Indented)
            );
    }

    // Rename Helpers
    public void RenameSelected() {

        if (_selectedPaths.Count == 1) StartRename(_selectedPaths.First());
    }

    private void HandleShiftSelection(string targetPath, List<string> entries) {

        if (_selectionAnchor == null || !entries.Contains(_selectionAnchor)) {

            _selectionAnchor = targetPath;
            _selectedPaths.Clear();
            _selectedPaths.Add(targetPath);

            return;
        }

        var startIdx = entries.IndexOf(_selectionAnchor);
        var endIdx = entries.IndexOf(targetPath);

        if (startIdx == -1 || endIdx == -1) return;

        var min = Math.Min(startIdx, endIdx);
        var max = Math.Max(startIdx, endIdx);

        if (!GetIO().KeyCtrl) _selectedPaths.Clear();

        for (var i = min; i <= max; i++) _selectedPaths.Add(entries[i]);
    }

    private void StartRename(string path) {

        _renamingPath = path;
        _renameBuffer = GetNameWithoutExtension(path);

        var fullName = Path.GetFileName(path);
        _renamingExtension = fullName.Substring(_renameBuffer.Length);

        _requestRenameFocus = true;
        _setRenameSelection = true;
    }

    private void ConfirmRename() {

        if (string.IsNullOrEmpty(_renamingPath)) return;

        var baseName = _renameBuffer;

        if (string.IsNullOrWhiteSpace(baseName)) {
            CancelRename();

            return;
        }

        var dir = Path.GetDirectoryName(_renamingPath);

        if (dir == null) return;

        var oldBaseName = GetNameWithoutExtension(_renamingPath);

        if (baseName == oldBaseName) {
            CancelRename();

            return;
        }

        var newName = baseName + _renamingExtension;
        var oldName = Path.GetFileName(_renamingPath);

        var newPath = Path.Combine(dir, newName);

        // Final Path check
        if (Directory.Exists(newPath) || File.Exists(newPath)) {

            Notifications.Show("Rename failed: Destination exists.");
            _renamingPath = null;

            return;
        }

        var oldPath = _renamingPath;

        // History
        History.Execute($"Rename {oldName}", redo: () => DoMove(oldPath, newPath), undo: () => DoMove(newPath, oldPath));

        // Update selection if it was selected
        if (_selectedPaths.Contains(oldPath)) {

            _selectedPaths.Remove(oldPath);
            _selectedPaths.Add(newPath);
        }

        _renamingPath = null;

        return;

        // Action
        void DoMove(string s, string d) {
            SafeExec.Try(() => {

                    if (Directory.Exists(s))
                        Directory.Move(s, d);
                    else if (File.Exists(s)) {
                        File.Move(s, d);

                        // Also move associated .json meta if it exists (e.g. .fbx.json)
                        var oldMeta = s + ".json";
                        var newMeta = d + ".json";
                        if (File.Exists(oldMeta)) File.Move(oldMeta, newMeta);
                    }
                }
            );
        }
    }

    private void CancelRename() => _renamingPath = null;

    // Helper struct for Rect
    public readonly struct Rect(float x, float y, float w, float h) {

        private readonly float _x = x, _y = y, _w = w, _h = h;
        public Vector2 Min => new(_x, _y);
        public Vector2 Max => new(_x + _w, _y + _h);
        public bool Intersects(Rect r) => _x < r._x + r._w && _x + _w > r._x && _y < r._y + r._h && _y + _h > r._y;
    }

    // Helpers
    private static bool IsLevel(string path) => path.EndsWith(".level.json", StringComparison.OrdinalIgnoreCase);
    private static bool IsMaterial(string path) => path.EndsWith(".material.json", StringComparison.OrdinalIgnoreCase);
    private static bool IsScript(string path) => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
    private static bool IsTexture(string path) => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase);

    private static bool IsModel(string path) {

        var ext = Path.GetExtension(path).ToLowerInvariant();

        return ext is ".fbx" or ".obj" or ".gltf" or ".iqm";
    }

    private static string GetNameWithoutExtension(string path) {

        var name = Path.GetFileName(path);

        if (IsLevel(path)) return name[..^11];
        if (IsMaterial(path)) return name[..^14];

        return Path.GetFileNameWithoutExtension(name);
    }

    private static Texture2D? GetThumbnail(string path) {

        if (IsTexture(path)) {

            var textureAsset = AssetManager.GetOrImport<TextureAsset>(path);
            if (textureAsset is { Thumbnail: not null }) return textureAsset.Thumbnail.Value;

            return null;
        }

        if (IsMaterial(path)) {

            return AssetManager.GetOrImport<MaterialAsset>(path)?.Thumbnail;
        }

        if (IsModel(path)) {

            return AssetManager.GetOrImport<ModelAsset>(path)?.Thumbnail;
        }

        return null;
    }
}
