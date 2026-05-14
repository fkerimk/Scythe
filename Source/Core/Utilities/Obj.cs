using System.Collections;
using System.Numerics;
using Raylib_cs;
using Newtonsoft.Json;

[JsonObject(MemberSerialization.OptIn)]
internal class Obj {

    public static string Icon => Icons.FaDotCircleO;
    public static Color Color => Colors.GuiTypeObject;

    public static event Action<Obj>? OnDelete;

    [Label("Name"), RecordHistory]
    public string Name {
        get;
        set {
            if (field == value) return;

            field = value;
            PrefabUtility.UpdateObjectOverrideState(this, nameof(Name), value);
        }
    } = null!;

    public Obj? Parent;
    public readonly ObjCollection ChildEntries = new();
    public Dictionary<string, Obj> Children => ChildEntries.ToFirstMatchDictionary();
    [JsonProperty, RecordHistory, FindAsset("PrefabAsset")]
    public string Prefab { get; set; } = "";
    [JsonProperty, RecordHistory]
    public string PrefabPath { get; set; } = "";
    [JsonProperty]
    public HashSet<string> PrefabOverrides { get; set; } = [];

    [JsonProperty] public Transform Transform = null!;
    public ComponentCollection ComponentEntries { get; set; } = null!;
    public Dictionary<string, Component> Components => ComponentEntries.ToFirstMatchDictionary();

    public Matrix4x4 Matrix = Matrix4x4.Identity;
    public Matrix4x4 RotMatrix = Matrix4x4.Identity;

    public Matrix4x4 WorldMatrix = Matrix4x4.Identity;
    public Matrix4x4 WorldRotMatrix = Matrix4x4.Identity;
    public Matrix4x4 VisualWorldMatrix = Matrix4x4.Identity;

    public Vector3 Up => Vector3.Normalize(new Vector3(WorldRotMatrix.M12, WorldRotMatrix.M22, WorldRotMatrix.M32));
    public Vector3 Fwd => Vector3.Normalize(new Vector3(WorldRotMatrix.M13, WorldRotMatrix.M23, WorldRotMatrix.M33));
    public Vector3 Right => Vector3.Normalize(new Vector3(WorldRotMatrix.M11, WorldRotMatrix.M21, WorldRotMatrix.M31));

    public Vector3 FwdFlat {
        get {
            var fwd = Fwd;
            fwd.Y = 0;
            fwd = Vector3.Normalize(fwd);
            return fwd;
        }
    }

    public Vector3 RightFlat {
        get {
            var right = Right;
            right.Y = 0;
            right = Vector3.Normalize(right);
            return right;
        }
    }

    public Vector3 Pos {
        get => Transform.Pos;
        set => Transform.Pos = value;
    }

    public Quaternion Rot {
        get => Transform.Rot;
        set => Transform.Rot = value;
    }

    public bool IsSelected;

    public Obj(string? name, Obj? parent) {

        if (name == null) return;

        Parent = parent;
        Name = name;
        Transform = new Transform(this);
        ComponentEntries = new ComponentCollection();
    }

    public bool HasPrefabOverride(string propertyName) => PrefabOverrides.Contains(propertyName);

    public void SetPrefabOverride(string propertyName, bool isOverridden) {

        if (string.IsNullOrWhiteSpace(propertyName)) return;

        if (isOverridden)
            PrefabOverrides.Add(propertyName);
        else
            PrefabOverrides.Remove(propertyName);
    }

    private bool HasPrefabLinkInHierarchy() {

        var current = this;

        while (current != null) {

            if (!string.IsNullOrWhiteSpace(current.Prefab) || !string.IsNullOrWhiteSpace(current.PrefabPath))
                return true;

            current = current.Parent;
        }

        return false;
    }

    public void Delete() {

        if (Parent == null) return;

        OnDelete?.Invoke(this);
        Dispose();
        Parent.ChildEntries.Remove(this);
    }

    public void Dispose() {

        IsSelected = false;

        Transform.UnloadAndQuit();

        foreach (var component in ComponentEntries.Values)
            component.UnloadAndQuit();

        foreach (var child in ChildEntries.Values)
            child.Dispose();
    }

    public void RecordedDelete() {

        var parent = Parent;

        if (parent == null) return;

        var name = Name;
        History.Execute($"Delete {name}", redo: Delete, undo: () => SetParent(parent));

        if (Core.ActiveLevel != null) Core.ActiveLevel.IsDirty = true;
    }

    public void SetParent(Obj? obj, bool keepWorld = false) {

        if (obj == null || obj == this || Parent == null || IsAncestorOf(this, obj)) return;

        MoveToIndex(obj, obj.ChildEntries.Count, keepWorld);
    }

    public void MoveBefore(Obj? sibling, bool keepWorld = false) {

        if (sibling?.Parent == null || sibling == this || Parent == null) return;

        MoveToIndex(sibling.Parent, sibling.GetSiblingIndex(), keepWorld);
    }

    public void MoveAfter(Obj? sibling, bool keepWorld = false) {

        if (sibling?.Parent == null || sibling == this || Parent == null) return;

        MoveToIndex(sibling.Parent, sibling.GetSiblingIndex() + 1, keepWorld);
    }

    public void RecordedMoveBefore(Obj? sibling) {

        if (sibling?.Parent == null || sibling == this || Parent == null) return;

        RecordedMoveToIndex(sibling.Parent, sibling.GetSiblingIndex());
    }

    public void RecordedMoveAfter(Obj? sibling) {

        if (sibling?.Parent == null || sibling == this || Parent == null) return;

        RecordedMoveToIndex(sibling.Parent, sibling.GetSiblingIndex() + 1);
    }

    public int GetSiblingIndex() {

        if (Parent == null) return -1;

        return Parent.ChildEntries.IndexOf(this);
    }

    public int GetSiblingNameIndex() =>
        Parent?.ChildEntries.GetOccurrenceIndex(this) ?? 0;

    private void MoveToIndex(Obj? obj, int insertIndex, bool keepWorld = false) {

        if (obj == null || obj == this || Parent == null || IsAncestorOf(this, obj)) return;

        var wp = Vector3.Zero;
        var wr = Quaternion.Identity;
        var ws = Vector3.One;

        if (keepWorld) DecomposeWorldMatrix(out wp, out wr, out ws);

        Parent.ChildEntries.Remove(this);

        var orderedChildren = obj.ChildEntries.Values.Where(child => child != this).ToList();
        insertIndex = Math.Clamp(insertIndex, 0, orderedChildren.Count);
        orderedChildren.Insert(insertIndex, this);

        obj.ChildEntries.Clear();
        Parent = obj;

        foreach (var child in orderedChildren) {
            child.Parent = obj;
            obj.ChildEntries.Add(child);
        }

        if (keepWorld) {
            Transform.WorldPos = wp;
            Transform.WorldRot = wr;
            Transform.WorldScale = ws;
        }
    }

    public void RecordedSetParent(Obj? obj) {

        if (obj == null || obj == this || Parent == null || IsAncestorOf(this, obj)) return;

        RecordedMoveToIndex(obj, obj.ChildEntries.Count);
    }

    private void RecordedMoveToIndex(Obj? obj, int insertIndex) {

        if (obj == null || obj == this || Parent == null || IsAncestorOf(this, obj)) return;

        var oldParent = Parent;
        var oldIndex = GetSiblingIndex();
        History.StartRecording(this, $"Change Parent of {Name}");
        History.StartRecording(Transform);

        MoveToIndex(obj, insertIndex, true);

        var newIndex = GetSiblingIndex();

        History.SetUndoAction(() => MoveToIndex(oldParent, oldIndex, true));
        History.SetRedoAction(() => MoveToIndex(obj, newIndex, true));

        if (Core.ActiveLevel != null) Core.ActiveLevel.IsDirty = true;
        History.StopRecording();
    }

    public unsafe void DecomposeMatrix(out Vector3 pos, out Quaternion rot, out Vector3 scale) {

        var position = Vector3.Zero;
        var rotation = Quaternion.Identity;
        var lossyScale = Vector3.One;

        Raymath.MatrixDecompose(Matrix, &position, &rotation, &lossyScale);

        pos = position;
        rot = rotation;
        scale = lossyScale;
    }

    public unsafe void DecomposeWorldMatrix(out Vector3 worldPos, out Quaternion worldRot, out Vector3 worldScale) {

        var position = Vector3.Zero;
        var rotation = Quaternion.Identity;
        var lossyScale = Vector3.One;

        Raymath.MatrixDecompose(WorldMatrix, &position, &rotation, &lossyScale);

        worldPos = position;
        worldScale = lossyScale;
        worldRot = rotation;
    }

    public string[] GetPathFromRoot() {

        var path = new List<string>();
        var current = this;

        while (current is { Parent: not null }) {
            path.Add(current.Name);
            current = current.Parent;
        }

        path.Reverse();
        return path.ToArray();
    }

    public Obj GetRoot() {

        var current = this;

        while (current.Parent != null)
            current = current.Parent;

        return current;
    }

    public Obj? GetChildAt(int index) => ChildEntries.GetAt(index);

    public T? GetComponent<T>() where T : class {

        foreach (var component in ComponentEntries.Values) {
            if (component is T typedComponent)
                return typedComponent;

            if (component is Script { Instance: T typedScript })
                return typedScript;
        }

        return null;
    }

    public List<T> GetComponents<T>() where T : class {

        var result = new List<T>();

        foreach (var component in ComponentEntries.Values) {
            if (component is T typedComponent)
                result.Add(typedComponent);

            if (component is Script { Instance: T typedScript })
                result.Add(typedScript);
        }

        return result;
    }

    public static bool IsAncestorOf(Obj ancestor, Obj? target) {

        if (target == null) return false;

        var current = target;

        while (current != null) {

            if (current == ancestor) return true;

            current = current.Parent;
        }

        return false;
    }

    public Obj? Find(params string[] names) {

        if (names.Length == 0) return this;

        var current = this;

        foreach (var name in names) {

            if (current.ChildEntries.TryGetValue(name, out var next))
                current = next;
            else
                return null;
        }

        return current;
    }

    public Component? FindComponent(params string[] names) {

        var obj = Find(names[..^1]);
        return obj?.ComponentEntries.GetValueOrDefault(names[^1]);
    }

    public Component MakeComponent(string name) {

        var component = Activator.CreateInstance(Type.GetType(name) ?? throw new KeyNotFoundException(), this) as Component ?? throw new InvalidOperationException();
        ComponentEntries.Add(component);
        return component;
    }
}

internal sealed class ObjCollection : IEnumerable<KeyValuePair<string, Obj>> {
    private readonly List<Obj> _items = [];

    public int Count => _items.Count;
    public IEnumerable<string> Keys => _items.Select(item => item.Name);
    public IReadOnlyList<Obj> Values => _items;

    public Obj this[string name] {
        get => GetValueOrDefault(name) ?? throw new KeyNotFoundException(name);
        set {
            var index = _items.FindIndex(item => string.Equals(item.Name, name, StringComparison.Ordinal));
            if (index >= 0)
                _items[index] = value;
            else
                _items.Add(value);
        }
    }

    public void Add(Obj obj) => _items.Add(obj);

    public void Add(string _, Obj obj) => _items.Add(obj);

    public void Clear() => _items.Clear();

    public bool ContainsKey(string name) =>
        _items.Any(item => string.Equals(item.Name, name, StringComparison.Ordinal));

    public Obj? GetAt(int index) =>
        index >= 0 && index < _items.Count ? _items[index] : null;

    public Obj? GetValueOrDefault(string name) {
        TryGetValue(name, out var value);
        return value;
    }

    public int GetOccurrenceIndex(Obj obj) {

        var index = 0;

        foreach (var item in _items) {
            if (ReferenceEquals(item, obj)) return index;
            if (string.Equals(item.Name, obj.Name, StringComparison.Ordinal))
                index++;
        }

        return -1;
    }

    public int IndexOf(Obj obj) => _items.IndexOf(obj);

    public bool Remove(Obj obj) => _items.Remove(obj);

    public bool Remove(string name) {

        var index = _items.FindIndex(item => string.Equals(item.Name, name, StringComparison.Ordinal));

        if (index < 0) return false;

        _items.RemoveAt(index);
        return true;
    }

    public bool TryGetValue(string name, out Obj value) =>
        TryGetValue(name, 0, out value);

    public bool TryGetValue(string name, int occurrenceIndex, out Obj value) {

        var index = 0;

        foreach (var item in _items) {
            if (!string.Equals(item.Name, name, StringComparison.Ordinal)) continue;
            if (index++ != occurrenceIndex) continue;
            value = item;
            return true;
        }

        value = null!;
        return false;
    }

    public IEnumerator<KeyValuePair<string, Obj>> GetEnumerator() {
        foreach (var item in _items)
            yield return new KeyValuePair<string, Obj>(item.Name, item);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public Dictionary<string, Obj> ToFirstMatchDictionary() {

        var result = new Dictionary<string, Obj>(StringComparer.Ordinal);

        foreach (var item in _items)
            result.TryAdd(item.Name, item);

        return result;
    }
}

internal sealed class ComponentCollection : IEnumerable<KeyValuePair<string, Component>> {
    private readonly List<Component> _items = [];

    public int Count => _items.Count;
    public IEnumerable<string> Keys => _items.Select(GetKey);
    public IReadOnlyList<Component> Values => _items;

    public Component this[string name] {
        get => GetValueOrDefault(name) ?? throw new KeyNotFoundException(name);
        set {
            var index = _items.FindIndex(item => string.Equals(GetKey(item), name, StringComparison.Ordinal));
            if (index >= 0)
                _items[index] = value;
            else
                _items.Add(value);
        }
    }

    public void Add(Component component) => _items.Add(component);

    public void Add(string _, Component component) => _items.Add(component);

    public void Clear() => _items.Clear();

    public bool ContainsKey(string name) =>
        _items.Any(item => string.Equals(GetKey(item), name, StringComparison.Ordinal));

    public Component? GetValueOrDefault(string name) {
        TryGetValue(name, out var value);
        return value;
    }

    public int GetOccurrenceIndex(Component component) {

        var key = GetKey(component);
        var index = 0;

        foreach (var item in _items) {
            if (ReferenceEquals(item, component)) return index;
            if (string.Equals(GetKey(item), key, StringComparison.Ordinal))
                index++;
        }

        return -1;
    }

    public bool Remove(Component component) => _items.Remove(component);

    public bool Remove(string name) {

        var index = _items.FindIndex(item => string.Equals(GetKey(item), name, StringComparison.Ordinal));

        if (index < 0) return false;

        _items.RemoveAt(index);
        return true;
    }

    public bool TryGetValue(string name, out Component value) =>
        TryGetValue(name, 0, out value);

    public bool TryGetValue(string name, int occurrenceIndex, out Component value) {

        var index = 0;

        foreach (var item in _items) {
            if (!string.Equals(GetKey(item), name, StringComparison.Ordinal)) continue;
            if (index++ != occurrenceIndex) continue;
            value = item;
            return true;
        }

        value = null!;
        return false;
    }

    private static string GetKey(Component component) => component.GetType().Name;

    public IEnumerator<KeyValuePair<string, Component>> GetEnumerator() {
        foreach (var item in _items)
            yield return new KeyValuePair<string, Component>(GetKey(item), item);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public Dictionary<string, Component> ToFirstMatchDictionary() {

        var result = new Dictionary<string, Component>(StringComparer.Ordinal);

        foreach (var item in _items)
            result.TryAdd(GetKey(item), item);

        return result;
    }
}

internal static partial class Extensions {

    extension(Obj source) {

        private Obj CloneInternal(Obj? parent = null, bool preserveName = false) {

            parent ??= source.Parent;

            var name = source.Name;
            var clone = new Obj(name, parent);
            if (parent != null)
                parent.ChildEntries.Add(clone);
            clone.Prefab = source.Prefab;
            clone.PrefabPath = source.PrefabPath;
            clone.PrefabOverrides = [.. source.PrefabOverrides];

            ObjectGraph.CopyJsonState(source.Transform, clone.Transform);
            clone.Transform.PrefabOverrides = [.. source.Transform.PrefabOverrides];
            clone.Transform.UpdateTransform();

            foreach (var (_, sourceComponent) in source.ComponentEntries) {

                var compType = sourceComponent.GetType();

                if (Activator.CreateInstance(compType, clone) is not Component cloneComp) continue;

                ObjectGraph.CopyJsonState(sourceComponent, cloneComp);
                cloneComp.PrefabOverrides = [.. sourceComponent.PrefabOverrides];

                clone.ComponentEntries.Add(cloneComp);
            }

            foreach (var child in source.ChildEntries.Values.ToList())
                child.CloneInternal(clone, preserveName: true);

            return clone;
        }

        public Obj DeepClone(Obj? parent = null, bool preserveName = false) => source.CloneInternal(parent, preserveName);

        public Obj CloneRecorded() {

            var clone = source.CloneInternal();
            var parent = source.Parent!;

            if (parent.FindPrefabRoot() != null && Core.ActiveLevel?.IsPrefabDocument != true)
                PrefabUtility.MarkAddedChildSubtree(clone);

            History.Execute($"Duplicate {source.Name}", redo: () => clone.SetParent(parent), undo: clone.Delete);

            return clone;
        }

        public void ClearPrefabLinksRecursive() {

            source.Prefab = "";
            source.PrefabPath = "";

            foreach (var child in source.ChildEntries.Values)
                child.ClearPrefabLinksRecursive();
        }

        public Obj? FindPrefabRoot() {

            var current = source;

            while (current != null) {

                if (!string.IsNullOrWhiteSpace(current.Prefab) || !string.IsNullOrWhiteSpace(current.PrefabPath))
                    return current;

                current = current.Parent!;
            }

            return null;
        }
    }
}
