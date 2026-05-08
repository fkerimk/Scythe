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

            if (Parent != null) {

                if (Parent.Children.ContainsKey(value)) return;

                if (!string.IsNullOrEmpty(field)) Parent.Children.Remove(field);

                Parent.Children.Add(value, this);
            }

            field = value;
        }
    } = null!;

    public Obj? Parent;
    [JsonProperty] public readonly Dictionary<string, Obj> Children = [];

    // Components
    [JsonProperty] public Transform Transform = null!;

    [JsonProperty] public Dictionary<string, Component> Components { get; set; } = null!;

    // Transform
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

        // Components
        Transform = new Transform(this);
        Components = new Dictionary<string, Component>();
    }

    public void Delete() {

        if (Parent == null) return;

        OnDelete?.Invoke(this);
        Dispose();
        Parent.Children.Remove(Name);
    }

    public void Dispose() {

        IsSelected = false;

        Transform.UnloadAndQuit();

        foreach (var component in Components.Values) component.UnloadAndQuit();

        foreach (var child in Children.Values) child.Dispose();
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

        MoveToIndex(obj, obj.Children.Count, keepWorld);
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

        var index = 0;

        foreach (var child in Parent.Children.Values) {
            if (child == this) return index;
            index++;
        }

        return -1;
    }

    private void MoveToIndex(Obj? obj, int insertIndex, bool keepWorld = false) {

        if (obj == null || obj == this || Parent == null || IsAncestorOf(this, obj)) return;

        var wp = Vector3.Zero;
        var wr = Quaternion.Identity;
        var ws = Vector3.One;

        if (keepWorld) DecomposeWorldMatrix(out wp, out wr, out ws);

        Parent.Children.Remove(Name);

        var orderedChildren = obj.Children.Values.Where(child => child != this).ToList();
        insertIndex = Math.Clamp(insertIndex, 0, orderedChildren.Count);
        orderedChildren.Insert(insertIndex, this);

        obj.Children.Clear();

        foreach (var child in orderedChildren)
            obj.Children.Add(child.Name, child);

        Parent = obj;

        if (keepWorld) {

            Transform.WorldPos = wp;
            Transform.WorldRot = wr;
            Transform.WorldScale = ws;
        }
    }

    public void RecordedSetParent(Obj? obj) {

        if (obj == null || obj == this || Parent == null || IsAncestorOf(this, obj)) return;

        RecordedMoveToIndex(obj, obj.Children.Count);
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

            if (current.Children.TryGetValue(name, out var next))
                current = next;
            else
                return null;
        }

        return current;
    }

    public Component? FindComponent(params string[] names) {

        var obj = Find(names[..^1]);

        return obj?.Components.GetValueOrDefault(names[^1]);
    }

    public Component MakeComponent(string name) {

        if (Components.ContainsKey(name)) throw new TypeLoadException();

        var component = Activator.CreateInstance(Type.GetType(name) ?? throw new KeyNotFoundException(), this) as Component ?? throw new InvalidOperationException();
        Components[name] = component;

        return component;
    }
}

internal static partial class Extensions {

    extension(Obj source) {

        private Obj Clone(Obj? parent = null) {

            parent ??= source.Parent;

            var name = source.Name;

            if (parent != null) name = Generators.AvailableName(name, parent.Children.Keys);

            var clone = new Obj(name, parent);

            // Copy Transform
            var transformJson = JsonConvert.SerializeObject(source.Transform);
            JsonConvert.PopulateObject(transformJson, clone.Transform);

            // Copy Components
            foreach (var (key, sourceComponent) in source.Components) {

                var compType = sourceComponent.GetType();

                if (Activator.CreateInstance(compType, clone) is not Component cloneComp) continue;

                var compJson = JsonConvert.SerializeObject(sourceComponent);
                JsonConvert.PopulateObject(compJson, cloneComp);

                clone.Components[key] = cloneComp;
            }

            // Clone children recursively
            foreach (var child in source.Children.Values.ToList()) child.Clone(clone);

            return clone;
        }

        public Obj CloneRecorded() {

            var clone = source.Clone();
            var parent = source.Parent!;

            History.Execute($"Duplicate {source.Name}", redo: () => clone.SetParent(parent), undo: clone.Delete);

            return clone;
        }
    }
}
