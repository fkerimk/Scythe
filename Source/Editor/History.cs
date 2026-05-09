using System.Collections;
using System.Reflection;

internal class HistoryStack {

    private readonly List<HistoryRecord> _records = [];
    private int _index = -1;
    private HistoryRecord? _active;

    public bool CanUndo => _index >= 0;
    public bool CanRedo => _index < _records.Count - 1;
    public bool CanExtend => _index == _records.Count - 1;

    public void Clear() {
        _records.Clear();
        _index = -1;
        _active = null;
    }

    public bool UpdateLastRecord(object reference, string description) {
        if (_active != null) return false;
        if (_records.Count == 0 || _index != _records.Count - 1) return false;

        var last = _records[_index];

        if (last.Description != description) return false;

        var snapshot = last.Snapshots.FirstOrDefault(s => s.Target == reference);

        if (snapshot == null) return false;

        snapshot.EndState = History.CaptureState(reference);

        return true;
    }

    public void Execute(string description, Action redo, Action undo) {
        if (_index < _records.Count - 1) _records.RemoveRange(_index + 1, _records.Count - (_index + 1));

        var record = new HistoryRecord(description) { UndoAction = undo, RedoAction = redo };

        redo();

        _records.Add(record);
        _index = _records.Count - 1;
        Notifications.Show(description);
    }

    public void StartRecording(object reference, string? description = null) {
        if (_active != null && description != null && _active.Description != description) StopRecording();

        _active ??= new HistoryRecord(description);

        if (_active.Snapshots.All(s => s.Target != reference)) _active.Snapshots.Add(new StateSnapshot(reference));
    }

    public void StopRecording() {
        if (_active == null) return;

        var changed = false;

        foreach (var snapshot in _active.Snapshots) {
            snapshot.EndState = History.CaptureState(snapshot.Target);
            if (!History.StateEquals(snapshot.StartState, snapshot.EndState)) changed = true;
        }

        if (changed || _active.UndoAction != null || _active.RedoAction != null) {
            if (_index < _records.Count - 1) _records.RemoveRange(_index + 1, _records.Count - (_index + 1));

            _records.Add(_active);
            _index = _records.Count - 1;
        }

        _active = null;
    }

    public void SetUndoAction(Action action) {
        if (_active != null) _active.UndoAction = action;
    }

    public void SetRedoAction(Action action) {
        if (_active != null) _active.RedoAction = action;
    }

    public void Undo() {
        if (!CanUndo) return;

        var record = _records[_index];

        record.UndoAction?.Invoke();
        foreach (var snapshot in record.Snapshots) History.RestoreState(snapshot.Target, snapshot.StartState);

        Notifications.Show("Undo: " + record.Description);
        _index--;
    }

    public void Redo() {
        if (!CanRedo) return;

        _index++;
        var record = _records[_index];

        record.RedoAction?.Invoke();
        foreach (var snapshot in record.Snapshots) History.RestoreState(snapshot.Target, snapshot.EndState);

        Notifications.Show("Redo: " + record.Description);
    }

    private class HistoryRecord(string? description) {

        public readonly string Description = description ?? "Action";
        public readonly List<StateSnapshot> Snapshots = [];
        public Action? UndoAction;
        public Action? RedoAction;
    }

    private class StateSnapshot(object target) {

        public readonly object Target = target;
        public readonly object?[] StartState = History.CaptureState(target);
        public object?[] EndState = [];
    }
}

internal static class History {

    private static readonly HistoryStack Global = new();

    public static bool CanUndo => Global.CanUndo;
    public static bool CanRedo => Global.CanRedo;

    public static void Clear() => Global.Clear();

    public static void Execute(string description, Action redo, Action undo) {
        if (Core.IsPlaying) {
            redo();

            return;
        }

        Global.Execute(description, redo, undo);
    }

    public static void StartRecording(object reference, string? description = null) {
        if (Core.IsPlaying) return;

        Global.StartRecording(reference, description);
    }

    public static void StopRecording() {
        if (Core.IsPlaying) return;

        Global.StopRecording();
    }

    public static void SetUndoAction(Action action) => Global.SetUndoAction(action);
    public static void SetRedoAction(Action action) => Global.SetRedoAction(action);

    public static void Undo() => Global.Undo();
    public static void Redo() => Global.Redo();

    // --- State Capture Logic (Exposed for HistoryStack) ---

    public static object?[] CaptureState(object target) {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var type = target.GetType();

        var props = type.GetProperties(flags).Where(f => Attribute.IsDefined(f, typeof(RecordHistoryAttribute))).OrderBy(p => p.Name).Select(f => CloneValue(f.GetValue(target)));

        var fields = type.GetFields(flags).Where(f => Attribute.IsDefined(f, typeof(RecordHistoryAttribute))).OrderBy(f => f.Name).Select(f => CloneValue(f.GetValue(target)));

        return props.Concat(fields).ToArray();
    }

    public static void RestoreState(object target, object?[] state) {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var type = target.GetType();

        var props = type.GetProperties(flags).Where(f => Attribute.IsDefined(f, typeof(RecordHistoryAttribute))).OrderBy(p => p.Name).ToArray();

        var fields = type.GetFields(flags).Where(f => Attribute.IsDefined(f, typeof(RecordHistoryAttribute))).OrderBy(f => f.Name).ToArray();

        var i = 0;
        foreach (var p in props) p.SetValue(target, state[i++]);
        foreach (var f in fields) f.SetValue(target, state[i++]);

        if (target is Component comp)
            comp.UnloadAndQuit();
        else if (target is ScriptAsset scriptAsset) {
            scriptAsset.SaveMeta();
            scriptAsset.ApplyConfigToScripts();
        }
        else if (target is MaterialAsset mat) {
            mat.Save();
            mat.ApplyChanges();
        } else if (target is ModelAsset model) {
            model.ApplySettings();
            model.SaveSettings();
        }
    }

    private static object? CloneValue(object? val) {
        if (val == null) return null;
        if (val is string) return val;
        if (val.GetType().IsValueType) return val;
        return ObjectGraph.DeepClone(val);
    }

    public static bool StateEquals(object?[] s1, object?[] s2) {
        if (s1.Length != s2.Length) return false;

        for (int i = 0; i < s1.Length; i++)
            if (!ValueEquals(s1[i], s2[i]))
                return false;

        return true;
    }

    private static bool ValueEquals(object? v1, object? v2) {
        if (v1 == null && v2 == null) return true;
        if (v1 == null || v2 == null) return false;
        return ObjectGraph.AreEqual(v1, v2);
    }
}
