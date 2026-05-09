using System.Numerics;
using System.Reflection;
using Newtonsoft.Json;

internal static class PrefabUtility {
    private const string AddedChildMarker = "__added_child";

    private static readonly Dictionary<string, Level?> SourceCache = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsApplyingSync { get; private set; }

    public static bool TryLoadPrefabLevel(string prefabPath, out Level? prefabLevel) {

        prefabLevel = null;

        if (!File.Exists(prefabPath)) return false;

        try {
            prefabLevel = new Level(CollectionData.GetLevelDisplayName(prefabPath), prefabPath, applyEditorCamera: false);
            return true;
        } catch {
            return false;
        }
    }

    public static Obj? GetPrefabRootObject(Level prefabLevel) => prefabLevel.Root.Children.Values.FirstOrDefault();

    public static void ClearSourceCache() => SourceCache.Clear();

    public static void UpdateObjectOverrideState(Obj obj, string propertyName, object? currentValue) {

        if (IsApplyingSync || string.IsNullOrWhiteSpace(propertyName)) return;

        var prefabRoot = obj.FindPrefabRoot();
        if (prefabRoot == null) {
            obj.SetPrefabOverride(propertyName, false);
            return;
        }

        if (ReferenceEquals(prefabRoot, obj) && propertyName == nameof(Obj.Name)) {
            obj.SetPrefabOverride(propertyName, false);
            return;
        }

        if (!TryGetSourceObject(obj, out var sourceObj) || sourceObj == null) {
            obj.SetPrefabOverride(propertyName, true);
            return;
        }

        var sourceProperty = sourceObj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        obj.SetPrefabOverride(propertyName, sourceProperty != null && !ValuesEqual(currentValue, sourceProperty.GetValue(sourceObj)));
    }

    public static void UpdateTransformOverrideState(Transform transform, string propertyName, object? currentValue) {

        if (IsApplyingSync || string.IsNullOrWhiteSpace(propertyName)) return;

        var prefabRoot = transform.Obj.FindPrefabRoot();
        var overrideKey = GetTransformOverrideKey(propertyName);

        if (prefabRoot == null || ReferenceEquals(prefabRoot, transform.Obj)) {
            if (ReferenceEquals(prefabRoot, transform.Obj) && overrideKey == nameof(Transform.Scale)) {

                if (!TryGetSourceObject(transform.Obj, out var rootSourceObj) || rootSourceObj == null) {
                    transform.SetPrefabOverride(overrideKey, true);
                    return;
                }

                var rootSourceProperty = rootSourceObj.Transform.GetType().GetProperty(nameof(Transform.Scale), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                transform.SetPrefabOverride(overrideKey, rootSourceProperty != null && !ValuesEqual(currentValue, rootSourceProperty.GetValue(rootSourceObj.Transform)));
                return;
            }

            transform.SetPrefabOverride(overrideKey, false);
            return;
        }

        if (!TryGetSourceObject(transform.Obj, out var sourceObj) || sourceObj == null) {
            transform.SetPrefabOverride(overrideKey, true);
            return;
        }

        var sourcePropertyName = propertyName == nameof(Transform.Euler) ? nameof(Transform.Euler) : overrideKey;
        var sourceProperty = sourceObj.Transform.GetType().GetProperty(sourcePropertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        transform.SetPrefabOverride(overrideKey, sourceProperty != null && !ValuesEqual(currentValue, sourceProperty.GetValue(sourceObj.Transform)));
    }

    public static void RefreshOpenPrefabInstances(string prefabFile) {

        if (string.IsNullOrWhiteSpace(prefabFile)) return;

        var fullPrefabPath = Path.GetFullPath(prefabFile);
        ClearSourceCache();

        foreach (var level in Core.OpenLevels) {

            if (!ContainsPrefabReference(level.Root, fullPrefabPath)) continue;

            var preservedRootTransforms = CapturePrefabRootTransforms(level.Root, fullPrefabPath);
            ApplyPrefabInstances(level);
            RestorePrefabRootTransforms(preservedRootTransforms);
            level.IsDirty = true;
        }

        if (Core.ActiveLevel != null && ContainsPrefabReference(Core.ActiveLevel.Root, fullPrefabPath))
            Core.Load();
    }

    public static void ApplyPrefabInstances(Level level) {

        foreach (var child in level.Root.Children.Values.ToList())
            ApplyPrefabInstancesRecursive(child);
    }

    public static void ApplyPrefabInstancesPreservingRootPlacement(Level level) {

        var preservedRootTransforms = CapturePrefabRootTransforms(level.Root, prefabFile: null);
        ApplyPrefabInstances(level);
        RestorePrefabRootTransforms(preservedRootTransforms);
    }

    public static bool TryInstantiateInto(string prefabReference, Obj parent, out Obj? instance) {

        instance = null;

        var guid = prefabReference;
        var path = prefabReference;
        var asset = AssetManager.ResolveReference<PrefabAsset>(ref guid, ref path);
        if (asset == null || !TryLoadPrefabLevel(asset.File, out var prefabLevel) || prefabLevel == null) return false;

        var sourceRoot = GetPrefabRootObject(prefabLevel);
        if (sourceRoot == null) return false;

        instance = sourceRoot.DeepClone(parent, preserveName: false);
        instance.Prefab = asset.GUID;
        instance.PrefabPath = AssetManager.GetStoredPath(asset.File);
        ClearOverrideMarkersRecursive(instance);

        return true;
    }

    public static bool SavePrefabFromObject(Obj source, string path, out string message) {

        message = "";

        try {
            var name = CollectionData.GetLevelDisplayName(path);
            var prefabLevel = new Level(name, path, load: false, applyEditorCamera: false);
            var clone = source.DeepClone(prefabLevel.Root, preserveName: true);
            clone.ClearPrefabLinksRecursive();
            ClearOverrideMarkersRecursive(clone);
            prefabLevel.Save();
            AssetManager.EnsureImported(path);
            message = $"Prefab '{Path.GetFileName(path)}' created.";
            return true;
        } catch (Exception e) {
            message = $"Prefab creation failed: {e.Message}";
            return false;
        }
    }

    public static void ResolvePrefabRoot(Obj obj) {

        if (obj.FindPrefabRoot() != obj) return;

        obj.ClearPrefabLinksRecursive();
        ClearOverrideMarkersRecursive(obj);
    }

    public static void ClearOverrideMarkers(Obj obj) => ClearOverrideMarkersRecursive(obj);

    public static void RefreshPrefabRoot(Obj obj) {

        if (obj.FindPrefabRoot() != obj) return;
        if (!TryGetSourceObject(obj, out var sourceObj) || sourceObj == null) return;

        SyncPrefabRoot(obj, sourceObj);
    }

    public static bool HasMissingSource(Obj obj) {

        var prefabRoot = obj.FindPrefabRoot();
        if (prefabRoot == null) return false;
        if (string.IsNullOrWhiteSpace(prefabRoot.Prefab) && string.IsNullOrWhiteSpace(prefabRoot.PrefabPath)) return false;

        var guid = prefabRoot.Prefab;
        var path = prefabRoot.PrefabPath;
        return AssetManager.ResolveReference<PrefabAsset>(ref guid, ref path) == null;
    }

    public static void MarkAsAddedChild(Obj obj) {

        if (!obj.PrefabOverrides.Contains(AddedChildMarker))
            obj.PrefabOverrides.Add(AddedChildMarker);
    }

    public static bool IsAddedChild(Obj obj) => obj.PrefabOverrides.Contains(AddedChildMarker);

    public static bool IsAddedChildOverride(Obj obj) {

        var prefabRoot = obj.FindPrefabRoot();
        if (prefabRoot == null || ReferenceEquals(prefabRoot, obj)) return false;

        return IsAddedChild(obj) || !TryGetSourceObject(obj, out _);
    }

    public static void MarkAddedChildSubtree(Obj obj) {

        MarkAsAddedChild(obj);

        foreach (var child in obj.Children.Values)
            MarkAddedChildSubtree(child);
    }

    public static bool TryGetSourceObject(Obj obj, out Obj? sourceObj) {

        sourceObj = null;
        var prefabRoot = obj.FindPrefabRoot();
        if (prefabRoot == null) return false;

        var guid = prefabRoot.Prefab;
        var path = prefabRoot.PrefabPath;
        var asset = AssetManager.ResolveReference<PrefabAsset>(ref guid, ref path);
        if (asset == null) return false;

        prefabRoot.Prefab = guid;
        prefabRoot.PrefabPath = path;

        if (!SourceCache.TryGetValue(asset.File, out var prefabLevel)) {
            TryLoadPrefabLevel(asset.File, out prefabLevel);
            SourceCache[asset.File] = prefabLevel;
        }

        if (prefabLevel == null) return false;

        var currentSource = GetPrefabRootObject(prefabLevel);
        if (currentSource == null) return false;

        if (ReferenceEquals(prefabRoot, obj)) {
            sourceObj = currentSource;
            return true;
        }

        var relativeNames = new Stack<string>();
        var current = obj;

        while (current != null && !ReferenceEquals(current, prefabRoot)) {
            relativeNames.Push(current.Name);
            current = current.Parent;
        }

        while (relativeNames.Count > 0) {
            currentSource = currentSource.Children.GetValueOrDefault(relativeNames.Pop());
            if (currentSource == null) return false;
        }

        sourceObj = currentSource;
        return true;
    }

    public static bool ObjectHasOverrides(Obj obj) {

        var prefabRoot = obj.FindPrefabRoot();
        if (prefabRoot == null) return false;
        if (!TryGetSourceObject(obj, out _)) return true;

        if (ReferenceEquals(prefabRoot, obj))
            obj.SetPrefabOverride(nameof(Obj.Name), false);

        return HasExplicitOverrides(obj);
    }

    public static bool TryGetObjectPropertyOverride(Obj obj, PropertyInfo property, out object? sourceValue) {

        sourceValue = null;
        if (ReferenceEquals(obj.FindPrefabRoot(), obj) && property.Name == nameof(Obj.Name)) {
            obj.SetPrefabOverride(property.Name, false);
            return false;
        }
        if (!obj.HasPrefabOverride(property.Name)) return false;
        if (!TryGetSourceObject(obj, out var sourceObj) || sourceObj == null) return false;

        var sourceProp = sourceObj.GetType().GetProperty(property.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (sourceProp == null) return false;

        sourceValue = sourceProp.GetValue(sourceObj);
        return true;
    }

    public static bool TryGetTransformPropertyOverride(Transform transform, PropertyInfo property, out object? sourceValue) {

        sourceValue = null;
        var overrideKey = GetTransformOverrideKey(property.Name);
        if (!transform.HasPrefabOverride(overrideKey)) return false;
        if (!TryGetSourceObject(transform.Obj, out var sourceObj) || sourceObj == null) return false;

        var sourcePropertyName = property.Name == nameof(Transform.Euler) ? nameof(Transform.Euler) : overrideKey;
        var sourceProp = sourceObj.Transform.GetType().GetProperty(sourcePropertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (sourceProp == null) return false;

        sourceValue = sourceProp.GetValue(sourceObj.Transform);
        return true;
    }

    public static bool TryGetComponentPropertyOverride(Component component, PropertyInfo property, out object? sourceValue) {

        sourceValue = null;
        if (!component.HasPrefabOverride(property.Name)) return false;
        if (!TryGetSourceObject(component.Obj, out var sourceObj) || sourceObj == null) return false;
        if (!sourceObj.Components.TryGetValue(component.GetType().Name, out var sourceComponent)) return false;

        var sourceProp = sourceComponent.GetType().GetProperty(property.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (sourceProp == null) return false;

        sourceValue = sourceProp.GetValue(sourceComponent);
        return true;
    }

    public static bool ApplyObjectPropertyToPrefab(Obj obj, PropertyInfo property, object? value) {

        if (!obj.HasPrefabOverride(property.Name)) return false;
        if (!TryGetSourceObject(obj, out var sourceObj) || sourceObj == null) return false;

        var sourceProperty = sourceObj.GetType().GetProperty(property.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (sourceProperty == null || !sourceProperty.CanWrite) return false;

        sourceProperty.SetValue(sourceObj, value);
        obj.SetPrefabOverride(property.Name, false);
        return SaveSourcePrefab(obj);
    }

    public static bool ApplyTransformPropertyToPrefab(Transform transform, PropertyInfo property, object? value) {

        var overrideKey = GetTransformOverrideKey(property.Name);
        if (!transform.HasPrefabOverride(overrideKey)) return false;
        if (!TryGetSourceObject(transform.Obj, out var sourceObj) || sourceObj == null) return false;

        var sourcePropertyName = property.Name == nameof(Transform.Euler) ? nameof(Transform.Euler) : overrideKey;
        var sourceProperty = sourceObj.Transform.GetType().GetProperty(sourcePropertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (sourceProperty == null || !sourceProperty.CanWrite) return false;

        sourceProperty.SetValue(sourceObj.Transform, value);
        transform.SetPrefabOverride(overrideKey, false);
        return SaveSourcePrefab(transform.Obj);
    }

    public static bool ApplyComponentPropertyToPrefab(Component component, PropertyInfo property, object? value) {

        if (!component.HasPrefabOverride(property.Name)) return false;
        if (!TryGetSourceObject(component.Obj, out var sourceObj) || sourceObj == null) return false;
        if (!sourceObj.Components.TryGetValue(component.GetType().Name, out var sourceComponent)) return false;

        var sourceProperty = sourceComponent.GetType().GetProperty(property.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (sourceProperty == null || !sourceProperty.CanWrite) return false;

        sourceProperty.SetValue(sourceComponent, value);
        component.SetPrefabOverride(property.Name, false);
        return SaveSourcePrefab(component.Obj);
    }

    public static bool ApplyAddedChildToPrefab(Obj obj) {

        if (!IsAddedChildOverride(obj) || obj.Parent == null) return false;
        if (!TryGetSourceObject(obj.Parent, out var sourceParent) || sourceParent == null) return false;

        var clone = obj.DeepClone(sourceParent, preserveName: true);
        clone.ClearPrefabLinksRecursive();
        ClearOverrideMarkersRecursive(clone);

        ClearOverrideMarkersRecursive(obj);
        return SaveSourcePrefab(obj);
    }

    public static bool RevertAddedChild(Obj obj) {

        if (!IsAddedChildOverride(obj)) return false;

        obj.RecordedDelete();
        return true;
    }

    private static void ApplyPrefabInstancesRecursive(Obj obj) {

        if (TryGetSourceObject(obj, out var sourceObj) && sourceObj != null && ReferenceEquals(obj.FindPrefabRoot(), obj))
            SyncPrefabRoot(obj, sourceObj);

        foreach (var child in obj.Children.Values.ToList())
            ApplyPrefabInstancesRecursive(child);
    }

    private static void SyncPrefabRoot(Obj target, Obj source) {

        IsApplyingSync = true;

        try {
            SyncObject(target, source, isPrefabRoot: true);
        } finally {
            IsApplyingSync = false;
        }
    }

    private static void SyncObject(Obj target, Obj source, bool isPrefabRoot = false) {

        if (!isPrefabRoot && !target.HasPrefabOverride(nameof(Obj.Name)))
            target.Name = source.Name;

        // Scene instance root name/pos/rot stay local to the scene; scale can still inherit/override.
        if (!isPrefabRoot) {
            if (!target.Transform.HasPrefabOverride(nameof(Transform.Pos))) target.Transform.Pos = source.Transform.Pos;
            if (!target.Transform.HasPrefabOverride(nameof(Transform.Scale))) target.Transform.Scale = source.Transform.Scale;
            if (!target.Transform.HasPrefabOverride(nameof(Transform.Rot))) target.Transform.Rot = source.Transform.Rot;
        } else if (!target.Transform.HasPrefabOverride(nameof(Transform.Scale)))
            target.Transform.Scale = source.Transform.Scale;

        foreach (var (componentName, sourceComponent) in source.Components) {

            if (!target.Components.TryGetValue(componentName, out var targetComponent)) {
                targetComponent = CloneComponent(sourceComponent, target);
                target.Components[componentName] = targetComponent;
            }

            SyncComponentProperties(targetComponent, sourceComponent);
        }

        foreach (var targetChild in target.Children.Values.Where(child => !source.Children.ContainsKey(child.Name)).ToList()) {
            if (IsAddedChild(targetChild))
                continue;

            targetChild.Dispose();
            target.Children.Remove(targetChild.Name);
        }

        foreach (var (childName, sourceChild) in source.Children) {

            if (target.Children.TryGetValue(childName, out var conflictingChild) && IsAddedChild(conflictingChild))
                conflictingChild.Name = Generators.AvailableName(childName, target.Children.Keys);

            if (!target.Children.TryGetValue(childName, out var targetChild)) {
                targetChild = sourceChild.DeepClone(target, preserveName: true);
                targetChild.ClearPrefabLinksRecursive();
                ClearOverrideMarkersRecursive(targetChild);
            }

            SyncObject(targetChild, sourceChild);
        }

        if (!isPrefabRoot) {
            target.Prefab = "";
            target.PrefabPath = "";
        }
    }

    private static Component CloneComponent(Component sourceComponent, Obj owner) {

        var clone = (Component)(Activator.CreateInstance(sourceComponent.GetType(), owner) ?? throw new InvalidOperationException());
        ObjectGraph.CopyJsonState(sourceComponent, clone);
        clone.PrefabOverrides.Clear();
        return clone;
    }

    private static void SyncComponentProperties(Component target, Component source) {

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (var prop in target.GetType().GetProperties(flags)) {

            if (!prop.CanRead || !prop.CanWrite) continue;
            if (prop.Name is nameof(Component.Obj) or nameof(Component.PrefabOverrides) or nameof(Component.IsLoaded) or nameof(Component.IsSelected)) continue;
            if (target.HasPrefabOverride(prop.Name)) continue;

            var sourceProp = source.GetType().GetProperty(prop.Name, flags);
            if (sourceProp == null || !sourceProp.CanRead) continue;

            prop.SetValue(target, sourceProp.GetValue(source));
        }
    }

    private static void ClearOverrideMarkersRecursive(Obj obj) {

        obj.PrefabOverrides.Clear();
        obj.Transform.PrefabOverrides.Clear();

        foreach (var component in obj.Components.Values)
            component.PrefabOverrides.Clear();

        foreach (var child in obj.Children.Values)
            ClearOverrideMarkersRecursive(child);
    }

    public static bool HasExplicitOverrides(Obj obj) {

        var prefabRoot = obj.FindPrefabRoot();
        var objectOverrideCount = ReferenceEquals(prefabRoot, obj)
            ? obj.PrefabOverrides.Count(value => value != nameof(Obj.Name))
            : obj.PrefabOverrides.Count;

        if (objectOverrideCount > 0 || obj.Transform.PrefabOverrides.Count > 0)
            return true;

        foreach (var component in obj.Components.Values)
            if (component.PrefabOverrides.Count > 0)
                return true;

        return false;
    }

    public static string GetTransformOverrideKey(string propertyName) =>
        propertyName == nameof(Transform.Euler) ? nameof(Transform.Rot) : propertyName;

    private static bool ValuesEqual(object? left, object? right) =>
        ObjectGraph.AreEqual(left, right);

    private static bool SaveSourcePrefab(Obj obj) {

        var prefabRoot = obj.FindPrefabRoot();
        if (prefabRoot == null) return false;

        var guid = prefabRoot.Prefab;
        var path = prefabRoot.PrefabPath;
        var asset = AssetManager.ResolveReference<PrefabAsset>(ref guid, ref path);
        if (asset == null) return false;

        prefabRoot.Prefab = guid;
        prefabRoot.PrefabPath = path;

        if (!SourceCache.TryGetValue(asset.File, out var prefabLevel) || prefabLevel == null)
            return false;

        prefabLevel.Save();
        AssetManager.EnsureImported(asset.File);
        RefreshOpenPrefabInstances(asset.File);
        return true;
    }

    private static bool ContainsPrefabReference(Obj obj, string prefabFile) {

        if (obj.FindPrefabRoot() == obj) {
            var guid = obj.Prefab;
            var path = obj.PrefabPath;
            var asset = AssetManager.ResolveReference<PrefabAsset>(ref guid, ref path);
            if (asset != null && Path.GetFullPath(asset.File).Equals(prefabFile, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var child in obj.Children.Values)
            if (ContainsPrefabReference(child, prefabFile))
                return true;

        return false;
    }

    private static Dictionary<Obj, (Vector3 Pos, Quaternion Rot)> CapturePrefabRootTransforms(Obj root, string? prefabFile) {

        var result = new Dictionary<Obj, (Vector3 Pos, Quaternion Rot)>();
        CapturePrefabRootTransformsRecursive(root, prefabFile, result);
        return result;
    }

    private static void CapturePrefabRootTransformsRecursive(Obj obj, string? prefabFile, Dictionary<Obj, (Vector3 Pos, Quaternion Rot)> transforms) {

        if (obj.FindPrefabRoot() == obj) {
            var guid = obj.Prefab;
            var path = obj.PrefabPath;
            var asset = AssetManager.ResolveReference<PrefabAsset>(ref guid, ref path);

            if (asset != null && (prefabFile == null || Path.GetFullPath(asset.File).Equals(prefabFile, StringComparison.OrdinalIgnoreCase)))
                transforms[obj] = (obj.Transform.Pos, obj.Transform.Rot);
        }

        foreach (var child in obj.Children.Values)
            CapturePrefabRootTransformsRecursive(child, prefabFile, transforms);
    }

    private static void RestorePrefabRootTransforms(Dictionary<Obj, (Vector3 Pos, Quaternion Rot)> transforms) {

        IsApplyingSync = true;

        try {
            foreach (var (obj, transform) in transforms) {
                obj.Transform.Pos = transform.Pos;
                obj.Transform.Rot = transform.Rot;
            }
        } finally {
            IsApplyingSync = false;
        }
    }
}
