using System.Numerics;
using Raylib_cs;
using static Raylib_cs.Raylib;

internal static class Core {

    public static readonly List<Level> OpenLevels = [];
    public static int ActiveLevelIndex = -1;
    public static Level? ActiveLevel => ActiveLevelIndex >= 0 && ActiveLevelIndex < OpenLevels.Count ? OpenLevels[ActiveLevelIndex] : null;
    public static Camera3D? ActiveCamera;
    public static Camera3D? GameCamera;
    public static bool IsAnyLevelDirty => OpenLevels.Any(l => l.IsDirty);

    public static void SaveAllDirtyLevels() {
        
        foreach (var level in OpenLevels.Where(level => level.IsDirty)) level.Save();
    }

    public static bool ShouldFocusActiveLevel;
    public static bool IsPreviewRender;
    public static bool IsPlaying;
    public static bool IsLoadingLevel;
    public static bool RuntimeInputEnabled = true;

    public static Matrix4x4 LastProjectionMatrix = Matrix4x4.Identity;
    public static Matrix4x4 LastViewMatrix = Matrix4x4.Identity;

    public static readonly RenderSettings RenderSettings = new();

    private static readonly List<Light> Lights = [];
    private static readonly List<TransparentDrawCall> TransparentRenderQueue = [];

    private static RenderTexture2D _shadowMap;
    private const int ShadowMapResolution = 4096;

    private static Raylib_cs.Model _skyboxModel;
    private static Texture2D _skyboxTexture;
    private static string _loadedSkyboxGuid = "";

    public static unsafe void Init() {

        var useInternalSplash = !CommandLine.NoSplash && !Splash.HasExternalHelper;

        if (useInternalSplash) Splash.RenderSingleFrame();

        // Physics
        Physics.Init();

        // Fonts
        Fonts.Init();

        // Runtime Scripts
        if (CommandLine.Runtime) ScriptCompiler.LoadRuntime();

        // Assets
        AssetManager.Init();

        if (useInternalSplash) Splash.ShowWhileLoading();
        else while (Splash.IsLoading) Tasks.Update();

        // Setup Global PBR Uniforms
        var pbr = AssetManager.Get<ShaderAsset>("Collection/pbr.vs");

        if (pbr != null) {

            SetShaderValue(pbr.Shader, pbr.GetLoc("use_tex_albedo"), !CommandLine.Runtime ? OldConfig.Editor.PbrAlbedo : OldConfig.Runtime.PbrAlbedo, ShaderUniformDataType.Int);
            SetShaderValue(pbr.Shader, pbr.GetLoc("use_tex_normal"), !CommandLine.Runtime ? OldConfig.Editor.PbrNormal : OldConfig.Runtime.PbrNormal, ShaderUniformDataType.Int);
            SetShaderValue(pbr.Shader, pbr.GetLoc("use_tex_mra"), !CommandLine.Runtime ? OldConfig.Editor.PbrMra : OldConfig.Runtime.PbrMra, ShaderUniformDataType.Int);
            SetShaderValue(pbr.Shader, pbr.GetLoc("use_tex_emissive"), !CommandLine.Runtime ? OldConfig.Editor.PbrEmissive : OldConfig.Runtime.PbrEmissive, ShaderUniformDataType.Int);
        }

        _shadowMap = LoadShadowmapRenderTexture(ShadowMapResolution, ShadowMapResolution);

        // Skybox
        var cube = GenMeshCube(1.0f, 1.0f, 1.0f);
        _skyboxModel = LoadModelFromMesh(cube);

        var skybox = AssetManager.Get<ShaderAsset>("Collection/skybox.vs");
        if (skybox != null) _skyboxModel.Materials[0].Shader = skybox.Shader;

        // Level & camera
        if (CommandLine.Runtime) {

            IsPlaying = true;

            var startupLevel = ResolveStartupLevelPath();

            if (!string.IsNullOrWhiteSpace(startupLevel))
                OpenLevel(CollectionData.GetLevelDisplayName(startupLevel), startupLevel);

            BackgroundScripts.Initialize();
        }

        Splash.StopExternalHelper();
        Splash.Cleanup();
    }

    private static RenderTexture2D LoadShadowmapRenderTexture(int width, int height) {

        var target = new RenderTexture2D { Id = Rlgl.LoadFramebuffer() };

        target.Texture.Width = width;
        target.Texture.Height = height;

        if (target.Id <= 0) return target;

        Rlgl.EnableFramebuffer(target.Id);

        target.Depth.Id = Rlgl.LoadTextureDepth(width, height, false);
        target.Depth.Width = width;
        target.Depth.Height = height;
        target.Depth.Format = PixelFormat.UncompressedGrayscale;
        target.Depth.Mipmaps = 1;

        Rlgl.FramebufferAttach(target.Id, target.Depth.Id, FramebufferAttachType.Depth, FramebufferAttachTextureType.Texture2D, 0);

        if (Rlgl.FramebufferComplete(target.Id)) TraceLog(TraceLogLevel.Info, "FBO: Shadowmap created successfully");

        SetTextureFilter(target.Depth, TextureFilter.Bilinear);
        SetTextureWrap(target.Depth, TextureWrap.Clamp);

        Rlgl.DisableFramebuffer();

        return target;
    }

    public static unsafe RenderTexture2D LoadRenderTextureWithDepth(int width, int height) {

        var target = new RenderTexture2D { Id = Rlgl.LoadFramebuffer() };

        target.Texture.Width = width;
        target.Texture.Height = height;
        target.Texture.Format = PixelFormat.UncompressedR8G8B8A8;
        target.Texture.Mipmaps = 1;

        if (target.Id <= 0) return target;

        Rlgl.EnableFramebuffer(target.Id);

        // Color texture
        target.Texture.Id = Rlgl.LoadTexture((void*)0, width, height, PixelFormat.UncompressedR8G8B8A8, 1);
        Rlgl.FramebufferAttach(target.Id, target.Texture.Id, FramebufferAttachType.ColorChannel0, FramebufferAttachTextureType.Texture2D, 0);

        // Depth texture
        target.Depth.Id = Rlgl.LoadTextureDepth(width, height, false);
        target.Depth.Width = width;
        target.Depth.Height = height;
        target.Depth.Format = PixelFormat.UncompressedGrayscale;
        target.Depth.Mipmaps = 1;

        Rlgl.FramebufferAttach(target.Id, target.Depth.Id, FramebufferAttachType.Depth, FramebufferAttachTextureType.Texture2D, 0);

        if (Rlgl.FramebufferComplete(target.Id)) TraceLog(TraceLogLevel.Info, "FBO: RT with depth created successfully");

        SetTextureFilter(target.Texture, TextureFilter.Bilinear);
        SetTextureWrap(target.Texture, TextureWrap.Clamp);
        SetTextureFilter(target.Depth, TextureFilter.Point);
        SetTextureWrap(target.Depth, TextureWrap.Clamp);

        Rlgl.DisableFramebuffer();

        return target;
    }

    public static void OpenLevel(string name, string? path = null) {

        Level? level;

        if (path == null) {

            if (PathUtil.GetPath($"Levels/{name}.lvl", out var levelPath) || PathUtil.GetPath($"{name}.lvl", out levelPath))
                level = new Level(name, levelPath);

            else {

                TraceLog(TraceLogLevel.Error, $"CORE: Could not find level {name}");

                return;
            }

        } else
            level = new Level(name, path);

        var existingIndex = OpenLevels.FindIndex(l => Path.GetFullPath(l.JsonPath).Equals(Path.GetFullPath(level.JsonPath), StringComparison.OrdinalIgnoreCase));

        if (existingIndex != -1) {

            SetActiveLevel(existingIndex);
            ShouldFocusActiveLevel = true;

            return;
        }

        OpenLevels.Add(level);
        SetActiveLevel(OpenLevels.Count - 1);
        ShouldFocusActiveLevel = true;

        Load();
    }

    public static bool SwitchLevel(string levelReference) {

        if (string.IsNullOrWhiteSpace(levelReference)) return false;

        var resolvedPath = ResolveLevelReferencePath(levelReference);
        if (string.IsNullOrWhiteSpace(resolvedPath)) {
            TraceLog(TraceLogLevel.Error, $"CORE: Could not resolve level {levelReference}");
            return false;
        }

        var previousLevel = ActiveLevel;
        OpenLevel(CollectionData.GetLevelDisplayName(resolvedPath), resolvedPath);

        if (previousLevel == null || ReferenceEquals(previousLevel, ActiveLevel)) return false;

        var previousIndex = OpenLevels.IndexOf(previousLevel);
        if (previousIndex >= 0)
            CloseLevel(previousIndex);

        return true;
    }

    private static string? ResolveLevelReferencePath(string levelReference) {

        var guid = levelReference.Replace('\\', '/');
        var path = guid;
        var asset = AssetManager.ResolveReference<LevelAsset>(ref guid, ref path)
            ?? AssetManager.Get<LevelAsset>(levelReference)
            ?? AssetManager.GetOrImport<LevelAsset>(levelReference);
        if (asset != null && File.Exists(asset.File)) return asset.File;

        if (PathUtil.GetPath(levelReference, out var fullPath) && File.Exists(fullPath)) return fullPath;
        if (File.Exists(levelReference)) return Path.GetFullPath(levelReference);

        return null;
    }

    private static string? ResolveStartupLevelPath() {

        var guid = ProjectConfig.Current.StartupLevel?.Replace('\\', '/');
        var path = ProjectConfig.Current.StartupLevelPath?.Replace('\\', '/');

        if (!string.IsNullOrWhiteSpace(guid) || !string.IsNullOrWhiteSpace(path)) {

            var lookupGuid = guid ?? "";
            var lookupPath = path ?? "";
            var levelAsset = AssetManager.ResolveReference<LevelAsset>(ref lookupGuid, ref lookupPath);

            ProjectConfig.Current.StartupLevel = lookupGuid;
            ProjectConfig.Current.StartupLevelPath = lookupPath;

            if (levelAsset is { IsLoaded: true } && File.Exists(levelAsset.File)) return levelAsset.File;
        }

        var firstLevel = FindFirstLevelPath();
        if (!string.IsNullOrWhiteSpace(firstLevel)) return firstLevel;

        return null;
    }

    private static string? FindFirstLevelPath() {

        if (!Directory.Exists(ScytheConfig.Current.Project)) return null;

        return Directory.EnumerateFiles(ScytheConfig.Current.Project, "*", SearchOption.AllDirectories)
            .Where(CollectionData.IsLevel)
            .OrderBy(path => path, new NaturalStringComparer()!)
            .FirstOrDefault();
    }

    public static void SetActiveLevel(int index, bool clearHistory = true) {

        if (index < 0 || index >= OpenLevels.Count) return;
        if (ActiveLevelIndex != index)
            LevelBrowser.SelectObject(null);

        ActiveLevelIndex = index;

        if (ActiveLevel == null) return;

        // Clear history when switching levels to avoid cross-level undo
        if (clearHistory) History.Clear();
        ActiveLevel.Root.Transform.UpdateTransform();

        GameCamera = RootCamera(ActiveLevel.Root);

        if (CommandLine.Runtime || IsPlaying)
            ActiveCamera = GameCamera;

        else
            ActiveCamera = new Camera3D();

        ApplyLevelVisualSettings();

        if (CommandLine.Runtime || IsPlaying)
            BackgroundScripts.ScheduleLevelLoad(ActiveLevel.Root);

        if (CommandLine.Runtime) return;

        if (ActiveLevel.EditorCamera != null) {

            FreeCam.Pos = ActiveLevel.EditorCamera.Position;
            FreeCam.Rot = ActiveLevel.EditorCamera.Rotation;
        } else
            FreeCam.SetFromTarget(ActiveCamera);
    }

    private static Camera3D? RootCamera(Obj obj) {

        if (obj.ChildEntries.TryGetValue("Camera", out var cameraObj) && cameraObj.ComponentEntries.TryGetValue("Camera", out var camComp)) return (camComp as Camera)?.Cam;

        return FindFirstCamera(obj);
    }

    private static Camera3D? FindFirstCamera(Obj obj) {

        if (obj.ComponentEntries.Values.FirstOrDefault(c => c is Camera) is Camera found) return found.Cam;

        return obj.ChildEntries.Values.Select(FindFirstCamera).OfType<Camera3D>().FirstOrDefault();
    }

    public static void CloseLevel(int index) {

        if (index < 0 || index >= OpenLevels.Count) return;

        var level = OpenLevels[index];
        var wasActiveLevel = index == ActiveLevelIndex;

        level.Root.Dispose();
        OpenLevels.RemoveAt(index);

        if (ActiveLevelIndex > index)
            ActiveLevelIndex--;

        if (wasActiveLevel && ActiveLevelIndex >= OpenLevels.Count)
            ActiveLevelIndex = OpenLevels.Count - 1;

        if (ActiveLevelIndex >= 0)
            SetActiveLevel(ActiveLevelIndex);
        else {
            ActiveCamera = null;
            ApplyLevelVisualSettings();
        }
    }

    public static void MoveLevel(int fromIndex, int toIndex) {

        if (fromIndex < 0 || fromIndex >= OpenLevels.Count) return;
        if (toIndex < 0 || toIndex > OpenLevels.Count) return;
        if (fromIndex == toIndex || fromIndex + 1 == toIndex) return;

        var level = OpenLevels[fromIndex];
        OpenLevels.RemoveAt(fromIndex);
        if (toIndex > fromIndex) toIndex--;
        OpenLevels.Insert(toIndex, level);

        if (ActiveLevelIndex == fromIndex)
            ActiveLevelIndex = toIndex;
        else if (ActiveLevelIndex > fromIndex && ActiveLevelIndex <= toIndex)
            ActiveLevelIndex--;
        else if (ActiveLevelIndex < fromIndex && ActiveLevelIndex >= toIndex)
            ActiveLevelIndex++;
    }

    public static Color GetActiveBackgroundColor() => ActiveLevel?.BackgroundColor ?? Colors.Game;

    public static unsafe void ApplyLevelVisualSettings() {

        if (ActiveLevel == null) {
            RenderSettings.AmbientColor = Color.White;
            UnloadSkyboxTexture();
            return;
        }

        var guid = ActiveLevel.Skybox ?? "";
        var path = ActiveLevel.SkyboxPath ?? "";
        var textureAsset = AssetManager.ResolveReference<TextureAsset>(ref guid, ref path);
        ActiveLevel.Skybox = guid;
        ActiveLevel.SkyboxPath = path;

        RenderSettings.AmbientColor = ActiveLevel.AmbientColor;

        if (textureAsset is not { IsLoaded: true } || string.IsNullOrWhiteSpace(textureAsset.GUID)) {
            UnloadSkyboxTexture();
            return;
        }

        var isSameSkyboxLoaded = string.Equals(_loadedSkyboxGuid, textureAsset.GUID, StringComparison.OrdinalIgnoreCase) && _skyboxTexture.Id != 0;

        if (isSameSkyboxLoaded) {
            if (ActiveLevel.SkyboxAmbientEnabled) {
                var ambientImage = LoadImage(textureAsset.File);
                if (ambientImage.Data != null) {
                    RenderSettings.AmbientColor = GetSkyboxAmbientColor(ambientImage, ActiveLevel.SkyboxAmbientIntensity);
                    UnloadImage(ambientImage);
                }
            }

            return;
        }

        UnloadSkyboxTexture();

        var image = LoadImage(textureAsset.File);
        if (image.Data == null) return;

        _skyboxTexture = LoadTextureCubemap(image, CubemapLayout.AutoDetect);
        if (ActiveLevel.SkyboxAmbientEnabled)
            RenderSettings.AmbientColor = GetSkyboxAmbientColor(image, ActiveLevel.SkyboxAmbientIntensity);

        UnloadImage(image);
        if (_skyboxModel.Materials == null || _skyboxModel.MaterialCount <= 0) {
            _loadedSkyboxGuid = textureAsset.GUID;
            return;
        }

        _skyboxModel.Materials[0].Maps[(int)MaterialMapIndex.Cubemap].Texture = _skyboxTexture;
        _loadedSkyboxGuid = textureAsset.GUID;
    }

    private static unsafe Color GetSkyboxAmbientColor(Image image, float intensity) {

        intensity = Math.Clamp(intensity, 0.0f, 1.0f);
        var colors = LoadImageColors(image);
        if (colors == null) return Color.White;

        try {
            var pixelCount = image.Width * image.Height;
            if (pixelCount <= 0) return Color.White;

            long totalR = 0;
            long totalG = 0;
            long totalB = 0;

            for (var i = 0; i < pixelCount; i++) {
                totalR += colors[i].R;
                totalG += colors[i].G;
                totalB += colors[i].B;
            }

            var avgR = (float)totalR / pixelCount;
            var avgG = (float)totalG / pixelCount;
            var avgB = (float)totalB / pixelCount;

            return new Color(
                (byte)Math.Clamp(avgR * intensity, 0f, 255f),
                (byte)Math.Clamp(avgG * intensity, 0f, 255f),
                (byte)Math.Clamp(avgB * intensity, 0f, 255f),
                (byte)255
            );

        } finally {
            UnloadImageColors(colors);
        }
    }

    private static unsafe void UnloadSkyboxTexture() {

        if (_skyboxTexture.Id != 0) {
            UnloadTexture(_skyboxTexture);
            _skyboxTexture = default;
        }

        if (_skyboxModel.Materials != null && _skyboxModel.MaterialCount > 0)
            _skyboxModel.Materials[0].Maps[(int)MaterialMapIndex.Cubemap].Texture = default;

        _loadedSkyboxGuid = "";
    }

    public static void Load() {

        var activeLevel = ActiveLevel;
        if (activeLevel == null) return;

        IsLoadingLevel = true;

        var anyLoaded = false;

        try {
            LoadObj(activeLevel.Root);
        } finally {
            IsLoadingLevel = false;
        }

        if (anyLoaded)
            ReapplyScriptFields(activeLevel.Root);

        if (CommandLine.Runtime || IsPlaying)
            BackgroundScripts.HandlePendingLevelLoad();

        return;

        void LoadObj(Obj obj) {

            foreach (var component in obj.ComponentEntries.Values) {

                if (!component.IsLoaded && component.Load()) {
                    component.IsLoaded = true;
                    anyLoaded = true;
                }
            }

            foreach (var child in obj.ChildEntries.Values.ToArray()) LoadObj(child);
        }

        static void ReapplyScriptFields(Obj obj) {

            foreach (var component in obj.ComponentEntries.Values)
                if (component is Script script)
                    script.ReapplyStoredFieldValues();

            foreach (var child in obj.ChildEntries.Values)
                ReapplyScriptFields(child);
        }
    }

    public static void Logic() {

        Tasks.Update();
        AssetManager.Update();

        if (ActiveLevel == null) return;

        // Robust loading of any newly added/restored components
        Load();

        if ((IsPlaying || CommandLine.Runtime) && ScriptCompiler.ConsumePendingPlayModeRefresh())
            RefreshPlayModeAfterScriptHotReload();

        Lights.Clear();
        TransparentRenderQueue.Clear();

        var shouldRunRuntimeLogic = CommandLine.Runtime || !IsPlaying || RuntimeInputEnabled;

        if ((IsPlaying || CommandLine.Runtime) && shouldRunRuntimeLogic) Physics.Update();

        // Behavior & Logic (Scripts, Physics Sync) This pass determines WHERE objects want to be in this frame.
        if (shouldRunRuntimeLogic) {
            RunLogic(ActiveLevel.Root);

            if (IsPlaying || CommandLine.Runtime)
                BackgroundScripts.Logic(Raylib.GetFrameTime());
        }

        // Hierarchy Sync & Visuals (World Matrices + Cartoon Bounce) his pass settles the physical truth and prepares the visual state for rendering.
        SyncHierarchy(ActiveLevel.Root);

        // Update global pbr parameters after everything is settled
        var pbr = AssetManager.Get<ShaderAsset>("Collection/pbr.vs");
        if (pbr != null) SetShaderValue(pbr.Shader, pbr.GetLoc("view_pos"), ActiveCamera?.Position ?? Vector3.Zero, ShaderUniformDataType.Vec3);
    }

    private static void RefreshPlayModeAfterScriptHotReload() {

        BackgroundScripts.RefreshAfterHotReload();

        foreach (var level in OpenLevels) {
            StartScripts(level.Root);
        }
    }

    private static void StartScripts(Obj obj) {

        foreach (var component in obj.ComponentEntries.Values) {
            if (component is Script script && script.IsLoaded) script.EnsureStarted();
        }

        foreach (var child in obj.ChildEntries.Values.ToArray()) StartScripts(child);
    }

    private static void RunLogic(Obj obj) {

        // Priority: Rigidbodies must sync physics to transform before scripts run
        if (obj.ComponentEntries.TryGetValue("Rigidbody", out var rb) && rb.IsLoaded) rb.Logic();

        foreach (var component in obj.ComponentEntries.Values) {

            if (component is Rigidbody) continue;

            if (component.IsLoaded) component.Logic();
        }

        foreach (var child in obj.ChildEntries.Values.ToArray()) RunLogic(child);
    }

    private static void SyncHierarchy(Obj obj) {

        // Physical World Sync (Top-Down)
        if (obj.Parent != null) {

            obj.WorldMatrix = obj.Parent.WorldMatrix * obj.Matrix;
            obj.WorldRotMatrix = obj.Parent.WorldRotMatrix * obj.RotMatrix;

        } else {

            obj.WorldMatrix = obj.Matrix;
            obj.WorldRotMatrix = obj.RotMatrix;
        }

        // Visual State Sync
        if (IsPlaying || CommandLine.Runtime) {

            // Runtime: Instant sync for zero lag (Physics etc.)
            obj.VisualWorldMatrix = obj.WorldMatrix;

        } else {

            // Handle Cartoon Bounce Start with a clean inherited baseline
            obj.VisualWorldMatrix = obj.Parent != null ? obj.Parent.VisualWorldMatrix * obj.Matrix : obj.WorldMatrix;

            // If this is the selected object or undergoing local bounce, UpdateCartoon will override it
            obj.Transform.UpdateCartoon();
        }

        // 3. Collection & Preparation
        foreach (var component in obj.ComponentEntries.Values) {

            if (!component.IsLoaded) continue;

            switch (component) {

                case Light light: Lights.Add(light); break;

                case Model { IsTransparent: true } model: {

                    var worldPos = new Vector3(obj.WorldMatrix.M41, obj.WorldMatrix.M42, obj.WorldMatrix.M43);
                    var distance = Vector3.Distance(ActiveCamera?.Position ?? Vector3.Zero, worldPos);
                    TransparentRenderQueue.Add(new TransparentDrawCall { Model = model, Distance = distance });

                    break;
                }
            }
        }

        foreach (var child in obj.ChildEntries.Values.ToArray()) SyncHierarchy(child);
    }

    public static void ShadowPass(Camera3D? overrideCamera = null) {

        if (ActiveLevel == null) return;

        var renderCamera = overrideCamera ?? ActiveCamera;

        if (renderCamera == null) return;

        var pbr = AssetManager.Get<ShaderAsset>("Collection/pbr.vs");

        if (pbr == null) return;

        SetShaderValue(pbr.Shader, pbr.GetLoc("light_count"), Lights.Count, ShaderUniformDataType.Int);

        var shadowLight = Lights.FirstOrDefault(l => l is { Enabled: true, Shadows: true });

        if (shadowLight != null) {

            var shadowLightIndex = Lights.IndexOf(shadowLight);

            var pos = shadowLight.Obj.Transform.WorldPos;
            var fwd = shadowLight.Obj.Fwd;

            var lightCamera = new Raylib_cs.Camera3D {
                Position = shadowLight.Type == 0 ? pos - fwd * 500.0f : pos,
                Target = shadowLight.Type switch {

                    0 => pos,
                    1 => pos + Vector3.UnitY * -1,
                    _ => pos + fwd
                },
                Up = shadowLight.Type == 1 ? Vector3.UnitX : Vector3.UnitY,
                FovY = (shadowLight.Type == 0 ? shadowLight.Range * 2.0f : shadowLight.Type == 2 ? 90.0f : 160.0f) * RenderSettings.ShadowFovScale,
                Projection = shadowLight.Type == 0 ? CameraProjection.Orthographic : CameraProjection.Perspective
            };

            BeginTextureMode(_shadowMap);
            ClearBackground(Color.White);
            BeginMode3D(lightCamera);

            var lightView = Rlgl.GetMatrixModelview();
            var lightProj = Rlgl.GetMatrixProjection();
            var lightVp = Raymath.MatrixMultiply(lightView, lightProj);

            // Draw objects for shadow depth
            RenderHierarchy(ActiveLevel.Root, false, true);

            EndMode3D();
            EndTextureMode();

            SetShaderValueMatrix(pbr.Shader, pbr.GetLoc("lightVP"), lightVp);
            SetShaderValue(pbr.Shader, pbr.GetLoc("shadow_light_index"), shadowLightIndex, ShaderUniformDataType.Int);
            SetShaderValue(pbr.Shader, pbr.GetLoc("shadow_strength"), shadowLight.ShadowStrength, ShaderUniformDataType.Float);
            SetShaderValue(pbr.Shader, pbr.GetLoc("shadow_bias"), RenderSettings.ShadowBias, ShaderUniformDataType.Float);
            SetShaderValue(pbr.Shader, pbr.GetLoc("shadow_map_resolution"), ShadowMapResolution, ShaderUniformDataType.Int);

            const int shadowMapSlot = 10;
            Rlgl.ActiveTextureSlot(shadowMapSlot);
            Rlgl.EnableTexture(_shadowMap.Depth.Id);
            SetShaderValue(pbr.Shader, pbr.GetLoc("shadowMap"), shadowMapSlot, ShaderUniformDataType.Int);
            Rlgl.ActiveTextureSlot(0);

        } else
            SetShaderValue(pbr.Shader, pbr.GetLoc("shadow_light_index"), -1, ShaderUniformDataType.Int);

        for (var i = 0; i < Lights.Count; i++) Lights[i].Update(i);
    }

    public static unsafe void Render(bool is2D) {

        var activeLevel = ActiveLevel;
        if (activeLevel == null) return;

        if (!is2D) {

            if (_skyboxTexture.Id != 0) {
                Rlgl.DisableBackfaceCulling();
                Rlgl.DisableDepthMask();
                var skyboxShader = _skyboxModel.Materials[0].Shader;
                var skyboxTintLoc = GetShaderLocation(skyboxShader, "skyboxTint");
                if (skyboxTintLoc != -1) SetShaderValue(skyboxShader, skyboxTintLoc, ColorNormalize(activeLevel.SkyboxTint), ShaderUniformDataType.Vec4);
                DrawModel(_skyboxModel, Vector3.Zero, 1.0f, activeLevel.SkyboxTint);
                Rlgl.EnableBackfaceCulling();
                Rlgl.EnableDepthMask();
            }
        }

        RenderHierarchy(activeLevel.Root, is2D, false);

        if (!is2D) {

            if (TransparentRenderQueue.Count > 0) {

                TransparentRenderQueue.Sort((a, b) => b.Distance.CompareTo(a.Distance));

                Rlgl.DisableDepthMask();
                BeginBlendMode(BlendMode.Alpha);

                foreach (var call in TransparentRenderQueue) call.Model.Draw(0.0f);

                EndBlendMode();
                Rlgl.EnableDepthMask();
            }
        }
    }

    private static void RenderHierarchy(Obj obj, bool is2D, bool isShadowPass) {

        // Ensure components drawing. Transform is updated in Logic
        if (isShadowPass) {

            foreach (var component in obj.ComponentEntries.Values) {

                if (component is Model { CastShadows: true, IsLoaded: true } m) m.DrawShadow();
            }

        } else {

            if (is2D)
                obj.Transform.Render2D();
            else
                obj.Transform.Render3D();

            foreach (var component in obj.ComponentEntries.Values) {

                if (!component.IsLoaded) continue;

                if (is2D)
                    component.Render2D();
                else
                    component.Render3D();
            }
        }

        foreach (var child in obj.ChildEntries.Values.ToArray()) RenderHierarchy(child, is2D, isShadowPass);
    }

    private static RenderTexture2D _mainRt;

    public static void Step() {

        Logic();
        ShadowPass();

        if (CommandLine.Runtime) {

            if (_mainRt.Texture.Width != GetScreenWidth() || _mainRt.Texture.Height != GetScreenHeight()) {

                if (_mainRt.Texture.Id != 0) UnloadRenderTexture(_mainRt);
                _mainRt = LoadRenderTextureWithDepth(GetScreenWidth(), GetScreenHeight());
            }

            // 3D Pass
            BeginTextureMode(_mainRt);
            ClearBackground(GetActiveBackgroundColor());

            var renderCamera = ActiveCamera;

            if (renderCamera != null) {
                BeginMode3D(renderCamera.Raylib);
                PostProcessing.ApplyJitter(renderCamera);
                LastProjectionMatrix = Rlgl.GetMatrixProjection();
                LastViewMatrix = Rlgl.GetMatrixModelview();
                Render(false);
                EndMode3D();
            }

            EndTextureMode();

            // Post-Process Pass
            PostProcessing.Apply(_mainRt);

            // 2D Pass
            BeginTextureMode(_mainRt);
            Render(true);
            EndTextureMode();

            DrawTextureRec(_mainRt.Texture, new Rectangle(0, 0, _mainRt.Texture.Width, -_mainRt.Texture.Height), Vector2.Zero, Color.White);

        } else {

            RenderAll();
        }
    }

    private static void RenderAll(Camera3D? overrideCamera = null) {

        var renderCamera = overrideCamera ?? ActiveCamera;

        if (renderCamera != null) {

            BeginMode3D(renderCamera.Raylib);
            Render(false);
            EndMode3D();
        }

        Render(true);
    }

    public static void Quit() {

        CloseAudioDevice();

        Fonts.UnloadRlFonts();
        AssetManager.UnloadAll();
        BackgroundScripts.Shutdown();

        if (ActiveLevel == null) return;

        UnloadRenderTexture(_shadowMap);
        UnloadRenderTexture(_mainRt);
        UnloadSkyboxTexture();
        UnloadModel(_skyboxModel);
        PostProcessing.Shutdown();

        QuitObj(ActiveLevel.Root);

        return;

        void QuitObj(Obj obj) {

            obj.Transform.Quit();

            foreach (var component in obj.ComponentEntries.Values) {

                if (component.IsLoaded) component.Unload();

                component.Quit();
            }

            foreach (var child in obj.ChildEntries.Values.ToArray()) QuitObj(child);
        }
    }
}
