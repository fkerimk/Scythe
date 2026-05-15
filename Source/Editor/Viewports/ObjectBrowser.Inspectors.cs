using System.Numerics;
using System.Reflection;
using EnumsNET;
using ImGuiNET;
using Raylib_cs;
using static ImGuiNET.ImGui;

internal partial class ObjectBrowser {

    private void DrawAssetInspector(string path) {

        var ext = Path.GetExtension(path).ToLowerInvariant();

        if (CollectionData.IsLevel(path)) {
            DrawImportedAsset<LevelAsset>(path, DrawLevelAssetInspector);
            return;
        }

        if (CollectionData.IsMaterial(path)) {
            DrawImportedAsset<MaterialAsset>(path, DrawMaterialAssetInspector);
            return;
        }

        if (_assetInspectorByExtension.TryGetValue(ext, out var inspector))
            inspector(this, path);
    }

    private void DrawLevelAssetInspector(LevelAsset levelAsset) {

        var path = levelAsset.File;
        var levelName = CollectionData.GetLevelDisplayName(path);

        PushID(levelAsset.GUID);
        DrawSectionHeader("Level Asset", Icons.FaMap, Colors.GuiCollectionLevel, out var open);

        if (open) {

            PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 8));
            Columns(2, "##level_asset_props", false);
            SetColumnWidth(0, GetWindowWidth() * 0.32f);

            DrawInfoRow("Name", levelName);

            DrawShadowedLabel("Skybox");
            object? skybox = levelAsset.Skybox;
            var (skyboxChanged, skyboxDeactivated) = DrawInspectorField("LevelAssetSkybox", ref skybox, typeof(string), [levelAsset], nameof(LevelAsset.Skybox), "TextureAsset");

            if (skyboxChanged) {
                levelAsset.Skybox = (string)skybox!;
                levelAsset.SkyboxPath = AssetManager.GetPath<TextureAsset>(levelAsset.Skybox) is { } resolvedPath
                    ? AssetManager.GetStoredPath(resolvedPath)
                    : "";
                levelAsset.SaveSettings();
                levelAsset.ApplyToActiveLevelIfOpen();
            }

            if (skyboxDeactivated) History.StopRecording();

            DrawShadowedLabel("Skybox Tint");
            object? tint = levelAsset.SkyboxTint;
            var (skyboxTintChanged, skyboxTintDeactivated) = DrawInspectorField("LevelAssetSkyboxTint", ref tint, typeof(Color), [levelAsset], nameof(LevelAsset.SkyboxTint));

            if (skyboxTintChanged) {
                levelAsset.SkyboxTint = (Color)tint!;
                levelAsset.SaveSettings();
                levelAsset.ApplyToActiveLevelIfOpen();
            }

            if (skyboxTintDeactivated) History.StopRecording();

            DrawShadowedLabel("Background Color");
            object? background = levelAsset.BackgroundColor;
            var (backgroundChanged, backgroundDeactivated) = DrawInspectorField("LevelAssetBackground", ref background, typeof(Color), [levelAsset], nameof(LevelAsset.BackgroundColor));

            if (backgroundChanged) {
                levelAsset.BackgroundColor = (Color)background!;
                levelAsset.SaveSettings();
                levelAsset.ApplyToActiveLevelIfOpen();
            }

            if (backgroundDeactivated) History.StopRecording();

            DrawShadowedLabel("Ambient Color");
            object? ambient = levelAsset.AmbientColor;
            var (ambientChanged, ambientDeactivated) = DrawInspectorField("LevelAssetAmbient", ref ambient, typeof(Color), [levelAsset], nameof(LevelAsset.AmbientColor));

            if (ambientChanged) {
                levelAsset.AmbientColor = (Color)ambient!;
                levelAsset.SaveSettings();
                levelAsset.ApplyToActiveLevelIfOpen();
            }

            if (ambientDeactivated) History.StopRecording();

            DrawShadowedLabel("Skybox Ambient");
            object? skyboxAmbient = levelAsset.SkyboxAmbientEnabled;
            var (skyboxAmbientChanged, skyboxAmbientDeactivated) = DrawInspectorField("LevelAssetSkyboxAmbientEnabled", ref skyboxAmbient, typeof(bool), [levelAsset], nameof(LevelAsset.SkyboxAmbientEnabled));

            if (skyboxAmbientChanged) {
                levelAsset.SkyboxAmbientEnabled = (bool)skyboxAmbient!;
                levelAsset.SaveSettings();
                levelAsset.ApplyToActiveLevelIfOpen();
            }

            if (skyboxAmbientDeactivated) History.StopRecording();

            DrawShadowedLabel("Skybox Ambient Intensity");
            object? skyboxAmbientIntensityValue = levelAsset.SkyboxAmbientIntensity;
            var (skyboxAmbientIntensityChanged, skyboxAmbientIntensityDeactivated) = DrawInspectorField("LevelAssetSkyboxAmbientIntensity", ref skyboxAmbientIntensityValue, typeof(float), [levelAsset], nameof(LevelAsset.SkyboxAmbientIntensity));

            if (skyboxAmbientIntensityChanged) {
                levelAsset.SkyboxAmbientIntensity = Math.Clamp((float)skyboxAmbientIntensityValue!, 0.0f, 1.0f);
                levelAsset.SaveSettings();
                levelAsset.ApplyToActiveLevelIfOpen();
            }

            if (skyboxAmbientIntensityDeactivated) History.StopRecording();

            Columns(1);
            PopStyleVar();
        }

        EndSection(open);
        PopID();
    }

    private void DrawModelAssetInspector(ModelAsset model) {

        PushID(model.GetHashCode());
        DrawSectionHeader("Model Asset", Icons.FaCube, Colors.GuiTypeModel, out var open);

        if (open) {
            PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 8));
            Columns(2, "##model_asset_props", false);
            SetColumnWidth(0, GetWindowWidth() * 0.32f);

            DrawInfoRow("Source Size", FormatFileSize(model.File));
            DrawInfoRow("Imported Size", FormatFileSize(model.ImportedFile));

            DrawShadowedLabel("Import Scale");

            object? scale = model.Settings.ImportScale;

            var (sChanged, sDeactivated) = DrawInspectorField("ImportScale", ref scale, typeof(float), [model], "Settings");

            if (sChanged) {

                model.Settings.ImportScale = (float)scale!;
                model.SaveSettings();
            }

            if (sDeactivated) History.StopRecording();

            for (var i = 0; i < model.Materials.Length; i++) {

                var name = i < model.Meshes.Count && !string.IsNullOrEmpty(model.Meshes[i].Name) ? model.Meshes[i].Name : $"Mesh {i}";
                DrawShadowedLabel(name);
                object? val = model.MaterialPaths[i];

                var (changed, deactivated) = DrawInspectorField($"MeshMat_{i}", ref val, typeof(string), [model], "Settings", "MaterialAsset");

                if (changed) model.ApplyMaterial(i, (string)val!);

                if (deactivated) History.StopRecording();
            }
            Columns(1);
            DrawModelAnimationClips(model);
            PopStyleVar();
        }

        EndSection(open);
        PopID();
    }

    private void DrawModelAnimationClips(ModelAsset model) {

        var sourceAnimations = AssetManager.GetImportedAnimationTracks(model.File);
        ModelAsset.EnsureDefaultAnimationClips(sourceAnimations, model.Settings);

        var buttonSize = new Vector2(GetFrameHeight(), GetFrameHeight());
        Separator();
        Spacing();
        PushFont(Fonts.ImMontserratRegular);
        Text("Animation Clips");
        PopFont();
        SameLine();
        PushFont(Fonts.ImFontAwesomeSmall);
        if (Button($"{Icons.FaPlus}##add_anim_clip", buttonSize)) {
            History.StartRecording(model, nameof(ModelAsset.Settings));
            var defaultTrack = sourceAnimations.Count > 0 ? 0 : -1;
            model.Settings.AnimationClips.Add(CreateDefaultClipSettings(sourceAnimations, defaultTrack, appendCopySuffix: true));
            ApplyModelAnimationClipChanges(model, sourceAnimations);
            History.StopRecording();
        }
        SameLine();
        if (Button($"{Icons.FaRotateLeft}##reset_anim_clips", buttonSize)) {
            History.StartRecording(model, nameof(ModelAsset.Settings));
            ResetAnimationClipsToDefaults(model, sourceAnimations);
            History.StopRecording();
        }
        PopFont();
        if (IsItemHovered())
            SetTooltip("Reset clips to imported defaults");

        if (model.Settings.AnimationClips.Count == 0) {
            PushStyleColor(ImGuiCol.Text, Colors.GuiTextDisabled.ToVector4());
            TextUnformatted("No clips.");
            PopStyleColor();
            return;
        }

        for (var i = 0; i < model.Settings.AnimationClips.Count; i++) {
            var clipSettings = model.Settings.AnimationClips[i];
            var trackName = clipSettings.Track >= 0 && clipSettings.Track < sourceAnimations.Count
                ? ModelAsset.GetDefaultClipName(sourceAnimations[clipSettings.Track], clipSettings.Track)
                : "Missing Track";

            PushID($"anim_clip_{i}");
            Separator();
            TextColored(Colors.GuiTextDisabled.ToVector4(), $"Clip {i}");
            SameLine();
            if (SmallButton($"Up##move_up_clip") && i > 0) {
                History.StartRecording(model, nameof(ModelAsset.Settings));
                (model.Settings.AnimationClips[i - 1], model.Settings.AnimationClips[i]) = (model.Settings.AnimationClips[i], model.Settings.AnimationClips[i - 1]);
                ApplyModelAnimationClipChanges(model, sourceAnimations);
                History.StopRecording();
            }
            SameLine();
            if (SmallButton($"Down##move_down_clip") && i < model.Settings.AnimationClips.Count - 1) {
                History.StartRecording(model, nameof(ModelAsset.Settings));
                (model.Settings.AnimationClips[i + 1], model.Settings.AnimationClips[i]) = (model.Settings.AnimationClips[i], model.Settings.AnimationClips[i + 1]);
                ApplyModelAnimationClipChanges(model, sourceAnimations);
                History.StopRecording();
            }
            SameLine();
            if (SmallButton($"Revert##revert_clip")) {
                History.StartRecording(model, nameof(ModelAsset.Settings));
                model.Settings.AnimationClips[i] = CreateDefaultClipSettings(sourceAnimations, clipSettings.Track, appendCopySuffix: false);
                ApplyModelAnimationClipChanges(model, sourceAnimations);
                History.StopRecording();
            }
            SameLine();
            if (SmallButton($"Delete##remove_clip")) {
                History.StartRecording(model, nameof(ModelAsset.Settings));
                model.Settings.AnimationClips.RemoveAt(i);
                ApplyModelAnimationClipChanges(model, sourceAnimations);
                History.StopRecording();
                PopID();
                continue;
            }

            var trackMax = Math.Max(0, sourceAnimations.Count - 1);
            int sourceDuration = clipSettings.Track >= 0 && clipSettings.Track < sourceAnimations.Count
                ? (int)Math.Ceiling(sourceAnimations[clipSettings.Track].Duration)
                : 0;

            PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 8));
            Columns(2, $"##anim_clip_fields_{i}", false);
            SetColumnWidth(0, GetWindowWidth() * 0.28f);

            DrawInfoRow("Source", $"{trackName} ({sourceDuration} frames)");

            object? clipName = clipSettings.Name;
            var (clipNameChanged, clipNameDeactivated) = DrawClipSettingField(model, "Name", ref clipName, typeof(string));
            clipSettings.Name = (string)clipName!;
            if (clipNameChanged) ApplyModelAnimationClipChanges(model, sourceAnimations);
            if (clipNameDeactivated) History.StopRecording();

            object? trackValue = clipSettings.Track;
            var (trackChanged, trackDeactivated) = DrawClipSettingField(model, "Track", ref trackValue, typeof(int));
            clipSettings.Track = sourceAnimations.Count == 0 ? -1 : Math.Clamp((int)trackValue!, 0, trackMax);
            if (trackChanged) ApplyModelAnimationClipChanges(model, sourceAnimations);
            if (trackDeactivated) History.StopRecording();

            sourceDuration = clipSettings.Track >= 0 && clipSettings.Track < sourceAnimations.Count
                ? (int)Math.Ceiling(sourceAnimations[clipSettings.Track].Duration)
                : 0;
            clipSettings.StartFrame = Math.Clamp(clipSettings.StartFrame, 0, sourceDuration);
            clipSettings.EndFrame = Math.Clamp(clipSettings.EndFrame, clipSettings.StartFrame, sourceDuration);

            object? startFrame = clipSettings.StartFrame;
            var (startFrameChanged, startFrameDeactivated) = DrawClipSettingField(model, "Start Frame", ref startFrame, typeof(int));
            clipSettings.StartFrame = Math.Clamp((int)startFrame!, 0, sourceDuration);
            if (startFrameChanged) ApplyModelAnimationClipChanges(model, sourceAnimations);
            if (startFrameDeactivated) History.StopRecording();

            object? endFrame = clipSettings.EndFrame;
            var (endFrameChanged, endFrameDeactivated) = DrawClipSettingField(model, "End Frame", ref endFrame, typeof(int));
            clipSettings.EndFrame = Math.Clamp((int)endFrame!, clipSettings.StartFrame, sourceDuration);
            if (endFrameChanged) ApplyModelAnimationClipChanges(model, sourceAnimations);
            if (endFrameDeactivated) History.StopRecording();

            object? loop = clipSettings.Loop;
            var (loopChanged, loopDeactivated) = DrawClipSettingField(model, "Loop", ref loop, typeof(bool));
            clipSettings.Loop = (bool)loop!;
            if (loopChanged) ApplyModelAnimationClipChanges(model, sourceAnimations);
            if (loopDeactivated) History.StopRecording();

            Columns(1);
            PopStyleVar();
            PopID();
        }
    }

    private (bool Changed, bool Deactivated) DrawClipSettingField(ModelAsset model, string label, ref object? value, Type type) {

        DrawShadowedLabel(label);
        return DrawInspectorField($"ModelAnimClip_{label}", ref value, type, [model], nameof(ModelAsset.Settings));
    }

    private static ModelAsset.ModelSettings.AnimationClipSettings CreateDefaultClipSettings(List<AnimationClip> sourceAnimations, int trackIndex, bool appendCopySuffix) {

        if (trackIndex < 0 || trackIndex >= sourceAnimations.Count) {
            return new ModelAsset.ModelSettings.AnimationClipSettings {
                Name = "Clip",
                Track = -1,
                StartFrame = 0,
                EndFrame = 0,
                Loop = true
            };
        }

        var source = sourceAnimations[trackIndex];
        var name = ModelAsset.GetDefaultClipName(source, trackIndex);
        if (appendCopySuffix)
            name += " Copy";

        return new ModelAsset.ModelSettings.AnimationClipSettings {
            Name = name,
            Track = trackIndex,
            StartFrame = 0,
            EndFrame = (int)Math.Ceiling(source.Duration),
            Loop = true
        };
    }

    private static void ResetAnimationClipsToDefaults(ModelAsset model, List<AnimationClip> sourceAnimations) {

        model.Settings.AnimationClips.Clear();
        ModelAsset.EnsureDefaultAnimationClips(sourceAnimations, model.Settings);
        ApplyModelAnimationClipChanges(model, sourceAnimations);
    }

    private static void ApplyModelAnimationClipChanges(ModelAsset model, List<AnimationClip> sourceAnimations) {

        model.Animations = ModelAsset.BuildAnimationClips(sourceAnimations, model.Settings);
        model.SaveSettings();
    }

    private void DrawMaterialAssetInspector(MaterialAsset mat) {

        PushID(mat.GetHashCode());
        DrawSectionHeader("Material Asset", Icons.FaFileImage, Colors.GuiTypeModel, out var open);

        if (open) {

            DrawShadowedLabel("Shader");

            object? shader = mat.Data.Shader;
            var (shaderChanged, shaderDeactivated) = DrawInspectorField("Shader", ref shader, typeof(string), [mat], "Data", "ShaderAsset");

            if (shaderChanged) {

                mat.Data.Shader = (string)shader!;
                mat.Save();
                mat.ApplyChanges();
            }

            if (shaderDeactivated) History.StopRecording();

            var shaderName = string.IsNullOrEmpty(mat.Data.Shader) ? "Collection/pbr.vs" : mat.Data.Shader;
            var sa = AssetManager.Get<ShaderAsset>(shaderName);

            if (sa != null) {

                foreach (var prop in sa.Properties) {

                    PushID(prop.Name);
                    DrawShadowedLabel(prop.Name);

                    object? val = null;
                    var t = typeof(float);
                    string? picker = null;

                    switch (prop.Type) {

                        case "sampler2D":
                            val = mat.Data.Textures.GetValueOrDefault(prop.Name, mat == MaterialAsset.Default ? "" : MaterialAsset.Default.Data.Textures.GetValueOrDefault(prop.Name, ""));
                            t = typeof(string);
                            picker = "TextureAsset";

                            break;

                        case "float":
                            val = mat.Data.Floats.GetValueOrDefault(prop.Name, mat == MaterialAsset.Default ? 0f : MaterialAsset.Default.Data.Floats.GetValueOrDefault(prop.Name, 0f));
                            t = typeof(float);

                            break;

                        case "int":
                            val = mat.Data.Ints.GetValueOrDefault(prop.Name, mat == MaterialAsset.Default ? 0 : MaterialAsset.Default.Data.Ints.GetValueOrDefault(prop.Name, 0));
                            t = typeof(int);

                            break;

                        case "vec2":
                            val = mat.Data.Vectors.GetValueOrDefault(prop.Name, mat == MaterialAsset.Default ? Vector2.Zero : MaterialAsset.Default.Data.Vectors.GetValueOrDefault(prop.Name, Vector2.Zero));
                            t = typeof(Vector2);

                            break;

                        case "vec3":
                        case "vec4": {

                            if (prop.Name.Contains("color", StringComparison.OrdinalIgnoreCase) || prop.Name.Contains("albedo", StringComparison.OrdinalIgnoreCase) || prop.Name.Contains("emiss", StringComparison.OrdinalIgnoreCase)) {

                                val = mat.Data.Colors.GetValueOrDefault(prop.Name, mat == MaterialAsset.Default ? Color.White : MaterialAsset.Default.Data.Colors.GetValueOrDefault(prop.Name, Color.White));
                                t = typeof(Color);

                            } else {

                                val = prop.Type == "vec3" ? Vector3.Zero : Vector4.One;
                                t = prop.Type == "vec3" ? typeof(Vector3) : typeof(Vector4);
                            }

                            break;
                        }
                    }

                    var (propChanged, propDeactivated) = DrawInspectorField(prop.Name, ref val, t, [mat], "Data", picker);

                    if (val != null && propChanged) {

                        if (t == typeof(string))
                            mat.Data.Textures[prop.Name] = (string)val;
                        else if (t == typeof(float))
                            mat.Data.Floats[prop.Name] = (float)val;
                        else if (t == typeof(int))
                            mat.Data.Ints[prop.Name] = (int)val;
                        else if (t == typeof(Vector2))
                            mat.Data.Vectors[prop.Name] = (Vector2)val;
                        else if (t == typeof(Color)) mat.Data.Colors[prop.Name] = (Color)val;

                        mat.Save();
                        mat.ApplyChanges();
                    }

                    if (propDeactivated) History.StopRecording();

                    PopID();
                }
            }
        }

        EndSection(open);
        PopID();
    }

    private void DrawScriptAssetInspector(ScriptAsset scriptAsset) {

        PushID(scriptAsset.GetHashCode());
        DrawSectionHeader("Script Asset", Icons.FaCode, Color.White, out var open);

        if (open) {

            DrawInfoRow("Class", Path.GetFileNameWithoutExtension(scriptAsset.File));

            if (scriptAsset.ScriptType == null)
                DrawInfoRow("Status", "Type not loaded");
            else {
                DrawScriptFieldRows([scriptAsset], scriptAsset, ScriptFieldStorageKind.Config);

                var inheritedConfigFields = ScriptFieldUtility.GetFields(scriptAsset.ScriptType, ScriptFieldStorageKind.Config)
                    .Where(field => field.DeclaringType != scriptAsset.ScriptType)
                    .ToArray();

                if (inheritedConfigFields.Length > 0) {
                    Spacing();
                    DrawInfoRow("Inherited Config", string.Join(", ", inheritedConfigFields.Select(field => $"{field.Name} ({field.DeclaringType?.Name})")));
                }
            }
        }

        EndSection(open);
        PopID();
    }

    private void DrawTextureAssetInspector(TextureAsset texture) {

        PushID(texture.GetHashCode());
        DrawSectionHeader("Texture Asset", Icons.FaFileImage, Colors.GuiTypeModel, out var open);

        if (open) {
            var isBusy = AssetManager.IsTextureImportInProgress(texture.GUID);
            PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 8));
            Columns(2, "##texture_asset_props", false);
            SetColumnWidth(0, GetWindowWidth() * 0.32f);

            DrawInfoRow("Source Resolution", $"{texture.SourceWidth} x {texture.SourceHeight}");
            DrawInfoRow("Imported Resolution", $"{texture.ImportedWidth} x {texture.ImportedHeight}");
            DrawInfoRow("Source Size", FormatFileSize(texture.SourceFileSize));
            DrawInfoRow("Imported Size", FormatFileSize(texture.ImportedFileSize));
            DrawInfoRow("Status", isBusy ? "Importing..." : "Ready");

            BeginDisabled(isBusy);

            DrawShadowedLabel("Format");
            var formatOptions = new[] { "Source", "Png", "Jpeg", "WebP", "Avif" };
            var selectedFormat = Array.IndexOf(formatOptions, texture.ImportSettings.Format);
            if (selectedFormat < 0) selectedFormat = 0;
            SetNextItemWidth(GetContentRegionAvail().X);
            if (Combo("##texture_format", ref selectedFormat, formatOptions, formatOptions.Length)) {
                History.StartRecording(texture, nameof(TextureAsset.ImportSettings));
                texture.ImportSettings.Format = formatOptions[selectedFormat];
                texture.SaveMeta();
                AssetManager.ReimportTextureAsync(texture);
                History.StopRecording();
            }
            NextColumn();

            var effectiveFormat = TextureImportProcessor.GetEffectiveFormat(texture.File, texture.ImportSettings);
            var usesResizeFilter = texture.ImportSettings.MaxSize > 0;
            var usesCompression = TextureImportProcessor.UsesCompression(effectiveFormat);
            var usesQuality = TextureImportProcessor.UsesQuality(effectiveFormat);

            var maxSizeOptions = new[] { 0, 32, 64, 128, 256, 512, 1024, 2048, 4096 };
            var maxSizeLabels = new[] { "Original", "32", "64", "128", "256", "512", "1024", "2048", "4096" };
            var selectedMaxSize = Array.IndexOf(maxSizeOptions, texture.ImportSettings.MaxSize);
            if (selectedMaxSize < 0) selectedMaxSize = 0;

            DrawShadowedLabel("Max Size");
            SetNextItemWidth(GetContentRegionAvail().X);
            if (Combo("##texture_max_size", ref selectedMaxSize, maxSizeLabels, maxSizeLabels.Length)) {
                History.StartRecording(texture, nameof(TextureAsset.ImportSettings));
                texture.ImportSettings.MaxSize = maxSizeOptions[selectedMaxSize];
                texture.SaveMeta();
                AssetManager.ReimportTextureAsync(texture);
                History.StopRecording();
            }
            NextColumn();

            DrawShadowedLabel("Resize Filter");
            BeginDisabled(!usesResizeFilter);
            var resizeOptions = new[] { "Nearest", "Bilinear", "Bicubic", "Lanczos" };
            var selectedResize = Array.IndexOf(resizeOptions, texture.ImportSettings.ResizeFilter);
            if (selectedResize < 0) selectedResize = 1;
            SetNextItemWidth(GetContentRegionAvail().X);
            if (Combo("##texture_resize_filter", ref selectedResize, resizeOptions, resizeOptions.Length)) {
                History.StartRecording(texture, nameof(TextureAsset.ImportSettings));
                texture.ImportSettings.ResizeFilter = resizeOptions[selectedResize];
                texture.SaveMeta();
                AssetManager.ReimportTextureAsync(texture);
                History.StopRecording();
            }
            EndDisabled();
            NextColumn();

            DrawShadowedLabel("Compression");
            BeginDisabled(!usesCompression);
            var compressionOptions = new[] { "Fast", "Balanced", "Best" };
            var selectedCompression = Array.IndexOf(compressionOptions, texture.ImportSettings.Compression);
            if (selectedCompression < 0) selectedCompression = 1;
            SetNextItemWidth(GetContentRegionAvail().X);
            if (Combo("##texture_compression", ref selectedCompression, compressionOptions, compressionOptions.Length)) {
                History.StartRecording(texture, nameof(TextureAsset.ImportSettings));
                texture.ImportSettings.Compression = compressionOptions[selectedCompression];
                texture.SaveMeta();
                AssetManager.ReimportTextureAsync(texture);
                History.StopRecording();
            }
            EndDisabled();
            NextColumn();

            DrawShadowedLabel("Quality");
            BeginDisabled(!usesQuality);
            if (!_pendingTextureQuality.TryGetValue(texture.GUID, out var quality))
                quality = texture.ImportSettings.Quality;
            SetNextItemWidth(GetContentRegionAvail().X);
            if (SliderInt("##texture_quality", ref quality, 1, 100))
                _pendingTextureQuality[texture.GUID] = quality;

            if (IsItemActivated()) History.StartRecording(texture, nameof(TextureAsset.ImportSettings));

            if (IsItemDeactivatedAfterEdit()) {

                texture.ImportSettings.Quality = quality;
                _pendingTextureQuality[texture.GUID] = quality;
                texture.SaveMeta();
                AssetManager.ReimportTextureAsync(texture);
                History.StopRecording();
            }
            EndDisabled();
            NextColumn();

            DrawShadowedLabel("Texture Filter");
            var textureFilterOptions = new[] { "Point", "Bilinear", "Trilinear", "Anisotropic 4x", "Anisotropic 8x", "Anisotropic 16x" };
            var selectedTextureFilter = Array.IndexOf(textureFilterOptions, texture.ImportSettings.TextureFilter);
            if (selectedTextureFilter < 0) selectedTextureFilter = 1;
            SetNextItemWidth(GetContentRegionAvail().X);
            if (Combo("##texture_filter", ref selectedTextureFilter, textureFilterOptions, textureFilterOptions.Length)) {
                History.StartRecording(texture, nameof(TextureAsset.ImportSettings));
                texture.ImportSettings.TextureFilter = textureFilterOptions[selectedTextureFilter];
                texture.SaveMeta();
                AssetManager.ApplyTextureFilterAsync(texture);
                History.StopRecording();
            }
            NextColumn();

            EndDisabled();
            Columns(1);
            PopStyleVar();
        }

        EndSection(open);
        PopID();
    }

    private void DrawModelAssetFromPath(string path) {

        var asset = AssetManager.GetOrImport<ModelAsset>(path) ?? AssetManager.Get<ModelAsset>(Path.GetFileNameWithoutExtension(path));
        if (asset != null)
            DrawModelAssetInspector(asset);
    }

    private static void DrawInfoRow(string label, string value) {

        DrawShadowedLabel(label);
        TextDisabled(value);
        NextColumn();
    }

    private static string FormatFileSize(string path) {

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "-";

        return FormatFileSize(new FileInfo(path).Length);
    }
}
