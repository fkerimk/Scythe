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
    private static bool _showCreateProjectModal;
    private static bool _showDeleteProjectModal;
    private static Texture2D _logoTexture;

    private struct ProjectInfo {
        public string Name;
        public string Path;
        public bool IsLatest;
    }

    public static string? Show() {
        _selectedProject = null;
        _shouldExit = false;
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
        var projectsDir = Path.Combine(PathUtil.GetBaseRoot(), "Projects");
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
            if (Button("Create New###btnCreate", new Vector2(160, 34))) _showCreateProjectModal = true;

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
                        _showDeleteProjectModal = true;
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

            DrawModals();
        }
        PopStyleVar();
        ImGui.End();
    }

    private static void DrawModals() {
        if (_showCreateProjectModal) OpenPopup("Create Project");

        if (Modal.Begin("Create Project", ref _showCreateProjectModal)) {
            Text("Project Name");

            if (IsWindowAppearing()) SetKeyboardFocusHere();

            SetNextItemWidth(360);
            if (InputText("##projname", ref _newProjectName, 64, ImGuiInputTextFlags.EnterReturnsTrue)) {
                var createdProject = CreateProject(_newProjectName);
                RefreshProjects();

                if (!string.IsNullOrEmpty(createdProject)) {
                    CommandLine.Runtime = false;
                    _selectedProject = createdProject;
                    _shouldExit = true;
                    _showCreateProjectModal = false;
                    CloseCurrentPopup();
                }
            }

            Spacing();
            Separator();
            Spacing();

            if (Button("Create", new Vector2(160, 0))) {
                var createdProject = CreateProject(_newProjectName);
                RefreshProjects();

                if (!string.IsNullOrEmpty(createdProject)) {
                    CommandLine.Runtime = false;
                    _selectedProject = createdProject;
                    _shouldExit = true;
                    _showCreateProjectModal = false;
                    CloseCurrentPopup();
                }
            }

            SameLine();

            if (Button("Cancel", new Vector2(160, 0))) {
                _showCreateProjectModal = false;
                CloseCurrentPopup();
            }

            Modal.End();
        }

        if (_showDeleteProjectModal) OpenPopup("DeleteConfirm");

        if (Modal.Begin("DeleteConfirm", ref _showDeleteProjectModal)) {
            Text("Delete project?");
            TextDisabled(_projectToDelete ?? "");

            Spacing();
            Separator();
            Spacing();

            if (Button("DELETE", new Vector2(160, 0))) { 
                if (!string.IsNullOrEmpty(_projectToDelete)) { try { Directory.Delete(_projectToDelete, true); RefreshProjects(); } catch { } }
                _projectToDelete = null; 
                _showDeleteProjectModal = false;
                CloseCurrentPopup(); 
            }

            SameLine();

            if (Button("Cancel", new Vector2(160, 0))) {
                _projectToDelete = null;
                _showDeleteProjectModal = false;
                CloseCurrentPopup();
            }

            Modal.End();
        }
    }

    private static string? CreateProject(string name) {
        var trimmedName = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName)) return null;

        var templatePath = Path.Combine(PathUtil.GetBaseRoot(), "Template");
        if (!Directory.Exists(templatePath)) return null;

        var projectName = SanitizeProjectDirectoryName(trimmedName);
        if (string.IsNullOrWhiteSpace(projectName)) projectName = "New Project";

        var path = Path.Combine(PathUtil.GetBaseRoot(), "Projects", projectName);
        var originalPath = path;
        var i = 1;

        while (Directory.Exists(path))
            path = originalPath + i++;

        CopyDirectory(templatePath, path);

        var projectJsonPath = Path.Combine(path, "Project.json");
        var config = JsonFile.ReadOrDefault<ProjectConfig?>(projectJsonPath, null) ?? new ProjectConfig();
        config.Name = trimmedName;
        JsonFile.WriteIndented(projectJsonPath, config);
        return path;
    }

    private static string SanitizeProjectDirectoryName(string name) {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return sanitized.TrimEnd('.', ' ');
    }

    private static void CopyDirectory(string sourcePath, string destinationPath) {
        Directory.CreateDirectory(destinationPath);

        foreach (var directory in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories)) {
            var relativePath = Path.GetRelativePath(sourcePath, directory);
            Directory.CreateDirectory(Path.Combine(destinationPath, relativePath));
        }

        foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories)) {
            var relativePath = Path.GetRelativePath(sourcePath, file);
            var destinationFile = Path.Combine(destinationPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(file, destinationFile, overwrite: false);
        }
    }
}
