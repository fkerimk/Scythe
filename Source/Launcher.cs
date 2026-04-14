using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;
using static ImGuiNET.ImGui;
using static Raylib_cs.Raylib;
using static rlImGui_cs.rlImGui;
using Newtonsoft.Json;

internal static class Launcher {

    private static string? _selectedProject;
    private static bool _shouldExit;
    private static readonly List<ProjectInfo> Projects = [];
    private static string _newProjectName = "New Project";
    private static string? _projectToDelete;
    private static string? _popupToOpen;

    private struct ProjectInfo {
        public string Name;
        public string Path;
    }

    public static string? Show() {
        Window.Show(title: "SCYTHE - Project Launcher", flags: [ConfigFlags.Msaa4xHint]);
        Setup(true, true);
        Fonts.Init(); 
        RefreshProjects();

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
        foreach (var dir in Directory.GetDirectories(projectsDir)) {
            var jsonPath = Path.Combine(dir, "Project.json");
            if (File.Exists(jsonPath)) {
                try {
                    var content = File.ReadAllText(jsonPath);
                    var config = JsonConvert.DeserializeObject<ProjectConfig>(content);
                    Projects.Add(new ProjectInfo { Name = config?.Name ?? Path.GetFileName(dir), Path = dir });
                } catch { }
            }
        }
    }

    private static void DrawUI() {
        var viewport = GetMainViewport();
        SetNextWindowPos(viewport.Pos);
        SetNextWindowSize(viewport.Size);

        if (Begin("Launcher", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove)) {
            var windowSize = GetWindowSize();
            var windowPos = GetWindowPos();
            var drawList = GetWindowDrawList();

            // Background Header
            drawList.AddRectFilled(windowPos, windowPos + new Vector2(windowSize.X, 130), ColorConvertFloat4ToU32(new Vector4(0.08f, 0.08f, 0.12f, 1f)));
            drawList.AddLine(windowPos + new Vector2(0, 130), windowPos + new Vector2(windowSize.X, 130), ColorConvertFloat4ToU32(Colors.GuiBorder.ToVector4()));

            // Center-ish Logo
            Dummy(new Vector2(0, 15));
            Indent(25);
            PushFont(Fonts.ImFontAwesomeLarge);
            TextColored(Colors.Primary.ToVector4(), Icons.FaCube);
            PopFont();
            SameLine();
            Dummy(new Vector2(5, 0)); SameLine();
            Text("SCYTHE"); SameLine(); TextDisabled("PRO ENGINE");

            // Close button
            SameLine(windowSize.X - 45);
            PushFont(Fonts.ImFontAwesomeNormal);
            if (Button(Icons.FaXMark + "##Close", new Vector2(30, 30))) _shouldExit = true;
            PopFont();
            
            // Toolbar
            Dummy(new Vector2(0, 45));
            Text("Recent Projects");
            SameLine(windowSize.X - 185);
            if (Button("Create New###btnCreate", new Vector2(160, 34))) _popupToOpen = "Create Project";
            Unindent(25);

            Dummy(new Vector2(0, 15));

            // Project List Area
            Indent(20);
            var childHeight = windowSize.Y - GetCursorPosY() - 25;
            if (BeginChild("ProjectList", new Vector2(windowSize.X - 40, childHeight))) {
                var childDrawList = GetWindowDrawList();
                
                if (Projects.Count == 0) {
                   Dummy(new Vector2(0, 50));
                   SetCursorPosX(GetContentRegionAvail().X / 2 - 100);
                   TextDisabled("No projects found.");
                }

                for (var i = 0; i < Projects.Count; i++) {
                    var project = Projects[i];
                    PushID(project.Path);

                    var screenPos = GetCursorScreenPos();
                    var width = GetContentRegionAvail().X;
                    var itemHeight = 90f;

                    if (IsMouseHoveringRect(screenPos, screenPos + new Vector2(width, itemHeight))) {
                        childDrawList.AddRectFilled(screenPos, screenPos + new Vector2(width, itemHeight), ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.05f)));
                        if (IsMouseDoubleClicked(ImGuiMouseButton.Left)) { CommandLine.Runtime = false; _selectedProject = project.Path; _shouldExit = true; }
                    }

                    Dummy(new Vector2(width, itemHeight));
                    var itemStartPos = GetCursorPos() - new Vector2(0, itemHeight);
                    
                    SetCursorPos(itemStartPos + new Vector2(15, 20));
                    Text(project.Name);
                    SetCursorPos(itemStartPos + new Vector2(15, 45));
                    TextDisabled(project.Path);

                    SetCursorPos(itemStartPos + new Vector2(width - 240, 25));
                    if (Button("EDITOR", new Vector2(85, 40))) { CommandLine.Runtime = false; _selectedProject = project.Path; _shouldExit = true; }
                    SameLine();
                    if (Button("RUNTIME", new Vector2(85, 40))) { CommandLine.Runtime = true; _selectedProject = project.Path; _shouldExit = true; }
                    SameLine();
                    
                    PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.15f, 0.15f, 1f));
                    PushFont(Fonts.ImFontAwesomeNormal);
                    if (Button(Icons.FaTrashAlt + "##DelBtn", new Vector2(40, 40))) {
                        _projectToDelete = project.Path;
                        _popupToOpen = "DeleteConfirm";
                    }
                    PopFont();
                    PopStyleColor();

                    if (i < Projects.Count - 1) {
                        SetCursorPos(itemStartPos + new Vector2(0, itemHeight));
                        Separator();
                    }
                    PopID();
                }
                Dummy(new Vector2(0, 30)); 
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
        File.WriteAllText(Path.Combine(path, "Project.json"), JsonConvert.SerializeObject(new ProjectConfig { Name = name }, Formatting.Indented));
    }
}
