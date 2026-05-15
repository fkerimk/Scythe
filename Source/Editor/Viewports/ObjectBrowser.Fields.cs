using System.Numerics;
using System.Reflection;
using EnumsNET;
using ImGuiNET;
using Raylib_cs;
using static ImGuiNET.ImGui;

internal partial class ObjectBrowser {

    private static float _labelDragDelta;
    private static bool _labelWasActivated;
    private static bool _labelWasDeactivated;

    private static void DrawSectionHeader(string title, string icon, Color color, out bool open, bool showRemove = false, Action? onRemove = null, bool defaultOpen = true, Component? comp = null, string? subtitle = null) {

        var flags = ImGuiTreeNodeFlags.AllowOverlap | ImGuiTreeNodeFlags.SpanFullWidth;
        if (defaultOpen) flags |= ImGuiTreeNodeFlags.DefaultOpen;

        Spacing();
        var headerPos = GetCursorScreenPos();
        var headerSize = new Vector2(GetContentRegionAvail().X, GetFrameHeight());
        GetWindowDrawList().AddRectFilled(headerPos, headerPos + headerSize, GetColorU32(ImGuiCol.Header, 0.45f), 2.0f);

        PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 3));
        PushStyleColor(ImGuiCol.Header, new Vector4(0, 0, 0, 0));
        SetNextItemOpen(defaultOpen, ImGuiCond.Once);
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

        if (!string.IsNullOrWhiteSpace(subtitle)) {
            SameLine();
            PushStyleColor(ImGuiCol.Text, Colors.GuiTextDisabled.ToVector4());
            Text($"({subtitle})");
            PopStyleColor();
        }

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

    private static bool DrawStringField(string id, ref object? value, string? pickerType) {

        var val = (string)(value ?? "");

        if (!string.IsNullOrWhiteSpace(pickerType)) {
            var display = GetAssetDisplayValue(val, pickerType);
            if (string.IsNullOrEmpty(display)) display = "None";

            var clicked = Button(display, new Vector2(CalcItemWidth(), 0f));

            if (clicked && !string.IsNullOrWhiteSpace(val))
                NavigateToPickerReference(val, pickerType, typeof(string));

            return false;
        }

        var textValue = GetAssetDisplayValue(val, pickerType);

        if (string.IsNullOrEmpty(textValue)) textValue = val;

        if (!InputTextWithHint($"##{id}", "None", ref textValue, 512, string.IsNullOrEmpty(pickerType) ? ImGuiInputTextFlags.None : ImGuiInputTextFlags.ReadOnly) || !string.IsNullOrEmpty(pickerType))
            return false;

        value = textValue;
        return true;
    }

    private static bool DrawSceneReferenceField(string id, ref object? value, Type type) {

        var display = GetSceneReferenceDisplayValue(value, type);
        if (string.IsNullOrEmpty(display)) display = "None";

        var clicked = Button(display, new Vector2(CalcItemWidth(), 0f));

        if (clicked && value != null)
            NavigateToPickerReference(value, null, type);

        return false;
    }

    private static bool DrawFloatField(string id, ref object? value, string? _) {

        var val = (float)(value ?? 0f);
        var changed = DragFloat($"##{id}", ref val, 0.1f);

        if (_labelDragDelta != 0) {
            val += _labelDragDelta * 0.1f;
            changed = true;
        }

        if (!changed) return false;

        value = val;
        return true;
    }

    private static bool DrawDoubleField(string id, ref object? value, string? _) {

        var val = (double)(value ?? 0d);
        var changed = InputDouble($"##{id}", ref val, 0.1, 1.0, "%.3f");

        if (_labelDragDelta != 0) {
            val += _labelDragDelta * 0.1d;
            changed = true;
        }

        if (!changed) return false;

        value = val;
        return true;
    }

    private static bool DrawIntField(string id, ref object? value, string? _) {

        var val = (int)(value ?? 0);
        if (id.Contains("is_")) {
            var boolValue = val == 1;
            if (!Checkbox($"##{id}", ref boolValue)) return false;

            value = boolValue ? 1 : 0;
            return true;
        }

        var changed = DragInt($"##{id}", ref val, 0.1f);

        if (_labelDragDelta != 0) {
            val += (int)MathF.Round(_labelDragDelta * 0.1f);
            changed = true;
        }

        if (!changed) return false;

        value = val;
        return true;
    }

    private static bool DrawBoolField(string id, ref object? value, string? _) {

        var val = (bool)(value ?? false);
        if (!Checkbox($"##{id}", ref val)) return false;

        value = val;
        return true;
    }

    private static bool DrawVector2Field(string id, ref object? value, string? _) {

        var val = (Vector2)(value ?? Vector2.Zero);
        var changed = DragFloat2($"##{id}", ref val, 0.1f);

        if (_labelDragDelta != 0) {
            val.X += _labelDragDelta * 0.1f;
            val.Y += _labelDragDelta * 0.1f;
            changed = true;
        }

        if (!changed) return false;

        value = val;
        return true;
    }

    private static bool DrawVector3Field(string id, ref object? value, string? _) {

        var val = (Vector3)(value ?? Vector3.Zero);
        var changed = DragFloat3($"##{id}", ref val, 0.1f);

        if (_labelDragDelta != 0) {
            val.X += _labelDragDelta * 0.1f;
            val.Y += _labelDragDelta * 0.1f;
            val.Z += _labelDragDelta * 0.1f;
            changed = true;
        }

        if (!changed) return false;

        value = val;
        return true;
    }

    private static bool DrawColorField(string id, ref object? value, string? _) {

        var col = (Color)(value ?? Color.White);
        var v4 = col.ToVector4();
        if (!ColorEdit4($"##{id}", ref v4, ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.NoInputs)) return false;

        value = v4.ToColor();
        return true;
    }

    private static bool DrawBool3Field(string id, ref object? value, string? _) {

        var val = (Bool3)(value ?? new Bool3(false, false, false));
        var changed = false;

        PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4, 0));

        if (Checkbox($"##{id}_x", ref val.X)) changed = true;
        SameLine();
        Text("X");
        SameLine();

        if (Checkbox($"##{id}_y", ref val.Y)) changed = true;
        SameLine();
        Text("Y");
        SameLine();

        if (Checkbox($"##{id}_z", ref val.Z)) changed = true;
        SameLine();
        Text("Z");

        PopStyleVar();

        if (!changed) return false;

        value = val;
        return true;
    }

    private static bool DrawEnumField(string id, ref object? value, Type type) {

        var val = (Enum)(value ?? Activator.CreateInstance(type)!);
        var names = Enums.GetNames(type, EnumMemberSelection.All).ToArray();
        var index = Array.IndexOf(names, val.ToString());
        if (!Combo($"##{id}", ref index, names, names.Length)) return false;

        value = Enums.Parse(type, names[index]);
        return true;
    }

    private static string GetSceneReferenceDisplayValue(object? value, Type type) {

        if (value == null) return "";

        if (value is SceneReferenceValue reference)
            return FormatSceneReferenceDisplay(reference, type);

        return value switch {
            Obj obj => string.Join("/", obj.GetPathFromRoot()),
            ScytheScript script => $"{string.Join("/", script.Obj.GetPathFromRoot())}/{script.GetType().Name}",
            Component component => $"{string.Join("/", component.Obj.GetPathFromRoot())}/{component.GetType().Name}",
            _ => ""
        };
    }

    private static string FormatSceneReferenceDisplay(SceneReferenceValue reference, Type type) {

        var path = reference.Path.Count == 0
            ? reference.IsPrefabLocal ? "Self" : "Level"
            : string.Join("/", reference.Path.Select(segment => segment.Name));

        if (type == typeof(Obj)) return path;

        if (typeof(ScytheScript).IsAssignableFrom(type))
            return $"{path}/{GetSceneReferenceTypeLabel(reference.ScriptType, type.Name)}";

        var componentName = string.IsNullOrWhiteSpace(reference.ComponentType) ? type.Name : reference.ComponentType;
        return $"{path}/{componentName}";
    }

    private static string GetSceneReferenceTypeLabel(string? typeName, string fallback) {

        if (string.IsNullOrWhiteSpace(typeName)) return fallback;

        var lastDot = typeName.LastIndexOf('.');
        return lastDot >= 0 && lastDot < typeName.Length - 1 ? typeName[(lastDot + 1)..] : typeName;
    }

    private static void EndSection(bool open) {

        if (!open) return;

        Columns(1);
        PopStyleVar();
        TreePop();
        Spacing();
    }

    private static void DrawShadowedLabel(string label, bool highlighted = false) {

        AlignTextToFramePadding();
        PushFont(Fonts.ImMontserratRegular);
        if (highlighted) PushStyleColor(ImGuiCol.Text, Colors.Primary.ToVector4());
        var cleanLabel = Generators.SplitCamelCase(label);

        var screenPos = GetCursorScreenPos();
        var labelSize = CalcTextSize(cleanLabel);

        // Draggable area covering the label
        InvisibleButton($"##drag_{label}", new Vector2(GetContentRegionAvail().X, GetFrameHeight()));
        if (IsItemHovered()) SetMouseCursor(ImGuiMouseCursor.ResizeEW);

        if (IsItemActivated()) _labelWasActivated = true;
        if (IsItemDeactivated()) _labelWasDeactivated = true;

        if (IsItemActive() && IsMouseDragging(ImGuiMouseButton.Left)) {
            _labelDragDelta = GetIO().MouseDelta.X;
        }

        // Return to start position to draw the text
        SetCursorScreenPos(screenPos);

        var shadowColor = ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.2f));
        GetWindowDrawList().AddText(screenPos + new Vector2(1f, 1f), shadowColor, cleanLabel);
        TextUnformatted(cleanLabel);

        if (highlighted) PopStyleColor();
        PopFont();
        NextColumn();
    }

    private static string GetAssetDisplayValue(string value, string? pickerType) {

        if (string.IsNullOrWhiteSpace(value)) return "";
        if (string.IsNullOrWhiteSpace(pickerType)) return Path.GetFileNameWithoutExtension(value);
        if (SupportsCollectionPicker(pickerType) && CollectionData.TryGetSelectionCollectionInfo(value, pickerType, out var display, out _)) return display;

        return TryGetPickerTypeMetadata(pickerType, out var metadata)
            ? metadata.GetDisplayValue(value)
            : Path.GetFileNameWithoutExtension(value);
    }

    private static string GetAssetTooltip(string value, string? pickerType) {

        if (string.IsNullOrWhiteSpace(pickerType)) return value;
        if (SupportsCollectionPicker(pickerType) && CollectionData.TryGetSelectionCollectionInfo(value, pickerType, out _, out var tooltip)) return tooltip;

        return TryGetPickerTypeMetadata(pickerType, out var metadata)
            ? metadata.GetTooltip(value)
            : value;
    }

    private static InspectableProperty[] GetInspectableProperties(Type type) {

        if (_inspectablePropertyCache.TryGetValue(type, out var cached))
            return cached;

        var inspectableProperties = type
            .GetProperties()
            .SelectMany(property => {
                var label = property.GetCustomAttribute<LabelAttribute>();
                if (label == null) return Array.Empty<InspectableProperty>();

                var filePath = property.GetCustomAttribute<FilePathAttribute>();
                var findAsset = property.GetCustomAttribute<FindAssetAttribute>();

                return [
                    new InspectableProperty(
                        property,
                        label.Value,
                        findAsset?.TypeName ?? filePath?.Category,
                        filePath != null || findAsset != null
                    )
                ];
            })
            .ToArray();

        _inspectablePropertyCache[type] = inspectableProperties;
        return inspectableProperties;
    }
}
