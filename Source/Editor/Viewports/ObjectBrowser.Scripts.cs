using System.Numerics;
using System.Reflection;
using EnumsNET;
using ImGuiNET;
using Raylib_cs;
using static ImGuiNET.ImGui;

internal partial class ObjectBrowser {

    private void DrawScriptFieldRows(List<object> targets, ScriptAsset asset, ScriptFieldStorageKind kind) {

        var fields = ScriptFieldUtility.GetFields(asset.ScriptType!, kind);
        if (fields.Length == 0) return;

        foreach (var field in fields) {

            var defaultValue = ScriptFieldUtility.GetCodeDefaultValue(asset.ScriptType, field);

            object? value;
            var picker = field.GetCustomAttribute<FindAssetAttribute>()?.TypeName
                         ?? field.GetCustomAttribute<FilePathAttribute>()?.Category
                         ?? GetScenePickerType(field.FieldType);
            var isOverridden = false;

            if (kind == ScriptFieldStorageKind.Config) {

                var assets = targets.Cast<ScriptAsset>().ToList();
                var values = assets.Select(scriptAsset => scriptAsset.GetConfigFieldValue(field)).ToList();
                value = values.All(val => ScriptFieldUtility.ValueEquals(val, values[0])) ? values[0] : null;
                isOverridden = values.Any(val => !ScriptFieldUtility.ValueEquals(val, defaultValue));
                DrawShadowedLabel(ScriptFieldUtility.GetLabel(field), isOverridden);

                var (changed, deactivated) = DrawInspectorField($"##script_cfg_{_propIndex++}", ref value, field.FieldType, targets, field.Name, picker, showResetButton: true, highlightOverride: isOverridden, resetValue: defaultValue);

                if (changed)
                    foreach (var scriptAsset in assets)
                        scriptAsset.SetConfigFieldValue(field, value);

                if (deactivated) History.StopRecording();
                continue;
            }

            var scripts = targets.Cast<Script>().ToList();
            var exposedValues = scripts.Select(script => script.GetExposeFieldValue(field, asset)).ToList();
            value = exposedValues.All(val => ScriptFieldUtility.ValueEquals(val, exposedValues[0])) ? exposedValues[0] : null;
            var (prefabOverride, prefabResetValue) = GetScriptExposePrefabOverrideState(scripts, asset, field);
            var resetValue = prefabOverride ? prefabResetValue : defaultValue;
            isOverridden = scripts.All(script => script.Obj.FindPrefabRoot() != null)
                ? prefabOverride
                : exposedValues.Any(val => !ScriptFieldUtility.ValueEquals(val, defaultValue));
            var applyOverride = GetScriptExposePrefabApplyAction(scripts, field);
            var applyOverrideWithHistory = GetScriptExposePrefabApplyHistoryAction(scripts, asset, field, picker, value);
            DrawShadowedLabel(ScriptFieldUtility.GetLabel(field), isOverridden);

            var (fieldChanged, fieldDeactivated) = DrawInspectorField($"##script_exp_{_propIndex++}", ref value, field.FieldType, targets, field.Name, picker, showResetButton: isOverridden, highlightOverride: isOverridden, resetValue: resetValue, applyOverride: applyOverride, applyOverrideWithHistory: applyOverrideWithHistory);

            if (fieldChanged)
                foreach (var script in scripts)
                    script.SetExposeFieldValue(field, value);

            if (fieldDeactivated) History.StopRecording();
        }
    }

    private static (bool HighlightOverride, object? ResetValue) GetScriptExposePrefabOverrideState(List<Script> scripts, ScriptAsset asset, FieldInfo field) {

        if (scripts.Count == 0) return (false, null);
        if (!scripts.All(script => script.Obj.FindPrefabRoot() != null)) return (false, null);

        var sourceValues = new List<object?>();

        foreach (var script in scripts) {
            if (!PrefabUtility.TryGetSourceScriptFieldValue(script, field, out var sourceValue))
                return (false, null);

            sourceValues.Add(sourceValue);
        }

        var resetValue = sourceValues.All(val => ScriptFieldUtility.ValueEquals(val, sourceValues[0])) ? sourceValues[0] : null;

        for (var index = 0; index < scripts.Count; index++) {
            var currentValue = scripts[index].GetExposeFieldValue(field, asset);
            if (!ScriptFieldUtility.ValueEquals(currentValue, sourceValues[index]))
                return (true, resetValue);
        }

        return (false, resetValue);
    }

    private static Action? GetScriptExposePrefabApplyAction(List<Script> scripts, FieldInfo field) {

        if (scripts.Count != 1) return null;

        var script = scripts[0];
        return PrefabUtility.TryGetSourceScriptFieldValue(script, field, out _)
            ? () => PrefabUtility.ApplyScriptExposeFieldToPrefab(script, field, script.GetAsset() is { } asset ? script.GetExposeFieldValue(field, asset) : null)
            : null;
    }

    private static Action? GetScriptExposePrefabApplyHistoryAction(List<Script> scripts, ScriptAsset asset, FieldInfo field, string? pickerType, object? value) {

        if (scripts.Count != 1) return null;

        var script = scripts[0];
        if (!PrefabUtility.TryGetSourcePrefabFile(script.Obj, out var prefabFile) || !File.Exists(prefabFile)) return null;
        if (!PrefabUtility.TryGetSourceScriptFieldValue(script, field, out _)) return null;

        return () => {
            var beforeLocalValue = CloneApplyHistoryValue(script.GetExposeFieldValue(field, asset));
            using var transaction = History.Begin($"Apply {field.Name} To Prefab");
            transaction.CapturePath(prefabFile);
            transaction.After(
                redo: () => {
                    if (!PrefabUtility.RefreshSourcePrefabFile(prefabFile)) return;
                    script.SetExposeFieldValue(field, value);
                    script.SetPrefabOverride(nameof(Script.ExposedValues), false);
                },
                undo: () => {
                    if (!PrefabUtility.RefreshSourcePrefabFile(prefabFile)) return;
                    script.SetExposeFieldValue(field, beforeLocalValue);
                    script.SetPrefabOverride(nameof(Script.ExposedValues), true);
                }
            );
            PrefabUtility.ApplyScriptExposeFieldToPrefab(script, field, value);
            if (transaction.Commit()) Notifications.Show(transaction.Description);
        };
    }
}
