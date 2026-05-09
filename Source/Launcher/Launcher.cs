using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;
using static ImGuiNET.ImGui;
using static Raylib_cs.Raylib;
using static rlImGui_cs.rlImGui;

internal static class Launcher {

    private static string? _selectedProject;
    private static bool _shouldExit;
    private static readonly List<ProjectInfo> Projects = [];
    private static string _newProjectName = "New Project";
    private static string? _projectToDelete;
    private static string? _popupToOpen;
    private static Texture2D _logoTexture;

    private struct ProjectInfo {
        public string Name;
        public string Path;
        public bool IsLatest;
    }

    public static string? Show() {
        Window.Show(title: "SCYTHE - Project Launcher", flags: [ConfigFlags.Msaa4xHint]);
        Setup(true, true);
        unsafe { GetIO().NativePtr->IniFilename = null; }
        Fonts.Init(); 
        RefreshProjects();
        
        if (_logoTexture.Id == 0) {
            if (PathUtil.GetPath("Collection/Icon.png", out var iconPath)) {
                var img = LoadImage(iconPath);
                _logoTexture = LoadTextureFromImage(img);
                UnloadImage(img);
            }
        }

        while (!WindowShouldClose() && !_shouldExit) {
            BeginDrawing();
            ClearBackground(Colors.Back);
            Begin();
            Style.Push();
            PushFont(Fonts.ImMontserratRegular);
            PushStyleColor(ImGuiCol.ModalWindowDimBg, new Vector4(0f, 0f, 0f, 0.8f));
            
            DrawUI();

            PopStyleColor();
            PopFont();
            Style.Pop();
            rlImGui.End();
            Notifications.Draw();
            EndDrawing();
        }

        Shutdown();
        return _selectedProject;
    }

    private static void RefreshProjects() {
        Projects.Clear();
        var projectsDir = Path.GetFullPath("Projects");
        if (!Directory.Exists(projectsDir)) Directory.CreateDirectory(projectsDir);
        
        var latestPath = string.IsNullOrEmpty(ScytheConfig.Current.Project) ? "" : Path.GetFullPath(ScytheConfig.Current.Project).Replace('\\', '/');

        foreach (var dir in Directory.GetDirectories(projectsDir)) {
            var jsonPath = Path.Combine(dir, "Project.json");
            if (File.Exists(jsonPath)) {
                try {
                    var config = JsonFile.ReadOrDefault<ProjectConfig?>(jsonPath, null);
                    var fullPath = Path.GetFullPath(dir).Replace('\\', '/');
                    var isLatest = !string.IsNullOrEmpty(latestPath) && string.Equals(fullPath, latestPath, StringComparison.OrdinalIgnoreCase);
                    
                    Projects.Add(new ProjectInfo { 
                        Name = config?.Name ?? Path.GetFileName(dir), 
                        Path = dir,
                        IsLatest = isLatest
                    });
                } catch { }
            }
        }

        // Sort: Latest first, then by name
        Projects.Sort((a, b) => {
            if (a.IsLatest && !b.IsLatest) return -1;
            if (!a.IsLatest && b.IsLatest) return 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static void DrawUI() {
        var viewport = GetMainViewport();
        SetNextWindowPos(viewport.Pos);
        SetNextWindowSize(viewport.Size);

        PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
        if (Begin("Launcher", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove)) {
            var windowSize = GetWindowSize();
            var windowPos = GetWindowPos();
            var drawList = GetWindowDrawList();

            // --- HEADER ---
            var headerHeight = 80f;
            drawList.AddRectFilled(windowPos, windowPos + new Vector2(windowSize.X, headerHeight), ColorConvertFloat4ToU32(new Vector4(0.08f, 0.08f, 0.12f, 1f)));
            drawList.AddLine(windowPos + new Vector2(0, headerHeight), windowPos + new Vector2(windowSize.X, headerHeight), ColorConvertFloat4ToU32(Colors.GuiBorder.ToVector4()));

            // Logo
            if (_logoTexture.Id != 0) {
                SetCursorPos(new Vector2(20, (headerHeight - 32) / 2));
                rlImGui.ImageSize(_logoTexture, 32, 32);
            }

            // Branding (SCYTHE ENGINE)
            SetCursorPos(new Vector2(_logoTexture.Id != 0 ? 62 : 20, (headerHeight - GetTextLineHeight()) / 2));
            Text("SCYTHE"); SameLine(0, 8);
            TextDisabled("ENGINE");

            // Create Project Button
            SetCursorPos(new Vector2(windowSize.X - 180, (headerHeight - 34) / 2));
            if (Button("Create New###btnCreate", new Vector2(160, 34))) _popupToOpen = "Create Project";

            // Validate header extent
            SetCursorPos(new Vector2(0, headerHeight));
            Dummy(new Vector2(windowSize.X, 0));

            // --- PROJECT LIST ---
            SetCursorPos(new Vector2(0, headerHeight + 10));
            
            Indent(20);
            var childHeight = windowSize.Y - GetCursorPosY() - 20;
            if (BeginChild("ProjectList", new Vector2(windowSize.X - 40, childHeight), ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar)) {
                var childDrawList = GetWindowDrawList();
                
                if (Projects.Count == 0) {
                   SetCursorPos(new Vector2(GetContentRegionAvail().X / 2 - 100, 50));
                   TextDisabled("No projects found.");
                }

                for (var i = 0; i < Projects.Count; i++) {
                    var project = Projects[i];
                    PushID(project.Path);

                    var itemHeight = 64f;
                    var screenPos = GetCursorScreenPos();
                    var width = GetContentRegionAvail().X;
                    var isHovered = IsMouseHoveringRect(screenPos, screenPos + new Vector2(width, itemHeight));

                    // Background
                    var bgColor = i % 2 == 0 ? new Vector4(1f, 1f, 1f, 0.02f) : new Vector4(0f, 0f, 0f, 0.04f);
                    if (isHovered) bgColor = new Vector4(1f, 1f, 1f, 0.08f);
                    childDrawList.AddRectFilled(screenPos, screenPos + new Vector2(width, itemHeight), ColorConvertFloat4ToU32(bgColor), 4f);

                    if (isHovered && IsMouseDoubleClicked(ImGuiMouseButton.Left)) { 
                        CommandLine.Runtime = false; _selectedProject = project.Path; _shouldExit = true; 
                    }

                    // Content Positioning
                    var itemStartPos = GetCursorPos();
                    
                    // Text (LATEST: Name)
                    var textY = (itemHeight - (GetTextLineHeight() * 2 + 4)) / 2;
                    SetCursorPos(itemStartPos + new Vector2(15, textY));
                    
                    if (project.IsLatest) {
                        TextColored(Colors.Primary.ToVector4(), "LATEST:"); SameLine(0, 6);
                    }
                    Text(project.Name);
                    
                    SetCursorPos(itemStartPos + new Vector2(15, textY + GetTextLineHeight() + 4));
                    TextDisabled(project.Path);

                    // Buttons
                    var btnWidth = 80f;
                    var btnHeight = 32f;
                    var btnY = (itemHeight - btnHeight) / 2;
                    
                    SetCursorPos(itemStartPos + new Vector2(width - 220, btnY));
                    if (Button("EDITOR", new Vector2(btnWidth, btnHeight))) { CommandLine.Runtime = false; _selectedProject = project.Path; _shouldExit = true; }
                    
                    SameLine(0, 8);
                    if (Button("RUNTIME", new Vector2(btnWidth, btnHeight))) { CommandLine.Runtime = true; _selectedProject = project.Path; _shouldExit = true; }
                    
                    SameLine(0, 8);
                    PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.15f, 0.15f, 1f));
                    PushFont(Fonts.ImFontAwesomeNormal);
                    if (Button(Icons.FaTrashAlt + "##DelBtn", new Vector2(btnHeight, btnHeight))) {
                        _projectToDelete = project.Path;
                        _popupToOpen = "DeleteConfirm";
                    }
                    PopFont();
                    PopStyleColor();

                    // Advance cursor for next item
                    SetCursorPos(itemStartPos + new Vector2(0, itemHeight + 4));
                    Dummy(new Vector2(width, 0));
                    PopID();
                }
                EndChild();
            }
            Unindent(20);

            // OPEN POPUP IN THE CORRECT PARENT CONTEXT
            if (_popupToOpen != null) {
                OpenPopup(_popupToOpen);
                _popupToOpen = null;
            }

            DrawModals();
        }
        PopStyleVar();
        ImGui.End();
    }

    private static void DrawModals() {
        var center = GetMainViewport().GetCenter();
        SetNextWindowPos(center, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        SetNextWindowSize(new Vector2(400, 0));
        
        if (BeginPopupModal("Create Project", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.AlwaysAutoResize)) {
            Dummy(new Vector2(0, 10));
            Text("Project Name:");
            SetNextItemWidth(-1);
            InputText("##projname", ref _newProjectName, 64);
            Spacing(); Separator(); Spacing();
            if (Button("Create", new Vector2(185, 40))) { CreateProject(_newProjectName); RefreshProjects(); CloseCurrentPopup(); }
            SameLine(); if (Button("Cancel", new Vector2(185, 40))) CloseCurrentPopup();
            EndPopup();
        }

        if (BeginPopupModal("DeleteConfirm", ImGuiWindowFlags.NoMove | ImGuiWindowFlags.AlwaysAutoResize)) {
            Text("Delete project?");
            TextDisabled(_projectToDelete ?? "");
            Spacing(); Separator(); Spacing();
            if (Button("DELETE", new Vector2(185, 40))) { 
                if (!string.IsNullOrEmpty(_projectToDelete)) { try { Directory.Delete(_projectToDelete, true); RefreshProjects(); } catch { } }
                _projectToDelete = null; 
                CloseCurrentPopup(); 
            }
            SameLine(); if (Button("Cancel", new Vector2(185, 40))) { _projectToDelete = null; CloseCurrentPopup(); }
            EndPopup();
        }
    }

    private static void CreateProject(string name) {
        var slug = name.Replace(" ", "");
        var path = Path.Combine(Path.GetFullPath("Projects"), slug);
        var i = 1; var originalPath = path;
        while (Directory.Exists(path)) { path = originalPath + i; i++; }
        Directory.CreateDirectory(path);
        Directory.CreateDirectory(Path.Combine(path, "Project"));
        Directory.CreateDirectory(Path.Combine(path, "Assets"));
        Directory.CreateDirectory(Path.Combine(path, "Scripts"));
        JsonFile.WriteIndented(Path.Combine(path, "Project.json"), new ProjectConfig { Name = name });
    }
}
