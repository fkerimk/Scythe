using System.Numerics;
using ImGuiNET;
using static ImGuiNET.ImGui;

namespace Viewports;
    
internal class Collections : Viewport {

    private readonly string _collectionsRoot;
    
    private string _currentPath;
    
    private string _newCollectionName = "";
    private bool _showAddPopup;
    
    private string RelativePath => Path.GetRelativePath(_collectionsRoot, _currentPath);
    private int Depth => RelativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries).Length;

    public Collections() : base("Collections") {

        _collectionsRoot = Path.Combine(ScytheConfig.Current.Project, "Collections");
        
        _currentPath = _collectionsRoot;

        if (!Directory.Exists(_collectionsRoot)) Directory.CreateDirectory(_collectionsRoot);
    }

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
        
        var directories = Directory.GetDirectories(_currentPath);

        foreach (var dir in directories) {
                
            var name = Path.GetFileName(dir);
                
            PushFont(Fonts.ImFontAwesomeNormal);

            var icon = name switch {
                
                "Materials" when Depth == 1 => Icons.FaFileImage,
                "Models" when Depth == 1 => Icons.FaCube,
                "Scripts" when Depth == 1 => Icons.FaFileCode,
                _ => Icons.FaFolder
            };

            var startX = GetCursorPosX();
            
            const float iconWidth = 20f;
            
            var iconSize = CalcTextSize(icon);
            SetCursorPosX(startX + (iconWidth - iconSize.X) * 0.5f);
            
            TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), icon);
            
            PopFont();
                
            SameLine(startX + iconWidth + 5f);
            
            if (!Selectable(name, false, ImGuiSelectableFlags.AllowDoubleClick)) continue;
            
            if (IsMouseDoubleClicked(ImGuiMouseButton.Left))
                _currentPath = dir;
        }

        EndChild();
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
}
