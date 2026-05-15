using System.Reflection;

internal sealed class HistoryTransaction : IDisposable {
    private readonly List<IHistoryOperation> _operations = [];
    private Action? _pendingUndoAction;
    private Action? _pendingRedoAction;
    private bool _committed;

    public HistoryTransaction(string description) {
        Description = string.IsNullOrWhiteSpace(description) ? "Action" : description;
    }

    public string Description { get; }

    public void Capture(object target) {

        if (_operations.OfType<ObjectStateOperation>().Any(operation => ReferenceEquals(operation.Target, target))) return;
        _operations.Add(new ObjectStateOperation(target));
    }

    public void CapturePath(string path) {

        var fullPath = Path.GetFullPath(path);
        if (_operations.OfType<PathStateOperation>().Any(operation => string.Equals(operation.Path, fullPath, StringComparison.OrdinalIgnoreCase))) return;
        _operations.Add(new PathStateOperation(fullPath));
    }

    public void Do(Action redo, Action undo) {
        _operations.Add(new DelegateOperation(redo, undo));
    }

    public void After(Action redo, Action undo) {
        _operations.Add(new AfterRestoreOperation(redo, undo));
    }

    public void SetUndoAction(Action action) => _pendingUndoAction = action;
    public void SetRedoAction(Action action) => _pendingRedoAction = action;

    public bool Commit() {

        if (_committed) return false;

        FlushPendingAction();

        foreach (var operation in _operations)
            operation.CaptureAfter();

        _committed = true;

        if (!HasChanges) {
            DisposeOperations();
            return false;
        }

        History.Push(this);
        return true;
    }

    public void Dispose() {
        if (_committed) return;
        DisposeOperations();
    }

    internal bool HasChanges => _operations.Any(operation => operation.HasChanges);

    internal void Undo() {

        foreach (var operation in _operations.OrderBy(operation => operation.UndoOrder))
            operation.Undo();
    }

    internal void Redo() {

        foreach (var operation in _operations.OrderBy(operation => operation.RedoOrder))
            operation.Redo();
    }

    internal void DisposeCommitted() => DisposeOperations();

    private void DisposeOperations() {

        foreach (var operation in _operations)
            operation.Dispose();

        _operations.Clear();
    }

    private void FlushPendingAction() {

        if (_pendingUndoAction == null && _pendingRedoAction == null) return;
        _operations.Add(new DelegateOperation(_pendingRedoAction, _pendingUndoAction));
        _pendingUndoAction = null;
        _pendingRedoAction = null;
    }
}

internal static class History {
    private const BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly List<HistoryTransaction> Records = [];
    private static int _index = -1;
    private static HistoryTransaction? _activeTransaction;

    public static bool CanUndo => _index >= 0;
    public static bool CanRedo => _index < Records.Count - 1;

    public static void Clear() {

        foreach (var record in Records)
            record.DisposeCommitted();

        Records.Clear();
        _index = -1;
        _activeTransaction?.Dispose();
        _activeTransaction = null;
    }

    public static HistoryTransaction Begin(string description) {

        if (Core.IsPlaying) return new HistoryTransaction(description);

        if (_activeTransaction != null && !string.Equals(_activeTransaction.Description, description, StringComparison.Ordinal))
            StopRecording();

        _activeTransaction ??= new HistoryTransaction(description);
        return _activeTransaction;
    }

    public static void Execute(string description, Action redo, Action undo) {

        if (Core.IsPlaying) {
            redo();
            return;
        }

        using var transaction = new HistoryTransaction(description);
        transaction.Do(redo, undo);
        redo();
        transaction.Commit();
        Notifications.Show(description);
    }

    public static void Record(string description, Action action, params object[] targets) {

        if (Core.IsPlaying) {
            action();
            return;
        }

        using var transaction = new HistoryTransaction(description);

        foreach (var target in targets)
            transaction.Capture(target);

        action();
        if (transaction.Commit()) Notifications.Show(description);
    }

    public static void RecordPathChange(string description, Action action, params string[] paths) {

        if (Core.IsPlaying) {
            action();
            return;
        }

        using var transaction = new HistoryTransaction(description);

        foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
            transaction.CapturePath(path);

        action();
        if (transaction.Commit()) Notifications.Show(description);
    }

    public static void StartRecording(object reference, string? description = null) {

        if (Core.IsPlaying) return;
        if (reference == null) return;

        var transaction = Begin(description ?? _activeTransaction?.Description ?? "Action");
        transaction.Capture(reference);
    }

    public static void StopRecording() {

        if (Core.IsPlaying) return;
        if (_activeTransaction == null) return;

        var transaction = _activeTransaction;
        _activeTransaction = null;

        using (transaction) {
            if (transaction.Commit()) Notifications.Show(transaction.Description);
        }
    }

    public static void SetUndoAction(Action action) {

        if (Core.IsPlaying) return;
        _activeTransaction?.SetUndoAction(action);
    }

    public static void SetRedoAction(Action action) {

        if (Core.IsPlaying) return;
        _activeTransaction?.SetRedoAction(action);
    }

    public static void CapturePath(string path, string? description = null) {

        if (Core.IsPlaying) return;
        if (string.IsNullOrWhiteSpace(path)) return;

        var transaction = Begin(description ?? _activeTransaction?.Description ?? "Action");
        transaction.CapturePath(path);
    }

    public static void Undo() {

        if (!CanUndo) return;

        var record = Records[_index];
        record.Undo();
        Notifications.Show("Undo: " + record.Description);
        _index--;
    }

    public static void Redo() {

        if (!CanRedo) return;

        _index++;
        var record = Records[_index];
        record.Redo();
        Notifications.Show("Redo: " + record.Description);
    }

    internal static void Push(HistoryTransaction transaction) {

        if (_index < Records.Count - 1) {
            foreach (var record in Records.Skip(_index + 1).ToList())
                record.DisposeCommitted();

            Records.RemoveRange(_index + 1, Records.Count - (_index + 1));
        }

        Records.Add(transaction);
        _index = Records.Count - 1;
    }

    public static object?[] CaptureState(object target) {
        var props = GetRecordedProperties(target.GetType())
            .Select(property => CloneValue(property.GetValue(target)));

        var fields = GetRecordedFields(target.GetType())
            .Select(field => CloneValue(field.GetValue(target)));

        return props.Concat(fields).ToArray();
    }

    public static void RestoreState(object target, object?[] state) {
        var index = 0;

        foreach (var property in GetRecordedProperties(target.GetType()))
            property.SetValue(target, state[index++]);

        foreach (var field in GetRecordedFields(target.GetType()))
            field.SetValue(target, state[index++]);

        ApplyPostRestoreEffects(target);
    }

    public static bool StateEquals(object?[] left, object?[] right) {

        if (left.Length != right.Length) return false;

        for (var index = 0; index < left.Length; index++)
            if (!ValueEquals(left[index], right[index]))
                return false;

        return true;
    }

    private static object? CloneValue(object? value) {

        if (value == null) return null;
        if (value is string) return value;
        if (value.GetType().IsValueType) return value;
        return ObjectGraph.DeepClone(value);
    }

    private static bool ValueEquals(object? left, object? right) {

        if (left == null && right == null) return true;
        if (left == null || right == null) return false;
        return ObjectGraph.AreEqual(left, right);
    }

    private static PropertyInfo[] GetRecordedProperties(Type type) =>
        type.GetProperties(InstanceFlags)
            .Where(property => Attribute.IsDefined(property, typeof(RecordHistoryAttribute)))
            .OrderBy(property => property.Name)
            .ToArray();

    private static FieldInfo[] GetRecordedFields(Type type) =>
        type.GetFields(InstanceFlags)
            .Where(field => Attribute.IsDefined(field, typeof(RecordHistoryAttribute)))
            .OrderBy(field => field.Name)
            .ToArray();

    private static void ApplyPostRestoreEffects(object target) {

        PrefabUtility.RefreshOverrideState(target);

        if (target is Component component)
            component.UnloadAndQuit();
        else if (target is ProjectConfig projectConfig)
            projectConfig.Save();
        else if (target is Level level) {
            level.IsDirty = true;
            level.Save();
            if (ReferenceEquals(Core.ActiveLevel, level)) Core.ApplyLevelVisualSettings();
        } else if (target is LevelAsset levelAsset) {
            levelAsset.SkyboxAmbientIntensity = Math.Clamp(levelAsset.SkyboxAmbientIntensity, 0.0f, 1.0f);
            levelAsset.SaveSettings();
            levelAsset.ApplyToActiveLevelIfOpen();
        } else if (target is ScriptAsset scriptAsset) {
            scriptAsset.SaveMeta();
            scriptAsset.ApplyConfigToScripts();
        } else if (target is MaterialAsset material) {
            material.Save();
            material.ApplyChanges();
        } else if (target is TextureAsset texture) {
            texture.SaveMeta();
            texture.ApplyTextureFilter();
            AssetManager.ReimportTextureAsync(texture);
        } else if (target is ModelAsset model) {
            model.ApplySettings();
            model.SaveSettings();
        }
    }
}

internal interface IHistoryOperation : IDisposable {
    bool HasChanges { get; }
    int UndoOrder { get; }
    int RedoOrder { get; }
    void CaptureAfter();
    void Undo();
    void Redo();
}

internal sealed class ObjectStateOperation : IHistoryOperation {
    public ObjectStateOperation(object target) {
        Target = target;
        BeforeState = History.CaptureState(target);
    }

    public object Target { get; }
    private object?[] BeforeState { get; }
    private object?[] AfterState { get; set; } = [];

    public bool HasChanges => History.StateEquals(BeforeState, AfterState) == false;
    public int UndoOrder => 0;
    public int RedoOrder => 0;

    public void CaptureAfter() => AfterState = History.CaptureState(Target);
    public void Undo() => History.RestoreState(Target, BeforeState);
    public void Redo() => History.RestoreState(Target, AfterState);
    public void Dispose() { }
}

internal sealed class PathStateOperation : IHistoryOperation {
    public PathStateOperation(string path) {
        Path = path;
        Before = PathSnapshot.Capture(path);
    }

    public string Path { get; }
    private PathSnapshot Before { get; }
    private PathSnapshot? After { get; set; }

    public bool HasChanges => After != null && !Before.EqualsSnapshot(After);
    public int UndoOrder => 0;
    public int RedoOrder => 0;

    public void CaptureAfter() => After = PathSnapshot.Capture(Path);
    public void Undo() => Before.RestoreTo(Path);
    public void Redo() => After?.RestoreTo(Path);

    public void Dispose() {
        Before.Dispose();
        After?.Dispose();
    }
}

internal sealed class DelegateOperation(Action? redo, Action? undo) : IHistoryOperation {
    public bool HasChanges => redo != null || undo != null;
    public int UndoOrder => -100;
    public int RedoOrder => -100;
    public void CaptureAfter() { }
    public void Undo() => undo?.Invoke();
    public void Redo() => redo?.Invoke();
    public void Dispose() { }
}

internal sealed class AfterRestoreOperation(Action? redo, Action? undo) : IHistoryOperation {
    public bool HasChanges => redo != null || undo != null;
    public int UndoOrder => 100;
    public int RedoOrder => 100;
    public void CaptureAfter() { }
    public void Undo() => undo?.Invoke();
    public void Redo() => redo?.Invoke();
    public void Dispose() { }
}

internal sealed class PathSnapshot : IDisposable {
    private readonly string? _backupPath;

    private PathSnapshot(bool exists, bool isDirectory, string originalPath, string? backupPath) {
        Exists = exists;
        IsDirectory = isDirectory;
        OriginalPath = originalPath;
        _backupPath = backupPath;
    }

    public bool Exists { get; }
    public bool IsDirectory { get; }
    public string OriginalPath { get; }

    public static PathSnapshot Capture(string path) {

        var fullPath = Path.GetFullPath(path);

        if (File.Exists(fullPath)) {
            var backupPath = AllocateBackupPath(Path.GetExtension(fullPath));
            File.Copy(fullPath, backupPath, overwrite: true);
            return new PathSnapshot(exists: true, isDirectory: false, fullPath, backupPath);
        }

        if (Directory.Exists(fullPath)) {
            var backupPath = AllocateBackupPath("");
            Directory.CreateDirectory(backupPath);
            CopyDirectory(fullPath, backupPath);
            return new PathSnapshot(exists: true, isDirectory: true, fullPath, backupPath);
        }

        return new PathSnapshot(exists: false, isDirectory: false, fullPath, null);
    }

    public bool EqualsSnapshot(PathSnapshot other) {

        if (Exists != other.Exists) return false;
        if (!Exists) return true;
        if (IsDirectory != other.IsDirectory) return false;

        return IsDirectory
            ? DirectoryContentsEqual(_backupPath!, other._backupPath!)
            : FileContentsEqual(_backupPath!, other._backupPath!);
    }

    public void RestoreTo(string path) {

        var fullPath = Path.GetFullPath(path);

        if (File.Exists(fullPath))
            File.Delete(fullPath);
        else if (Directory.Exists(fullPath))
            Directory.Delete(fullPath, recursive: true);

        if (!Exists || _backupPath == null) return;

        if (IsDirectory) {
            Directory.CreateDirectory(fullPath);
            CopyDirectory(_backupPath, fullPath);
            return;
        }

        var parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);

        File.Copy(_backupPath, fullPath, overwrite: true);
    }

    public void Dispose() {

        if (string.IsNullOrWhiteSpace(_backupPath)) return;

        if (File.Exists(_backupPath))
            File.Delete(_backupPath);
        else if (Directory.Exists(_backupPath))
            Directory.Delete(_backupPath, recursive: true);
    }

    private static string AllocateBackupPath(string extension) {

        var root = Path.Combine(Path.GetTempPath(), "ScytheHistorySnapshots");
        Directory.CreateDirectory(root);
        return Path.Combine(root, Guid.NewGuid().ToString("N") + extension);
    }

    private static void CopyDirectory(string source, string target) {

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(target, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) {
            var relative = Path.GetRelativePath(source, file);
            var destination = Path.Combine(target, relative);
            var destinationParent = Path.GetDirectoryName(destination);

            if (!string.IsNullOrWhiteSpace(destinationParent))
                Directory.CreateDirectory(destinationParent);

            File.Copy(file, destination, overwrite: true);
        }
    }

    private static bool FileContentsEqual(string left, string right) {

        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);

        if (leftInfo.Length != rightInfo.Length) return false;

        using var leftStream = File.OpenRead(left);
        using var rightStream = File.OpenRead(right);

        while (true) {
            var leftByte = leftStream.ReadByte();
            var rightByte = rightStream.ReadByte();

            if (leftByte != rightByte) return false;
            if (leftByte == -1) return true;
        }
    }

    private static bool DirectoryContentsEqual(string left, string right) {

        var leftEntries = Directory.EnumerateFileSystemEntries(left, "*", SearchOption.AllDirectories)
            .Select(entry => Path.GetRelativePath(left, entry).Replace('\\', '/'))
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToArray();
        var rightEntries = Directory.EnumerateFileSystemEntries(right, "*", SearchOption.AllDirectories)
            .Select(entry => Path.GetRelativePath(right, entry).Replace('\\', '/'))
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToArray();

        if (!leftEntries.SequenceEqual(rightEntries, StringComparer.Ordinal)) return false;

        foreach (var entry in leftEntries) {
            var leftPath = Path.Combine(left, entry);
            var rightPath = Path.Combine(right, entry);

            if (Directory.Exists(leftPath)) continue;
            if (!FileContentsEqual(leftPath, rightPath)) return false;
        }

        return true;
    }
}
