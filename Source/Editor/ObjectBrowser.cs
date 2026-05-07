using System.Numerics;
using System.Reflection;
using ImGuiNET;
using Raylib_cs;
using static ImGuiNET.ImGui;

internal class ObjectBrowser : Viewport {

    private int _propIndex;
    private readonly IEnumerable<Type> _addComponentTypes;
    private (string Name, string Path, string GUID)[] _foundAssets = [];
    private string _searchFilter = "";
    private bool _showAnimationFrames;
    private readonly Dictionary<string, int> _pendingTextureQuality = new();

    public ObjectBrowser() : base("Object") {

        var hideComponents = new[] { "Transform" };

        _addComponentTypes = Assembly.GetExecutingAssembly().GetTypes().Where(t => t.IsSubclassOf(typeof(Component)) && !t.IsAbstract && !hideComponents.Contains(t.Name));
    }

    protected override void OnDraw() {

        _propIndex = 0;

        // Asset inspection
        var selectedFile = Editor.SelectedAssetPath;
        if (!string.IsNullOrEmpty(selectedFile)) {

            DrawAssetInspector(selectedFile.Replace('\\', '/'));
            return;
        }

        if (Core.ActiveLevel == null) return;

        var targets = LevelBrowser.SelectedObjects;
        if (targets.Count == 0) return;

        // Header info
        PushStyleColor(ImGuiCol.Text, Colors.GuiTextDisabled.ToVector4());

        if (targets.Count == 1) {

            if (targets[0].Parent != null) {

                Text(targets[0].Parent?.Name);
                SameLine();
            }
        } else
            Text($"{targets.Count} objects selected");

        PopStyleColor();

        Separator();
        Spacing();

        // Object & component inspection
        DrawProperties(targets.Cast<object>().ToList(), false, "Object");
        DrawProperties(targets.Select(t => (object)t.Transform).ToList(), true, "Transform", false);

        var firstObj = targets[0];

        var commonCompNames = firstObj.Components.Keys.Where(k => targets.All(t => t.Components.ContainsKey(k))).OrderBy(k => k, new NaturalStringComparer());

        foreach (var compName in commonCompNames) {

            var compInstances = targets.Select(object (t) => t.Components[compName]).ToList();
            DrawProperties(compInstances, true, compName, false);
        }

        DrawAddComponentButton(targets);
    }

    private void DrawAddComponentButton(List<Obj> targets) {

        if (targets.Count != 1) return;

        Spacing();
        Separator();
        Spacing();

        if (Button("Add Component", new Vector2(GetContentRegionAvail().X, 0))) OpenPopup("AddComponentPopup");

        if (!BeginPopup("AddComponentPopup")) return;

        foreach (var type in _addComponentTypes) {

            if (!Selectable(type.Name)) continue;

            var targetObj = targets[0];

            if (targetObj.Components.ContainsKey(type.Name)) continue;

            if (Activator.CreateInstance(type, targetObj) is not Component component) continue;

            var compName = type.Name;

            History.StartRecording(targetObj, $"Add Component {compName}");
            targetObj.Components[compName] = component;
            if (component.Load()) component.IsLoaded = true;
            if (Core.ActiveLevel != null) Core.ActiveLevel.IsDirty = true;

            History.StopRecording();
            if (component is Animation anim && targetObj.Components.TryGetValue("Model", out var m)) anim.GUID = (m as Model)!.GUID;
        }

        EndPopup();
    }

    private static void DrawShadowedLabel(string label) {

        AlignTextToFramePadding();
        PushFont(Fonts.ImMontserratRegular);
        var cp = GetCursorPos();
        var cleanLabel = Generators.SplitCamelCase(label);
        Text(cleanLabel);
        SetCursorPos(cp + new Vector2(0.3f, 0));
        Text(cleanLabel);
        PopFont();
        NextColumn();
    }

    private (bool changed, bool deactivated) DrawInspectorField(string id, ref object? value, Type type, List<object> targets, string? propName, string? pickerType = null) {

        var changed = false;
        var deactivated = false;

        PushItemWidth(-1); // Fill the entire column

        // Asset Picker Logic
        if (!string.IsNullOrEmpty(pickerType)) {

            PushFont(Fonts.ImFontAwesomeSmall);

            if (Button($"{Icons.FaSearch}##{id}_btn")) {

                List<(string Name, string Path, string GUID)> names = pickerType switch {

                    "ShaderAsset"    => AssetManager.GetNames<ShaderAsset>(),
                    "TextureAsset"   => AssetManager.GetNames<TextureAsset>(),
                    "ModelAsset"     => AssetManager.GetNames<ModelAsset>(),
                    "AnimationAsset" => AssetManager.GetNames<AnimationAsset>(),
                    "MaterialAsset"  => AssetManager.GetNames<MaterialAsset>(),
                    "ScriptAsset"    => AssetManager.GetNames<ScriptAsset>(),
                    _                => new List<(string, string, string)>()
                };

                _foundAssets = names.ToArray();
                _searchFilter = "";

                OpenPopup($"Picker_{id}");
            }

            if (IsItemActivated() && propName != null) targets.ForEach(t => History.StartRecording(t, propName));
            if (IsItemDeactivated()) deactivated = true;

            SameLine();

            if (Button($"{Icons.FaXMark}##{id}_clear")) {
                value = "";
                changed = true;
                deactivated = true;
            }

            if (IsItemActivated() && propName != null) targets.ForEach(t => History.StartRecording(t, propName));
            PopFont();
            SameLine();

            SetNextItemWidth(GetContentRegionAvail().X);
        }

        // Field drawing
        if (type == typeof(string)) {

            var val = (string)(value ?? "");
            var display = GetAssetDisplayValue(val, pickerType);

            if (string.IsNullOrEmpty(display)) display = val;

            if (InputTextWithHint($"##{id}", "None", ref display, 512, string.IsNullOrEmpty(pickerType) ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.ReadOnly) && string.IsNullOrEmpty(pickerType)) {

                value = display;
                changed = true;
            }
        } else if (type == typeof(float)) {

            var val = (float)(value ?? 0f);

            if (InputFloat($"##{id}", ref val)) {

                value = val;
                changed = true;
            }
        } else if (type == typeof(int)) {

            var val = (int)(value ?? 0);

            if (id.Contains("is_")) {

                var bVal = val == 1;

                if (Checkbox($"##{id}", ref bVal)) {

                    value = bVal ? 1 : 0;
                    changed = true;
                }
            } else if (InputInt($"##{id}", ref val)) {

                value = val;
                changed = true;
            }
        } else if (type == typeof(bool)) {

            var val = (bool)(value ?? false);

            if (Checkbox($"##{id}", ref val)) {

                value = val;
                changed = true;
            }
        } else if (type == typeof(Vector3)) {

            var val = (Vector3)(value ?? Vector3.Zero);

            if (InputFloat3($"##{id}", ref val)) {
                value = val;
                changed = true;
            }
        } else if (type == typeof(Bool3)) {

            var val = (Bool3)(value ?? new Bool3(false, false, false));

            PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4, 0));

            if (Checkbox($"##{id}_x", ref val.X)) {
                value = val;
                changed = true;
            }

            SameLine();
            Text("X");
            SameLine();

            if (Checkbox($"##{id}_y", ref val.Y)) {
                value = val;
                changed = true;
            }

            SameLine();
            Text("Y");
            SameLine();

            if (Checkbox($"##{id}_z", ref val.Z)) {
                value = val;
                changed = true;
            }

            SameLine();
            Text("Z");

            PopStyleVar();
        } else if (type == typeof(Vector2)) {

            var val = (Vector2)(value ?? Vector2.Zero);

            if (InputFloat2($"##{id}", ref val)) {

                value = val;
                changed = true;
            }
        } else if (type == typeof(Color)) {

            var col = (Color)(value ?? Color.White);
            var v4 = col.ToVector4();

            if (ColorEdit4($"##{id}", ref v4, ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.NoInputs)) {

                value = v4.ToColor();
                changed = true;
            }
        } else if (type.IsEnum) {

            var val = (Enum)(value ?? Activator.CreateInstance(type)!);
            var names = Enum.GetNames(type);
            var index = Array.IndexOf(names, val.ToString());

            if (Combo($"##{id}", ref index, names, names.Length)) {

                value = Enum.Parse(type, names[index]);
                changed = true;
            }
        }

        // History Logic inside Universal Control
        if (IsItemActivated() && propName != null) targets.ForEach(t => History.StartRecording(t, propName));

        if (IsItemDeactivated()) deactivated = true;

            if (IsItemHovered() && type == typeof(string) && !string.IsNullOrEmpty((string)value!)) SetTooltip(GetAssetTooltip((string)value!, pickerType));

        // Picker Popup logic
        if (BeginPopup($"Picker_{id}")) {

            SetNextItemWidth(300);
            InputTextWithHint("##filter", "Search...", ref _searchFilter, 128);
            BeginChild("##files", new Vector2(400, 400));

            var nms = _foundAssets.Select(asset => asset.Name).ToList();

            for (var i = 0; i < _foundAssets.Length; i++) {

                var asset = _foundAssets[i];
                var f = asset.Path;
                var n = nms[i];

                if (!string.IsNullOrEmpty(_searchFilter) && !f.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase)) continue;

                if (Selectable($"{n}##{asset.GUID}")) {

                    if (targets != null && propName != null) targets.ForEach(t => History.StartRecording(t, propName));

                    value = asset.GUID;
                    changed = true;
                    deactivated = true;

                    CloseCurrentPopup();
                }

                if (string.IsNullOrEmpty(n) || nms.Count(x => x == n) <= 1) continue;

                SameLine();
                TextDisabled(f);
            }

            EndChild();
            EndPopup();
        }

        PopItemWidth();
        NextColumn();

        return (changed, deactivated);
    }

    private static void DrawSectionHeader(string title, string icon, Color color, out bool open, bool showRemove = false, Action? onRemove = null, bool defaultOpen = true, Component? comp = null) {

        var flags = ImGuiTreeNodeFlags.AllowOverlap | ImGuiTreeNodeFlags.SpanFullWidth;
        if (defaultOpen) flags |= ImGuiTreeNodeFlags.DefaultOpen;

        Spacing();
        var headerPos = GetCursorScreenPos();
        var headerSize = new Vector2(GetContentRegionAvail().X, GetFrameHeight());
        GetWindowDrawList().AddRectFilled(headerPos, headerPos + headerSize, GetColorU32(ImGuiCol.Header, 0.45f), 2.0f);

        PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 3));
        PushStyleColor(ImGuiCol.Header, new Vector4(0, 0, 0, 0));
        open = TreeNodeEx($"##{title}", flags);

        if (comp != null && BeginDragDropSource()) {

            LevelBrowser.DragComponent = comp;
            SetDragDropPayload("component", IntPtr.Zero, 0);
            Text(title);
            EndDragDropSource();
        }

        PopStyleColor();
        PopStyleVar();

        SameLine();
        SetCursorPosX(GetCursorPosX() - 7.5f);
        SetCursorPosY(GetCursorPosY() + 2.5f);
        PushFont(Fonts.ImFontAwesomeSmall);
        TextColored(color.ToVector4(), icon);
        PopFont();
        SameLine();
        PushFont(Fonts.ImMontserratRegular);
        Text(title);
        PopFont();

        if (showRemove && onRemove != null) {

            SameLine();
            var removeBtnX = GetContentRegionAvail().X + GetCursorPosX() - 22;
            SetCursorPosX(removeBtnX);
            if (SmallButton($"X##rem_{title}")) onRemove();
        }

        if (open) {

            Spacing();
            PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 8));
            Columns(2, $"##{title}_cols", false);
            SetColumnWidth(0, GetWindowWidth() * 0.3f); // Reduced label width
        }
    }

    private static void EndSection(bool open) {

        if (!open) return;

        Columns(1);
        PopStyleVar();
        TreePop();
        Spacing();
    }

    private void DrawAnimationPreviewControls(Animation animation) {

        if (!animation.HasPreviewClip) return;

        Spacing();
        Separator();
        Spacing();

        var isPlaying = animation.EditorPreviewPlaying;
        PushFont(Fonts.ImFontAwesomeSmall);

        var playHovered = false;
        if (Button(isPlaying ? Icons.FaPause : Icons.FaPlay, new Vector2(30, 24)))
            if (isPlaying)
                animation.PausePreview();
            else
                animation.PlayPreview();

        playHovered = IsItemHovered();

        SameLine();

        var stopHovered = false;
        if (Button(Icons.FaStop, new Vector2(30, 24))) animation.StopPreview();

        stopHovered = IsItemHovered();

        SameLine();

        var modeHovered = false;
        if (Button(_showAnimationFrames ? Icons.FaFilm : Icons.FaClock, new Vector2(30, 24)))
            _showAnimationFrames = !_showAnimationFrames;

        modeHovered = IsItemHovered();

        PopFont();

        if (playHovered) SetTooltip(isPlaying ? "Pause" : "Play");
        if (stopHovered) SetTooltip("Stop");
        if (modeHovered) SetTooltip(_showAnimationFrames ? "Frames" : "Seconds");

        var duration = _showAnimationFrames ? animation.DurationFrames : animation.DurationSeconds;
        var value = _showAnimationFrames ? animation.CurrentFrame : animation.CurrentTime;
        var max = Math.Max(duration, 0.0001f);
        var format = _showAnimationFrames
            ? $"{value:0}f / {duration:0}f"
            : $"{value:0.00}s / {duration:0.00}s";

        SameLine();
        SetNextItemWidth(GetContentRegionAvail().X);
        BeginDisabled(duration <= 0f);
        if (SliderFloat("##animation_time", ref value, 0f, max, format))
            if (_showAnimationFrames)
                animation.CurrentFrame = value;
            else
                animation.CurrentTime = value;
        EndDisabled();
    }

    // Asset inspectors
    private void DrawAssetInspector(string path) {

        var ext = Path.GetExtension(path).ToLowerInvariant();

        if (path.EndsWith(".material.json", StringComparison.OrdinalIgnoreCase)) {

            var asset = AssetManager.GetOrImport<MaterialAsset>(path);

            if (asset != null) DrawMaterialAssetInspector(asset);
        } else if (ext is ".png" or ".jpg" or ".jpeg" or ".tga" or ".bmp") {

            var asset = AssetManager.GetOrImport<TextureAsset>(path);

            if (asset != null) DrawTextureAssetInspector(asset);
        } else if (ext is ".fbx" or ".obj" or ".gltf" or ".iqm") {

            var asset = AssetManager.GetOrImport<ModelAsset>(path) ?? AssetManager.Get<ModelAsset>(Path.GetFileNameWithoutExtension(path));

            if (asset != null) DrawModelAssetInspector(asset);
        }
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
            PopStyleVar();
        }

        EndSection(open);
        PopID();
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

            var shaderName = string.IsNullOrEmpty(mat.Data.Shader) ? "pbr" : mat.Data.Shader;
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

    private void DrawProperties(List<object> targets, bool separator, string title, bool defaultOpen = true) {

        if (targets.Count == 0) return;
        var first = targets[0];
        PushID(first.GetHashCode());

        var open = true;

        if (separator) {

            var icon = first is Component c ? c.LabelIcon : Icons.FaCube;
            var color = first is Component cc ? cc.LabelColor : Colors.GuiTypeModel;
            var isRemovable = first is Component and not Transform && targets.Count == 1;

            DrawSectionHeader(
                title,
                icon,
                color,
                out open,
                isRemovable,
                () => {

                    var comp = (first as Component)!;
                    var targetObj = comp.Obj;
                    var name = comp.GetType().Name;
                    History.StartRecording(targetObj, $"Remove {name}");
                    comp.UnloadAndQuit();
                    targetObj.Components.Remove(name);
                    if (Core.ActiveLevel != null) Core.ActiveLevel.IsDirty = true;
                    History.StopRecording();

                },
                defaultOpen,
                first as Component
            );

        } else {

            PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 8));
            Columns(2, "##props", false);
            SetColumnWidth(0, GetWindowWidth() * 0.3f); // Reduced label width
        }

        if (open) {

            foreach (var prop in first.GetType().GetProperties()) {

                var labelAttr = prop.GetCustomAttribute<LabelAttribute>();

                if (labelAttr == null) continue;

                var id = $"##prop_{_propIndex++}";
                var values = targets.Select(prop.GetValue).ToList();
                var allSame = values.All(v => Equals(v, values[0]));
                var val = allSame ? values[0] : null;

                DrawShadowedLabel(labelAttr.Value);

                var fileAttr = prop.GetCustomAttribute<FilePathAttribute>();
                var assetAttr = prop.GetCustomAttribute<FindAssetAttribute>();
                var picker = assetAttr?.TypeName ?? fileAttr?.Category;

                var (changed, deactivated) = DrawInspectorField(id, ref val, prop.PropertyType, targets, prop.Name, picker);

                if (changed) {

                    foreach (var t in targets) {

                        prop.SetValue(t, val);
                        if (t is Component comp && (fileAttr != null || assetAttr != null)) comp.UnloadAndQuit();
                    }

                    if (Core.ActiveLevel != null) Core.ActiveLevel.IsDirty = true;
                }

                if (deactivated) History.StopRecording();
            }
        }

        if (separator) {

            if (open && first is Animation animation && targets.Count == 1) {

                Columns(1);
                PopStyleVar();
                DrawAnimationPreviewControls(animation);
                TreePop();
                Spacing();

            } else
                EndSection(open);

        } else {

            Columns(1);
            PopStyleVar();
        }

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

                texture.ImportSettings.Format = formatOptions[selectedFormat];
                texture.SaveMeta();
                AssetManager.ReimportTextureAsync(texture);
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

                texture.ImportSettings.MaxSize = maxSizeOptions[selectedMaxSize];
                texture.SaveMeta();
                AssetManager.ReimportTextureAsync(texture);
            }
            NextColumn();

            DrawShadowedLabel("Resize Filter");
            BeginDisabled(!usesResizeFilter);
            var resizeOptions = new[] { "Nearest", "Bilinear", "Bicubic", "Lanczos" };
            var selectedResize = Array.IndexOf(resizeOptions, texture.ImportSettings.ResizeFilter);
            if (selectedResize < 0) selectedResize = 1;
            SetNextItemWidth(GetContentRegionAvail().X);
            if (Combo("##texture_resize_filter", ref selectedResize, resizeOptions, resizeOptions.Length)) {

                texture.ImportSettings.ResizeFilter = resizeOptions[selectedResize];
                texture.SaveMeta();
                AssetManager.ReimportTextureAsync(texture);
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

                texture.ImportSettings.Compression = compressionOptions[selectedCompression];
                texture.SaveMeta();
                AssetManager.ReimportTextureAsync(texture);
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

            if (IsItemDeactivatedAfterEdit()) {

                texture.ImportSettings.Quality = quality;
                _pendingTextureQuality[texture.GUID] = quality;
                texture.SaveMeta();
                AssetManager.ReimportTextureAsync(texture);
            }
            EndDisabled();
            NextColumn();

            DrawShadowedLabel("Texture Filter");
            var textureFilterOptions = new[] { "Point", "Bilinear", "Trilinear", "Anisotropic 4x", "Anisotropic 8x", "Anisotropic 16x" };
            var selectedTextureFilter = Array.IndexOf(textureFilterOptions, texture.ImportSettings.TextureFilter);
            if (selectedTextureFilter < 0) selectedTextureFilter = 1;
            SetNextItemWidth(GetContentRegionAvail().X);
            if (Combo("##texture_filter", ref selectedTextureFilter, textureFilterOptions, textureFilterOptions.Length)) {

                texture.ImportSettings.TextureFilter = textureFilterOptions[selectedTextureFilter];
                texture.SaveMeta();
                AssetManager.ApplyTextureFilterAsync(texture);
            }
            NextColumn();

            EndDisabled();
            Columns(1);
            PopStyleVar();
        }

        EndSection(open);
        PopID();
    }

    private static string GetAssetDisplayValue(string value, string? pickerType) {

        if (string.IsNullOrWhiteSpace(value)) return "";
        if (string.IsNullOrWhiteSpace(pickerType)) return Path.GetFileNameWithoutExtension(value);

        return pickerType switch {
            "ShaderAsset" => AssetManager.Get<ShaderAsset>(value) is { } asset ? Path.GetFileNameWithoutExtension(asset.File) : value,
            "TextureAsset" => AssetManager.Get<TextureAsset>(value) is { } asset ? Path.GetFileNameWithoutExtension(asset.File) : value,
            "ModelAsset" => AssetManager.Get<ModelAsset>(value) is { } asset ? Path.GetFileNameWithoutExtension(asset.File) : value,
            "AnimationAsset" => AssetManager.Get<AnimationAsset>(value) is { } asset ? Path.GetFileNameWithoutExtension(asset.File) : value,
            "MaterialAsset" => AssetManager.Get<MaterialAsset>(value) is { } asset ? Path.GetFileNameWithoutExtension(asset.File) : value,
            "ScriptAsset" => AssetManager.Get<ScriptAsset>(value) is { } asset ? Path.GetFileNameWithoutExtension(asset.File) : value,
            _ => Path.GetFileNameWithoutExtension(value)
        };
    }

    private static string GetAssetTooltip(string value, string? pickerType) {

        if (string.IsNullOrWhiteSpace(pickerType)) return value;

        return pickerType switch {
            "ShaderAsset" => AssetManager.GetPath<ShaderAsset>(value) ?? value,
            "TextureAsset" => AssetManager.GetPath<TextureAsset>(value) ?? value,
            "ModelAsset" => AssetManager.GetPath<ModelAsset>(value) ?? value,
            "AnimationAsset" => AssetManager.GetPath<AnimationAsset>(value) ?? value,
            "MaterialAsset" => AssetManager.GetPath<MaterialAsset>(value) ?? value,
            "ScriptAsset" => AssetManager.GetPath<ScriptAsset>(value) ?? value,
            _ => value
        };
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

    private static string FormatFileSize(long bytes) {

        if (bytes < 0) return "-";

        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1) {

            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }
}
