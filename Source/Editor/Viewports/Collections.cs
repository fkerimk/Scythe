using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using static ImGuiNET.ImGui;

namespace Viewports;
    
internal class Collections : Viewport {

    private readonly string _collectionsRoot;
    
    private string _currentPath;
    private string? _selectedPath;
    
    private string _newCollectionName = "";
    private bool _showAddPopup;
    
    private string RelativePath => Path.GetRelativePath(_collectionsRoot, _currentPath);
    private int Depth => RelativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries).Length;

    public Collections() : base("Collections") {

        _collectionsRoot = Path.Combine(ScytheConfig.Current.Project, "Collections");
        
        _currentPath = _collectionsRoot;

        if (!Directory.Exists(_collectionsRoot)) Directory.CreateDirectory(_collectionsRoot);
    }

    public void SyncExternalSelection(string? path) => _selectedPath = path;

    protected override void OnDraw() {

        Validate();
        DrawToolbar();
        Separator();
        DrawBrowser();
        DrawPopups();
    }
    
    private void Validate() {
        
        if (RelativePath == "." || Depth != 1) return;
        
        string[] subfolders = ["Levels", "Textures", "Materials", "Models", "Scripts", "Prefabs"];
        
        foreach (var subFolderPath in subfolders)
            Directory.CreateDirectory(Path.Combine(_currentPath, subFolderPath));
    }

    private void DrawToolbar() {

        PushFont(Fonts.ImFontAwesomeNormal);

        if (Button(Icons.FaPlus)) {
            
            _newCollectionName = "";
            _showAddPopup = true;
        }
        
        PopFont();
        
        if (IsItemHovered()) SetTooltip("Add Collection");

        // Up Button
        SameLine();
        
        BeginDisabled(RelativePath == ".");
        
        PushFont(Fonts.ImFontAwesomeNormal);
        
        if (Button(Icons.FaLevelUp)) {
            
            var parent = Directory.GetParent(_currentPath);
            if (parent != null) _currentPath = parent.FullName;
        }
        
        PopFont();
        
        EndDisabled();
        
        if (IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) SetTooltip("Up");

        if (RelativePath == ".") return;
        
        SameLine();
        
        TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), RelativePath);
    }

    private void DrawBrowser() {
        
        if (!BeginChild("Browser")) return;

        var entries = Directory
            .GetFileSystemEntries(_currentPath)
            .Where(entry => !IsSidecarMetaFile(entry))
            .OrderByDescending(Directory.Exists)
            .ThenBy(Path.GetFileName, new NaturalStringComparer()!)
            .ToArray();

        foreach (var entry in entries)
            DrawEntry(entry);

        EndChild();
    }

    private void DrawEntry(string path) {

        var name = GetNameWithoutExtension(path);
        var isDirectory = Directory.Exists(path);
        var startX = GetCursorPosX();

        const float iconWidth = 20f;
        const float thumbnailSize = 16f;

        PushFont(Fonts.ImFontAwesomeNormal);

        if (!TryDrawThumbnail(path, startX, iconWidth, thumbnailSize))
            DrawIcon(path, isDirectory, startX, iconWidth);

        PopFont();

        SameLine(startX + iconWidth + 5f);
        var isSelected = string.Equals(_selectedPath, path, StringComparison.OrdinalIgnoreCase);

        if (isSelected) {
            PushStyleColor(ImGuiCol.Header, Colors.GuiButtonActive.ToVector4());
            PushStyleColor(ImGuiCol.HeaderHovered, Colors.GuiButtonActive.ToVector4());
            PushStyleColor(ImGuiCol.HeaderActive, Colors.GuiButtonActive.ToVector4());
        }

        var clicked = Selectable(name, isSelected, ImGuiSelectableFlags.None, new Vector2(GetContentRegionAvail().X, 0f));

        if (isSelected) PopStyleColor(3);

        if (!clicked) return;

        if (isDirectory) {

            _currentPath = path;
            Editor.SetSelectedAsset(null);

            return;
        }

        Editor.SetSelectedAsset(path);
        LevelBrowser.SelectObject(null);
    }

    private bool TryDrawThumbnail(string path, float startX, float iconWidth, float thumbnailSize) {

        var textureAsset = AssetManager.Get<TextureAsset>(path);
        var matAsset = AssetManager.Get<MaterialAsset>(path);
        var modelAsset = AssetManager.Get<ModelAsset>(path);

        Texture2D? thumbTex = null;

        if (textureAsset is { Thumbnail: not null })
            thumbTex = textureAsset.Thumbnail.Value;
        else if (matAsset != null) {

            if (!matAsset.Thumbnail.HasValue) Preview.UpdateThumbnail(matAsset);
            thumbTex = matAsset.Thumbnail;

        } else if (modelAsset != null) {

            if (!modelAsset.Thumbnail.HasValue) Preview.UpdateThumbnail(modelAsset);
            thumbTex = modelAsset.Thumbnail;
        }

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

    private void DrawIcon(string path, bool isDirectory, float startX, float iconWidth) {

        var icon = GetIcon(path, isDirectory);
        var iconSize = CalcTextSize(icon);
        SetCursorPosX(startX + (iconWidth - iconSize.X) * 0.5f);

        if (isDirectory) PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.8f, 0.2f, 1f));
        Text(icon);
        if (isDirectory) PopStyleColor();
    }

    private void DrawPopups() {

        if (_showAddPopup) OpenPopup("Add Collection");

        if (!BeginPopupModal("Add Collection", ref _showAddPopup, ImGuiWindowFlags.AlwaysAutoResize)) return;
        
        Text("Enter collection name:");
            
        if (IsWindowAppearing()) SetKeyboardFocusHere();
            
        if (InputText("##name", ref _newCollectionName, 64, ImGuiInputTextFlags.EnterReturnsTrue)) {
                
            CreateCollection(_newCollectionName);
            _showAddPopup = false;
            CloseCurrentPopup();
        }

        Spacing();
        Separator();
        Spacing();

        if (Button("Create", new Vector2(120, 0))) {
                
            CreateCollection(_newCollectionName);
            _showAddPopup = false;
            CloseCurrentPopup();
        }
            
        SameLine();
            
        if (Button("Cancel", new Vector2(120, 0))) {
                
            _showAddPopup = false;
            CloseCurrentPopup();
        }

        EndPopup();
    }

    private void CreateCollection(string name) {

        if (string.IsNullOrWhiteSpace(name)) return;

        var path = Path.Combine(_collectionsRoot, name);
        
        if (Directory.Exists(path)) return;

        Directory.CreateDirectory(path);
        
        string[] subfolders = ["Levels", "Textures", "Materials", "Models", "Scripts", "Prefabs"];
        
        foreach (var sub in subfolders) {
            
            Directory.CreateDirectory(Path.Combine(path, sub));
        }

        Notifications.Show($"Collection '{name}' created.");
    }

    private static bool IsSidecarMetaFile(string path) {

        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;

        var assetPath = path[..^5];
        return File.Exists(assetPath);
    }

    private string GetIcon(string path, bool isDirectory) {

        if (isDirectory) {

            var name = Path.GetFileName(path);
            return name switch {
                "Materials" when Depth == 1 => Icons.FaFileImage,
                "Models" when Depth == 1 => Icons.FaCube,
                "Scripts" when Depth == 1 => Icons.FaFileCode,
                "Levels" when Depth == 1 => Icons.FaMap,
                _ => Icons.FaFolder
            };
        }

        if (IsScript(path)) return Icons.FaFileCode;
        if (IsLevel(path)) return Icons.FaFlag;
        if (IsMaterial(path)) return Icons.FaFileImage;
        if (IsModel(path)) return Icons.FaCube;

        return Icons.FaFile;
    }

    private static bool IsLevel(string path) => path.EndsWith(".level.json", StringComparison.OrdinalIgnoreCase);
    private static bool IsMaterial(string path) => path.EndsWith(".material.json", StringComparison.OrdinalIgnoreCase);
    private static bool IsScript(string path) => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

    private static bool IsModel(string path) {

        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".fbx" or ".obj" or ".gltf" or ".glb" or ".iqm";
    }

    private static string GetNameWithoutExtension(string path) {

        var name = Path.GetFileName(path);

        if (IsLevel(path)) return name[..^11];
        if (IsMaterial(path)) return name[..^14];

        return Path.GetFileNameWithoutExtension(name);
    }
}
