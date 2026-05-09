using System.Numerics;
using System.Runtime.InteropServices;

using Viewports;

using Raylib_cs;
using static Raylib_cs.Raylib;
using static Raylib_cs.Rlgl;

using rlImGui_cs;
using static rlImGui_cs.rlImGui;

using ImGuiNET;
using static ImGuiNET.ImGui;

internal static unsafe class Editor {

    private static bool _scheduledQuit;
    private static bool _showExitModal;
    private static Camera3D _editorCamera = null!;

    // ReSharper disable MemberCanBePrivate.Global
    public static EditorRender EditorRender = null!;
    public static LevelBrowser LevelBrowser = null!;
    public static ObjectBrowser ObjectBrowser = null!;
    public static Preview Preview = null!;
    public static RuntimeRender RuntimeRender = null!;
    public static Collections Collections = null!;
    // ReSharper restore MemberCanBePrivate.Global

    private static Level? _editorLevelRef;
    public static string? SelectedAssetPath { get; private set; }
    public static bool ProjectSettingsSelected { get; private set; }

    public static void OpenScript(string path) => throw new NotImplementedException(Ansi.ErrorMessage("Script editor"));

    public static void OpenLevel(string path) {

        var name = CollectionData.GetLevelDisplayName(path);
        Core.OpenLevel(name, path);
    }

    public static void CreateLevel(string path) {

        var name = CollectionData.GetLevelDisplayName(path);
        var level = new Level(name, path, false);

        Core.OpenLevels.Add(level);
        Core.SetActiveLevel(Core.OpenLevels.Count - 1);
        level.Save();
        Core.Load();
    }

    public static void SetSelectedAsset(string? path) {

        AssetManager.EnsureImported(path);
        SelectedAssetPath = path;
        ProjectSettingsSelected = false;
        Collections.SyncExternalSelection(path);
    }

    public static void SelectProjectSettings() {

        LevelBrowser.SelectObject(null);
        SelectedAssetPath = null;
        Collections.SyncExternalSelection(null);
        ProjectSettingsSelected = true;
    }

    public static void OnDocumentPathMoved(string oldPath, string newPath) {

        var oldFullPath = Path.GetFullPath(oldPath);
        var newFullPath = Path.GetFullPath(newPath);

        for (var i = 0; i < Core.OpenLevels.Count; i++) {

            var level = Core.OpenLevels[i];
            if (!Path.GetFullPath(level.JsonPath).Equals(oldFullPath, StringComparison.OrdinalIgnoreCase)) continue;

            var snapshot = level.ToSnapshot();
            File.WriteAllText(newFullPath, snapshot);

            var reloaded = new Level(CollectionData.GetLevelDisplayName(newFullPath), newFullPath) {
                IsDirty = level.IsDirty
            };

            Core.OpenLevels[i] = reloaded;

            if (ReferenceEquals(_editorLevelRef, level)) _editorLevelRef = reloaded;
            if (Core.ActiveLevelIndex == i) Core.SetActiveLevel(i, clearHistory: false);
        }
    }

    public static void OnCollectionPathMoved(string oldPath, string newPath) {

        var oldFullPath = Path.GetFullPath(oldPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var newFullPath = Path.GetFullPath(newPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        for (var i = 0; i < Core.OpenLevels.Count; i++) {

            var level = Core.OpenLevels[i];
            var levelPath = Path.GetFullPath(level.JsonPath);
            if (!levelPath.StartsWith(oldFullPath, StringComparison.OrdinalIgnoreCase)) continue;

            var movedPath = Path.GetFullPath(newFullPath + levelPath[oldFullPath.Length..]);
            var snapshot = level.ToSnapshot();
            File.WriteAllText(movedPath, snapshot);

            var reloaded = new Level(CollectionData.GetLevelDisplayName(movedPath), movedPath) {
                IsDirty = level.IsDirty
            };

            Core.OpenLevels[i] = reloaded;

            if (ReferenceEquals(_editorLevelRef, level)) _editorLevelRef = reloaded;
            if (Core.ActiveLevelIndex == i) Core.SetActiveLevel(i, clearHistory: false);
        }
    }

    public static void Show() {

        Window.Show(flags: [ConfigFlags.Msaa4xHint, ConfigFlags.ResizableWindow], title: $"{ProjectConfig.Current.Name} - Editor");

        Setup(true, true);

        EditorRender = new EditorRender { CustomStyle = new CustomStyle { WindowPadding = new Vector2(0, 0), CellPadding = new Vector2(0, 0), SeparatorTextPadding = new Vector2(0, 0) } };
        LevelBrowser = new LevelBrowser();
        ObjectBrowser = new ObjectBrowser();
        Preview = new Preview();
        RuntimeRender = new RuntimeRender();
        Collections = new Collections();

        PathUtil.ValidateFile("Layouts/User.ini", out var layoutPath);

        GetIO().ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        GetIO().NativePtr->IniFilename = (byte*)Marshal.StringToHGlobalAnsi(layoutPath).ToPointer();

        // Setup core
        Core.Init();
        _editorCamera = new Camera3D();
        FreeCam.SetFromTarget(Core.ActiveCamera);

        EditorRender.Load();

        ViewSettings.Load();

        Core.Load();

        var shouldClose = false;

        while (!shouldClose) {

            if (WindowShouldClose() || _scheduledQuit) {

                if (Core.IsAnyLevelDirty) {

                    _showExitModal = true;
                    _scheduledQuit = false;

                } else {

                    shouldClose = true;
                }
            }

            Window.UpdateFps();

            if (Core.ActiveLevel == null || Core.ActiveCamera == null) {

                BeginDrawing();
                ClearBackground(Color.Black);
                Begin();
                Style.Push();
                PushFont(Fonts.ImMontserratRegular);
                DockSpaceOverViewport(GetMainViewport().ID);

                MenuBar.Draw();
                EditorRender.Draw();
                Preview.Draw();

                PopFont();
                Style.Pop();

                DrawExitModal();

                rlImGui.End();
                Notifications.Draw();
                EndDrawing();

                if (_scheduledQuit) break;

                continue;
            }

            BeginDrawing();

            // Run logic inside BeginDrawing for timing - before rlImGui to avoid input/state conflicts
            Core.ActiveCamera = Core.IsPlaying ? Core.GameCamera : _editorCamera;
            Core.Logic();
            Core.ShadowPass();

            ClearBackground(Color.Black);
            Begin();
            Style.Push();
            PushFont(Fonts.ImMontserratRegular);

            DockSpaceOverViewport(GetMainViewport().ID);
            GetIO().MouseDoubleClickTime = 0.2f;

            // Handle Editor UI Lock when playing with mouse locked
            if (IsCursorHidden()) {
                GetIO().ConfigFlags |= ImGuiConfigFlags.NoMouse;
                GetIO().ConfigFlags |= ImGuiConfigFlags.NoKeyboard;
            } else {
                GetIO().ConfigFlags &= ~ImGuiConfigFlags.NoMouse;
                GetIO().ConfigFlags &= ~ImGuiConfigFlags.NoKeyboard;
            }

            // Reload Viewport Textures if Resized
            if (EditorRender.TexSize != EditorRender.TexTemp) {
                UnloadRenderTexture(EditorRender.Rt);
                EditorRender.Rt = Core.LoadRenderTextureWithDepth((int)EditorRender.TexSize.X, (int)EditorRender.TexSize.Y);

                UnloadRenderTexture(EditorRender.OutlineRt);
                EditorRender.OutlineRt = LoadRenderTexture((int)EditorRender.TexSize.X, (int)EditorRender.TexSize.Y);
                SetTextureWrap(EditorRender.OutlineRt.Texture, TextureWrap.Clamp);

                EditorRender.TexTemp = EditorRender.TexSize;
            }

            if (RuntimeRender.TexSize != RuntimeRender.TexTemp) {
                UnloadRenderTexture(RuntimeRender.Rt);
                RuntimeRender.Rt = Core.LoadRenderTextureWithDepth((int)RuntimeRender.TexSize.X, (int)RuntimeRender.TexSize.Y);

                RuntimeRender.TexTemp = RuntimeRender.TexSize;
            }

            // Outline mask pass
            if (LevelBrowser.SelectedObject != null || Picking.DragSource != null || Picking.DragTarget != null) {
                BeginTextureMode(EditorRender.OutlineRt);
                ClearBackground(Color.Blank);
                ClearScreenBuffers();
                BeginMode3D(_editorCamera.Raylib);
                foreach (var obj in LevelBrowser.SelectedObjects) RenderOutline(obj);
                if (Picking.DragSource != null) RenderOutline(Picking.DragSource);
                if (Picking.DragTarget != null) RenderOutline(Picking.DragTarget);
                EndMode3D();
                EndTextureMode();
            }

            // Runtime viewport
            BeginTextureMode(RuntimeRender.Rt);
            ClearBackground(Core.GetActiveBackgroundColor());
            Core.IsPreviewRender = true;

            // 3D Pass
            if (Core.GameCamera != null) {
                BeginMode3D(Core.GameCamera.Raylib);
                PostProcessing.ApplyJitter(Core.GameCamera);
                Core.LastProjectionMatrix = GetMatrixProjection();
                Core.LastViewMatrix = GetMatrixModelview();
                Core.Render(false);
                EndMode3D();
            }

            EndTextureMode();

            // Post-Process Pass
            PostProcessing.Apply(RuntimeRender.Rt);

            // 2D Pass
            BeginTextureMode(RuntimeRender.Rt);
            Core.Render(true);
            Core.IsPreviewRender = false;
            EndTextureMode();

            // Editor viewport
            BeginTextureMode(EditorRender.Rt);
            ClearBackground(Core.GetActiveBackgroundColor());

            Core.ActiveCamera = _editorCamera;
            FreeCam.Loop(EditorRender);

            Camera.ApplySettings(_editorCamera, 0.01f, 2000.0f);
            BeginMode3D(_editorCamera.Raylib);
            Core.LastProjectionMatrix = GetMatrixProjection();
            Core.LastViewMatrix = GetMatrixModelview();
            Core.Render(false);
            Grid.Draw(_editorCamera);
            EndMode3D();

            // Post-process outline
            if ((LevelBrowser.SelectedObject != null || Picking.IsDragging) && !LevelBrowser.IsReorderingObject) {
                var outlinePost = AssetManager.Get<ShaderAsset>("Collection/outline_post.vs");

                if (outlinePost != null) {
                    BeginShaderMode(outlinePost.Shader);
                    SetShaderValue(outlinePost.Shader, outlinePost.GetLoc("textureSize"), new Vector2(EditorRender.TexSize.X, EditorRender.TexSize.Y), ShaderUniformDataType.Vec2);
                    SetShaderValue(outlinePost.Shader, outlinePost.GetLoc("outlineSize"), 2.0f, ShaderUniformDataType.Float);
                    SetShaderValue(outlinePost.Shader, outlinePost.GetLoc("outlineColor"), ColorNormalize(Colors.Primary), ShaderUniformDataType.Vec4);
                    DrawTextureRec(EditorRender.OutlineRt.Texture, new Rectangle(0, 0, EditorRender.TexSize.X, -EditorRender.TexSize.Y), Vector2.Zero, Color.White);
                    EndShaderMode();
                }
            }

            Core.Render(true); // 2D Icons/Gizmos
            Picking.Render2D();
            EndTextureMode();

            // ImGui
            MenuBar.Draw();
            EditorRender.Draw();
            RuntimeRender.Draw();
            LevelBrowser.Draw();
            ObjectBrowser.Draw();
            Preview.Draw();
            Collections.Draw();

            Picking.Update();

            // END
            PopFont();
            Style.Pop();

            DrawExitModal();

            rlImGui.End();

            Notifications.Draw();
            EndDrawing();

            Shortcuts.Check();

            if (_scheduledQuit) break;
        }

        ViewSettings.Save();
        EditorRender.Save();

        Shutdown();
        Core.Quit();
    }

    public static void Quit() => _scheduledQuit = true;

    private static void DrawExitModal() {

        if (!_showExitModal) return;

        OpenPopup("Unsaved Changes###SaveExitModal");
        Style.Push();
        PushFont(Fonts.ImMontserratRegular);

        if (Modal.Begin("Unsaved Changes###SaveExitModal", ref _showExitModal)) {

            Text("You have unsaved changes in your scripts or scenes.");
            Text("Would you like to save them before exiting?");
            Spacing();
            Spacing();

            if (Button("Save All & Exit", new Vector2(160, 40))) {

                Core.SaveAllDirtyLevels();
                _scheduledQuit = true;
                _showExitModal = false;
                CloseCurrentPopup();
            }

            SameLine();

            if (Button("Discard", new Vector2(100, 40))) {

                _scheduledQuit = true;
                _showExitModal = false;
                CloseCurrentPopup();
            }

            SameLine();

            if (Button("Cancel", new Vector2(100, 40))) {

                _showExitModal = false;
                CloseCurrentPopup();
            }
            Modal.End();
        }

        PopFont();
        Style.Pop();
    }

    public static void TogglePlayMode(Vector2? mouseCenter = null) {
        
        if (Core.ActiveLevel == null) return;

        if (!Core.IsPlaying) {
            // Isolate editor state
            _editorLevelRef = Core.ActiveLevel;
            var snapshot = Core.ActiveLevel.ToSnapshot();

            Core.IsPlaying = true;
            DisableCursor();
            RuntimeRender.IsOpen = true;
            RuntimeRender.ShouldFocus = true;

            // Re-init physics to clear any leftovers and prepare for fresh simulation
            Physics.Init();

            // Replace the level reference in the active slot with a runtime clone
            Core.OpenLevels[Core.ActiveLevelIndex] = new Level(_editorLevelRef.Name, _editorLevelRef.JsonPath, snapshot);
            Core.SetActiveLevel(Core.ActiveLevelIndex, clearHistory: false);
            Core.Load();

            if (mouseCenter.HasValue) SetMousePosition((int)mouseCenter.Value.X, (int)mouseCenter.Value.Y);

            Notifications.Show("Play Mode Started");
        } else {
            // Stop play mode
            Core.IsPlaying = false;
            EnableCursor();
            ShowCursor();

            // Re-init physics to clear runtime bodies
            Physics.Init();

            // Restore - Undo history holds references to objects in _editorLevelRef.
            if (_editorLevelRef != null) {
                Core.OpenLevels[Core.ActiveLevelIndex] = _editorLevelRef;
                Core.SetActiveLevel(Core.ActiveLevelIndex, clearHistory: false);

                // Force reload rigidbodies because Physics World was reset
                ReloadPhysics(_editorLevelRef.Root);

                _editorLevelRef = null;
            }

            Notifications.Show("Play Mode Stopped");
        }

        Core.ActiveCamera = Core.IsPlaying ? Core.GameCamera : _editorCamera;
    }

    private static void ReloadPhysics(Obj obj) {
        if (obj.Components.TryGetValue("Rigidbody", out var rb)) {
            rb.IsLoaded = false;
            rb.Load();
            rb.IsLoaded = true;
        }

        foreach (var child in obj.Children.Values) ReloadPhysics(child);
    }

    private static void RenderOutline(Obj obj) {
        foreach (var component in obj.Components.Values) {
            if (component is not Model { IsLoaded: true } model) continue;

            // Override shaders
            var modelAsset = model.AssetRef;
            var outlineMask = AssetManager.Get<ShaderAsset>("Collection/outline_mask.vs");

            if (outlineMask != null) {
                // Track original shaders by Material index to handle shared materials correctly
                var originalShaders = new Dictionary<int, Shader>();

                for (var i = 0; i < modelAsset.Materials.Length; i++) {
                    originalShaders[i] = modelAsset.Materials[i].Shader;
                    modelAsset.Materials[i].Shader = outlineMask.Shader;
                }

                model.Draw();

                // Restore
                for (var i = 0; i < modelAsset.Materials.Length; i++)
                    if (originalShaders.TryGetValue(i, out var shader))
                        modelAsset.Materials[i].Shader = shader;
            } else
                model.Draw();
        }

        foreach (var child in obj.Children.Values) RenderOutline(child);
    }
}
