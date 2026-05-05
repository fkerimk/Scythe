using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using static Raylib_cs.Raylib;
using static ImGuiNET.ImGui;

internal class Preview : Viewport {

    public Preview() : base("Preview") { CustomStyle = new CustomStyle { WindowPadding = Vector2.Zero }; }

    private static Mesh? _previewSphere;
    private static Mesh? _previewCube;
    private static Mesh? _previewPlane;
    private static Mesh? _previewCylinder;
    private static Mesh? _previewTorus;

    private static readonly string[] PrimitiveNames = ["Sphere", "Cube", "Plane", "Cylinder", "Torus"];

    private int _currentPrimitiveIndex;
    private int _currentAnimationIndex;
    private double _animationTime;

    private RenderTexture2D _rt;
    private string _lastFile = "";

    // Interactive Preview State
    private float _zoom = 1.0f;
    private Vector2 _pan = Vector2.Zero;
    private Vector2 _rotation = new(45.0f, 45.0f);
    private float _distance = 2.5f;

    protected override void OnDraw() {

        var selectedFile = Editor.SelectedAssetPath;
        var selectedCamera = LevelBrowser.SelectedObject?.Components.GetValueOrDefault("Camera") as Camera;

        if (selectedCamera == null && string.IsNullOrEmpty(selectedFile)) {

            _lastFile = "";

            BeginChild("##empty");
            TextDisabled("Select an asset or a camera to preview");
            EndChild();

            return;
        }

        var avail = GetContentRegionAvail();

        if (avail.X <= 1 || avail.Y <= 1) return;

        // Reset view when switching assets (only if no camera is selected)
        if (selectedCamera == null && selectedFile != _lastFile) {

            _pan = Vector2.Zero;
            _rotation = new Vector2(45.0f, 45.0f);
            _currentAnimationIndex = 0;
            _animationTime = 0;

            var textureAsset = AssetManager.Get<TextureAsset>(selectedFile);
            var matAsset = AssetManager.Get<MaterialAsset>(selectedFile);
            var modelAsset = AssetManager.Get<ModelAsset>(selectedFile);

            if (textureAsset is { IsLoaded: true }) {

                var tw = (float)textureAsset.Texture.Width;
                var th = (float)textureAsset.Texture.Height;
                _zoom = Math.Min(avail.X / tw, avail.Y / th) * 0.9f;
                _distance = 2.5f;

            } else {

                _zoom = 1.0f;
                var asset = (Asset?)matAsset ?? modelAsset;
                if (asset != null) _distance = GetAssetAutoDistance(asset, out _) * 1.6f;
            }

            _lastFile = selectedFile ?? "";
        }

        // Ensure RT matches window size
        if (_rt.Texture.Width != (int)avail.X || _rt.Texture.Height != (int)avail.Y) {

            if (_rt.Texture.Id != 0) UnloadRenderTexture(_rt);
            _rt = LoadRenderTexture((int)avail.X, (int)avail.Y);
        }

        BeginTextureMode(_rt);
        ClearBackground(new Color(25, 25, 25, 255));

        if (selectedCamera != null) {

            Core.IsPreviewRender = true;
            Camera.ApplySettings(selectedCamera.Cam, selectedCamera.NearClip, selectedCamera.FarClip);

            BeginMode3D(selectedCamera.Cam.Raylib);
            Core.Render(false);
            EndMode3D();

            EndTextureMode(); // Exit RT mode to apply post-process
            PostProcessing.Apply(_rt);
            BeginTextureMode(_rt); // Re-enter for consistency if needed or just end

            Core.IsPreviewRender = false;

        } else {

            var textureAsset = AssetManager.Get<TextureAsset>(selectedFile!);
            var matAsset = AssetManager.Get<MaterialAsset>(selectedFile!);
            var modelAsset = AssetManager.Get<ModelAsset>(selectedFile!);

            if (textureAsset != null)
                DrawTexturePreview(textureAsset);
            else {

                var asset = (Asset?)matAsset ?? modelAsset;

                if (asset != null) {

                    Draw3DPreview(asset);
                    DrawOverlayUi(asset);
                }
            }
        }

        EndTextureMode();

        var tex = (IntPtr)_rt.Texture.Id;
        Image(tex, avail, new Vector2(0, 1), new Vector2(1, 0));
    }

    private void DrawTexturePreview(TextureAsset tex) {

        if (!tex.IsLoaded) return;

        if (IsWindowHovered()) {

            _zoom += GetMouseWheelMove() * 0.1f * _zoom;
            _zoom = Math.Clamp(_zoom, 0.01f, 100f);
            if (IsMouseDown(ImGuiMouseButton.Left)) _pan += GetMouseDelta() / _zoom;
        }

        var tw = tex.Texture.Width;
        var th = tex.Texture.Height;
        var drawSize = new Vector2(tw, th) * _zoom;
        var pos = (new Vector2(_rt.Texture.Width, _rt.Texture.Height) - drawSize) * 0.5f + _pan * _zoom;

        DrawTextureEx(tex.Texture, pos, 0, _zoom, Color.White);
    }

    private void Draw3DPreview(Asset asset) {

        if (!asset.IsLoaded) return;

        // Interaction
        if (IsWindowHovered()) {

            if (IsMouseDown(ImGuiMouseButton.Left) && !IsAnyArrowHovered(asset)) {

                var delta = GetMouseDelta();

                _rotation.X += delta.X * 0.5f;
                _rotation.Y = Math.Clamp(_rotation.Y + delta.Y * 0.5f, -89f, 89f);
            }

            _distance -= GetMouseWheelMove() * 0.5f * (_distance / 5.0f);
            _distance = Math.Clamp(_distance, 0.01f, 1000f);
        }

        GetAssetAutoDistance(asset, out var targetPos);

        var camPos = targetPos + new Vector3((float)(Math.Cos(_rotation.X * Math.PI / 180.0) * Math.Cos(_rotation.Y * Math.PI / 180.0) * _distance), (float)(Math.Sin(_rotation.Y * Math.PI / 180.0) * _distance), (float)(Math.Sin(_rotation.X * Math.PI / 180.0) * Math.Cos(_rotation.Y * Math.PI / 180.0) * _distance));

        var camera = new Raylib_cs.Camera3D {
            Position = camPos,
            Target = targetPos,
            Up = Vector3.UnitY,
            FovY = 45.0f,
            Projection = CameraProjection.Perspective
        };

        // Draw Grid
        BeginMode3D(camera);

        var gridCol = new Color(80, 80, 80, 100);
        var gridSize = Math.Max(10f, _distance * 2f);

        for (var i = -(int)gridSize; i <= (int)gridSize; i++) {

            DrawLine3D(new Vector3(i, 0, -gridSize), new Vector3(i, 0, gridSize), gridCol);
            DrawLine3D(new Vector3(-gridSize, 0, i), new Vector3(gridSize, 0, i), gridCol);
        }

        EndMode3D();

        BeginMode3D(camera);
        RenderAsset3D(asset, camera, targetPos, _distance, true);
        EndMode3D();
    }

    private bool IsAnyArrowHovered(Asset asset) {

        var count = asset switch {

            MaterialAsset    => PrimitiveNames.Length,
            ModelAsset model => model.Animations.Count,
            _                => 0
        };

        if (count <= 1) return false;

        var mousePos = GetMousePositionInRt();
        var (left, right, _, _) = GetUiBounds();

        return CheckCollisionPointRec(mousePos, left) || CheckCollisionPointRec(mousePos, right);
    }

    private static Vector2 GetMousePositionInRt() {
        // Correct way to get mouse in RT while in ImGui window
        var winPos = GetWindowPos();
        var curPos = GetCursorStartPos(); // This is local to window

        return GetMousePosition() - winPos - curPos;
    }

    private (Rectangle Left, Rectangle Right, float Width, float Height) GetUiBounds() {

        var w = (float)_rt.Texture.Width;
        var h = (float)_rt.Texture.Height;

        const float barHeight = 35f;
        const float arrowWidth = 45f;

        var leftArrowRect = new Rectangle(0, h - barHeight, arrowWidth, barHeight);
        var rightArrowRect = new Rectangle(w - arrowWidth, h - barHeight, arrowWidth, barHeight);

        return (leftArrowRect, rightArrowRect, w, barHeight);
    }

    private void DrawOverlayUi(Asset asset) {

        string label;
        int count;
        int currentIndex;

        switch (asset) {

            case MaterialAsset:
                label = PrimitiveNames[_currentPrimitiveIndex];
                count = PrimitiveNames.Length;
                currentIndex = _currentPrimitiveIndex;

                break;

            case ModelAsset { Animations.Count: > 0 } model:
                label = model.Animations[_currentAnimationIndex].Name;
                count = model.Animations.Count;
                currentIndex = _currentAnimationIndex;

                break;

            default: return;
        }

        var font = Fonts.RlMontserratRegular;
        var (leftRect, rightRect, fullWidth, barHeight) = GetUiBounds();
        var h = _rt.Texture.Height;
        var uiY = h - barHeight * 0.5f;
        var mousePos = GetMousePositionInRt();

        // Background
        DrawRectangle(0, (int)(h - barHeight), (int)fullWidth, (int)barHeight, new Color(15, 15, 15, 220));

        var idStr = $"{currentIndex}";
        var idTextSize = MeasureTextEx(font, idStr, 18, 1);
        const float idPadding = 20f;
        var idAreaWidth = idTextSize.X + idPadding;

        var maxLabelWidth = fullWidth - (leftRect.Width + rightRect.Width + idAreaWidth + 10);
        var displayLabel = label;

        if (MeasureTextEx(font, displayLabel, 18, 1).X > maxLabelWidth) {

            while (displayLabel.Length > 0 && MeasureTextEx(font, "..." + displayLabel, 18, 1).X > maxLabelWidth) displayLabel = displayLabel.Substring(1);

            displayLabel = "..." + displayLabel;
        }

        var labelSize = MeasureTextEx(font, displayLabel, 18, 1);
        var totalTextWidth = idAreaWidth + labelSize.X;
        var startX = (fullWidth - totalTextWidth) * 0.5f;

        // Interaction Rects for Copy
        var idRect = new Rectangle(startX, h - barHeight, idAreaWidth, barHeight);
        var labelRect = new Rectangle(startX + idAreaWidth, h - barHeight, labelSize.X, barHeight);

        bool idHover = CheckCollisionPointRec(mousePos, idRect);
        bool labelHover = CheckCollisionPointRec(mousePos, labelRect);

        if (idHover) DrawRectangleRec(idRect, new Color(255, 255, 255, 15));
        if (labelHover) DrawRectangleRec(labelRect, new Color(255, 255, 255, 15));

        // Draw Texts - Center ID text within its allocated padded area
        DrawTextEx(font, idStr, new Vector2(startX + (idAreaWidth - idTextSize.X) * 0.5f - 2, uiY - idTextSize.Y * 0.5f), 18, 1, idHover ? Color.SkyBlue : new Color(140, 140, 150, 255));
        DrawTextEx(font, displayLabel, new Vector2(startX + idAreaWidth, uiY - labelSize.Y * 0.5f), 18, 1, labelHover ? Color.SkyBlue : Color.White);

        if (count <= 1) return;

        bool leftHover = CheckCollisionPointRec(mousePos, leftRect);
        bool rightHover = CheckCollisionPointRec(mousePos, rightRect);

        if (leftHover) DrawRectangleRec(leftRect, new Color(255, 255, 255, 20));
        if (rightHover) DrawRectangleRec(rightRect, new Color(255, 255, 255, 20));

        // Arrows
        var alc = new Vector2(leftRect.X + leftRect.Width * 0.5f, leftRect.Y + leftRect.Height * 0.5f);
        DrawTriangle(new Vector2(alc.X - 8, alc.Y), new Vector2(alc.X + 4, alc.Y + 8), new Vector2(alc.X + 4, alc.Y - 8), leftHover ? Color.SkyBlue : Color.White);

        var arc = new Vector2(rightRect.X + rightRect.Width * 0.5f, rightRect.Y + rightRect.Height * 0.5f);
        DrawTriangle(new Vector2(arc.X + 8, arc.Y), new Vector2(arc.X - 4, arc.Y - 8), new Vector2(arc.X - 4, arc.Y + 8), rightHover ? Color.SkyBlue : Color.White);

        if (!IsMouseButtonPressed(MouseButton.Left)) return;

        if (leftHover) {

            switch (asset) {

                case MaterialAsset: _currentPrimitiveIndex = (_currentPrimitiveIndex - 1 + count) % count; break;

                case ModelAsset:
                    _currentAnimationIndex = (_currentAnimationIndex - 1 + count) % count;
                    _animationTime = 0;

                    break;
            }
        } else if (rightHover) {

            switch (asset) {

                case MaterialAsset: _currentPrimitiveIndex = (_currentPrimitiveIndex + 1) % count; break;

                case ModelAsset:
                    _currentAnimationIndex = (_currentAnimationIndex + 1) % count;
                    _animationTime = 0;

                    break;
            }
        } else if (idHover) {

            Raylib.SetClipboardText(currentIndex.ToString());
            Notifications.Show($"Index Copied: {currentIndex}");
        } else if (labelHover) {

            Raylib.SetClipboardText(label);
            Notifications.Show($"Name Copied: {label}");
        }
    }

    private void RenderAsset3D(Asset asset, Raylib_cs.Camera3D camera, Vector3 target, float distance, bool isInteractive) {

        switch (asset) {

            case MaterialAsset mat: {

                var mesh = GetPrimitiveMesh(_currentPrimitiveIndex);
                mat.ApplyChanges(updateThumbnail: false);
                SetupPreviewLighting(mat, camera, target, distance);

                var transform = Matrix4x4.Transpose(Matrix4x4.CreateTranslation(0, 0.5f, 0));
                if (PrimitiveNames[_currentPrimitiveIndex] == "Plane") transform = Matrix4x4.Transpose(Matrix4x4.CreateRotationX((float)Math.PI * -0.5f));

                DrawMesh(mesh, mat.Material, transform);

                break;
            }

            case ModelAsset model: {

                model.UpdateMaterialsIfDirty();
                var matBase = Matrix4x4.Transpose(Matrix4x4.CreateScale(model.Settings.ImportScale));

                if (isInteractive && model.Animations.Count > 0) {

                    var clip = model.Animations[_currentAnimationIndex];
                    _animationTime += GetFrameTime() * clip.TicksPerSecond;
                    if (_animationTime > clip.Duration) _animationTime = 0;

                    AssimpLoader.UpdateAnimation(model.RootNode, clip, _animationTime, Matrix4x4.Identity, model.GlobalInverse, model.BoneMap);

                    foreach (var mesh in model.Meshes) AssimpLoader.SkinMesh(mesh, model.Bones);
                }

                foreach (var mesh in model.Meshes) {

                    var material = mesh.MaterialIndex >= 0 && mesh.MaterialIndex < model.Materials.Length ? model.Materials[mesh.MaterialIndex] : MaterialAsset.Default.Material;
                    var matAsset = mesh.MaterialIndex >= 0 && mesh.MaterialIndex < model.CachedMaterialAssets.Count ? model.CachedMaterialAssets[mesh.MaterialIndex] ?? MaterialAsset.Default : MaterialAsset.Default;

                    SetupPreviewLighting(matAsset, camera, target, distance);
                    DrawMesh(mesh.RlMesh, material, matBase);
                }

                break;
            }
        }
    }

    private static Mesh GetPrimitiveMesh(int index) {

        switch (index) {

            case 0:
                _previewSphere ??= GenMeshSphere(0.5f, 64, 64);

                return _previewSphere.Value;
            case 1:
                _previewCube ??= GenMeshCube(0.8f, 0.8f, 0.8f);

                return _previewCube.Value;
            case 2:
                _previewPlane ??= GenMeshPlane(1.0f, 1.0f, 1, 1);

                return _previewPlane.Value;
            case 3:
                _previewCylinder ??= GenMeshCylinder(0.4f, 0.8f, 32);

                return _previewCylinder.Value;
            case 4:
                _previewTorus ??= GenMeshTorus(0.2f, 0.4f, 32, 32);

                return _previewTorus.Value;
            default: return _previewSphere!.Value;
        }
    }

    private static bool IsAssetReady(Asset asset) {

        if (asset is not { IsLoaded: true }) return false;

        switch (asset) {

            case MaterialAsset mat: {

                foreach (var path in mat.Data.Textures.Values) {

                    if (string.IsNullOrEmpty(path)) continue;

                    var tex = AssetManager.Get<TextureAsset>(path);

                    if (tex == null || !tex.IsLoaded) return false;
                }

                break;
            }

            case ModelAsset model: {

                foreach (var path in model.MaterialPaths) {

                    if (string.IsNullOrEmpty(path)) continue;

                    var subMat = AssetManager.Get<MaterialAsset>(path);

                    if (subMat == null || !IsAssetReady(subMat)) return false;
                }

                break;
            }
        }

        return true;
    }

    private static void SetupPreviewLighting(MaterialAsset mat, Raylib_cs.Camera3D camera, Vector3 target, float distance) {

        var shaderAsset = AssetManager.Get<ShaderAsset>(mat.Data.Shader) ?? AssetManager.Get<ShaderAsset>("pbr");

        if (shaderAsset is not { IsLoaded: true }) return;

        var shader = shaderAsset.Shader;

        mat.ApplyUniforms(shader);

        // Ensure sampler slots are assigned correctly for custom shaders
        SetShaderValue(shader, shaderAsset.GetLoc("albedo_map"), 0, ShaderUniformDataType.Int);
        SetShaderValue(shader, shaderAsset.GetLoc("normal_map"), 1, ShaderUniformDataType.Int);
        SetShaderValue(shader, shaderAsset.GetLoc("metallic_map"), 2, ShaderUniformDataType.Int);
        SetShaderValue(shader, shaderAsset.GetLoc("roughness_map"), 3, ShaderUniformDataType.Int);
        SetShaderValue(shader, shaderAsset.GetLoc("occlusion_map"), 4, ShaderUniformDataType.Int);
        SetShaderValue(shader, shaderAsset.GetLoc("emissive_map"), 5, ShaderUniformDataType.Int);

        var lightDist = distance * 2.0f;
        SetShaderValue(shader, shaderAsset.GetLoc("view_pos"), camera.Position, ShaderUniformDataType.Vec3);
        SetShaderValue(shader, shaderAsset.GetLoc("light_count"), 2, ShaderUniformDataType.Int);
        SetShaderValue(shader, shaderAsset.GetLoc("lights[0].enabled"), 1, ShaderUniformDataType.Int);
        SetShaderValue(shader, shaderAsset.GetLoc("lights[0].type"), 0, ShaderUniformDataType.Int);
        SetShaderValue(shader, shaderAsset.GetLoc("lights[0].position"), target + new Vector3(lightDist, lightDist, lightDist), ShaderUniformDataType.Vec3);
        SetShaderValue(shader, shaderAsset.GetLoc("lights[0].target"), target, ShaderUniformDataType.Vec3);
        SetShaderValue(shader, shaderAsset.GetLoc("lights[0].color"), Vector4.One, ShaderUniformDataType.Vec4);
        SetShaderValue(shader, shaderAsset.GetLoc("lights[0].intensity"), 4.0f, ShaderUniformDataType.Float);

        SetShaderValue(shader, shaderAsset.GetLoc("lights[1].enabled"), 1, ShaderUniformDataType.Int);
        SetShaderValue(shader, shaderAsset.GetLoc("lights[1].type"), 0, ShaderUniformDataType.Int);
        SetShaderValue(shader, shaderAsset.GetLoc("lights[1].position"), target + new Vector3(-lightDist, lightDist * 0.5f, lightDist * 0.5f), ShaderUniformDataType.Vec3);
        SetShaderValue(shader, shaderAsset.GetLoc("lights[1].target"), target, ShaderUniformDataType.Vec3);
        SetShaderValue(shader, shaderAsset.GetLoc("lights[1].color"), new Vector4(0.8f, 0.9f, 1.0f, 1.0f), ShaderUniformDataType.Vec4);
        SetShaderValue(shader, shaderAsset.GetLoc("lights[1].intensity"), 1.5f, ShaderUniformDataType.Float);

        SetShaderValue(shader, shaderAsset.GetLoc("ambient_intensity"), 1.0f, ShaderUniformDataType.Float);
        SetShaderValue(shader, shaderAsset.GetLoc("ambient_color"), Vector3.One, ShaderUniformDataType.Vec3);
        SetShaderValue(shader, shaderAsset.GetLoc("aoValue"), 1.0f, ShaderUniformDataType.Float);
        SetShaderValue(shader, shaderAsset.GetLoc("receive_shadows"), 0, ShaderUniformDataType.Int);
    }

    private static float GetAssetAutoDistance(Asset asset, out Vector3 center) {

        center = Vector3.Zero;

        switch (asset) {

            case MaterialAsset:
                center = new Vector3(0, 0.5f, 0);

                return 1.4f;

            case ModelAsset model: {

                var min = new Vector3(float.MaxValue);
                var max = new Vector3(float.MinValue);
                var hasVertices = false;
                var scale = model.Settings.ImportScale;

                foreach (var mesh in model.Meshes) {

                    foreach (var v in mesh.Vertices) {

                        var tv = v * scale;
                        min = Vector3.Min(min, tv);
                        max = Vector3.Max(max, tv);
                        hasVertices = true;
                    }
                }

                if (!hasVertices) return 5f;

                center = (min + max) * 0.5f;
                var size = max - min;
                var diagonal = size.Length();

                return diagonal * 1.35f;
            }

            default: return 5f;
        }
    }

    private static bool _isProcessingThumbnail;

    public static void UpdateThumbnail(Asset? asset) {

        if (asset == null || _isProcessingThumbnail) return;

        _isProcessingThumbnail = true;

        try {

            switch (asset) {

                case TextureAsset tex: GenerateTextureThumbnail(tex); break;

                case MaterialAsset or ModelAsset: Generate3DThumbnail(asset); break;
            }

        } finally {
            _isProcessingThumbnail = false;
        }
    }

    private static unsafe void GenerateTextureThumbnail(TextureAsset tex) {

        if (!tex.IsLoaded) return;

        var img = LoadImage(tex.File);

        if (img.Data == null) return;

        int newW, newH;

        if (img.Width > img.Height) {
            newW = 64;
            newH = (int)((float)img.Height / img.Width * 64);
        } else {
            newH = 64;
            newW = (int)((float)img.Width / img.Height * 64);
        }

        ImageResize(&img, newW, newH);
        if (tex.Thumbnail.HasValue) UnloadTexture(tex.Thumbnail.Value);
        tex.Thumbnail = LoadTextureFromImage(img);
        UnloadImage(img);
    }

    private static unsafe void Generate3DThumbnail(Asset asset) {

        if (!asset.IsLoaded) return;
        if (!IsAssetReady(asset)) return;

        const int size = 64;
        var rt = LoadRenderTexture(size, size);
        BeginTextureMode(rt);
        ClearBackground(Color.Blank);
        BeginBlendMode(BlendMode.Alpha);

        Raylib_cs.Camera3D camera;
        Vector3 targetPos;
        float dist;

        if (asset is MaterialAsset) {

            targetPos = new Vector3(0, 0.5f, 0);
            dist = 1.15f;

            camera = new Raylib_cs.Camera3D {
                Position = new Vector3(0, 0.5f, 1.4f),
                Target = targetPos,
                Up = Vector3.UnitY,
                FovY = 45.0f,
                Projection = CameraProjection.Perspective
            };

        } else {

            dist = GetAssetAutoDistance(asset, out targetPos);

            camera = new Raylib_cs.Camera3D {
                Position = targetPos + Vector3.Normalize(new Vector3(1, 0.8f, 1)) * dist,
                Target = targetPos,
                Up = Vector3.UnitY,
                FovY = 45.0f,
                Projection = CameraProjection.Perspective
            };
        }

        BeginMode3D(camera);
        RenderAsset3DStatic(asset, camera, targetPos, dist);
        EndMode3D();
        EndBlendMode();
        EndTextureMode();

        var img = LoadImageFromTexture(rt.Texture);
        ImageFlipVertical(&img);
        if (asset.Thumbnail.HasValue) UnloadTexture(asset.Thumbnail.Value);
        asset.Thumbnail = LoadTextureFromImage(img);
        UnloadImage(img);
        UnloadRenderTexture(rt);
    }

    private static void RenderAsset3DStatic(Asset asset, Raylib_cs.Camera3D camera, Vector3 target, float distance) {

        switch (asset) {

            case MaterialAsset mat: {

                var mesh = _previewSphere ?? GenMeshSphere(0.5f, 64, 64);
                _previewSphere = mesh;

                mat.ApplyChanges(updateThumbnail: false);

                SetupPreviewLighting(mat, camera, target, distance);
                DrawMesh(mesh, mat.Material, Matrix4x4.Transpose(Matrix4x4.CreateTranslation(0, 0.5f, 0)));

                break;
            }

            case ModelAsset model: {

                model.UpdateMaterialsIfDirty();

                var matBase = Matrix4x4.Transpose(Matrix4x4.CreateScale(model.Settings.ImportScale));

                foreach (var mesh in model.Meshes) {

                    var material = mesh.MaterialIndex >= 0 && mesh.MaterialIndex < model.Materials.Length ? model.Materials[mesh.MaterialIndex] : MaterialAsset.Default.Material;
                    var matAsset = mesh.MaterialIndex >= 0 && mesh.MaterialIndex < model.CachedMaterialAssets.Count ? model.CachedMaterialAssets[mesh.MaterialIndex] ?? MaterialAsset.Default : MaterialAsset.Default;

                    SetupPreviewLighting(matAsset, camera, target, distance);
                    DrawMesh(mesh.RlMesh, material, matBase);
                }

                break;
            }
        }
    }
}
