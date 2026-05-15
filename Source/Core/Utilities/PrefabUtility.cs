using System.Numerics;
using System.Reflection;
using Newtonsoft.Json;

internal static class PrefabUtility {
    private const string AddedChildMarker = "__added_child";
    private const string AddedComponentMarker = "__added_component";
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

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

    public static Obj? GetPrefabRootObject(Level prefabLevel) => prefabLevel.Root.ChildEntries.Values.FirstOrDefault();

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

        obj.SetPrefabOverride(propertyName, TryGetPropertyValue(sourceObj, propertyName, out var sourceValue) && !ValuesEqual(currentValue, sourceValue));
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

                transform.SetPrefabOverride(overrideKey, TryGetPropertyValue(rootSourceObj.Transform, nameof(Transform.Scale), out var rootScale) && !ValuesEqual(currentValue, rootScale));
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
        transform.SetPrefabOverride(overrideKey, TryGetPropertyValue(sourceObj.Transform, sourcePropertyName, out var sourceValue) && !ValuesEqual(currentValue, sourceValue));
    }

    public static void UpdateComponentOverrideState(Component component, string propertyName, object? currentValue) {

        if (IsApplyingSync || string.IsNullOrWhiteSpace(propertyName)) return;

        var prefabRoot = component.Obj.FindPrefabRoot();
        if (prefabRoot == null) {
            component.SetPrefabOverride(propertyName, false);
            return;
        }

        if (!TryGetSourceComponent(component, out var sourceComponent) || sourceComponent == null) {
            component.SetPrefabOverride(propertyName, true);
            return;
        }

        component.SetPrefabOverride(propertyName, TryGetPropertyValue(sourceComponent, propertyName, out var sourceValue) && !ValuesEqual(currentValue, sourceValue));
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

        foreach (var child in level.Root.ChildEntries.Values.ToList())
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
        ClearInstanceOverrideMarkersPreservingNestedPrefabs(instance);

        return true;
    }

    public static bool SavePrefabFromObject(Obj source, string path, out string message) {

        message = "";

        try {
            var name = CollectionData.GetLevelDisplayName(path);
            var prefabLevel = new Level(name, path, load: false, applyEditorCamera: false);
            var clone = source.DeepClone(prefabLevel.Root, preserveName: true);
            clone.Prefab = "";
            clone.PrefabPath = "";
            ClearInstanceOverrideMarkersPreservingNestedPrefabs(clone);
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

    public static void RefreshOverrideState(object target) {

        ClearSourceCache();

        switch (target) {
            case Obj obj:
                RefreshObjectOverrideStateRecursive(obj);
                break;
            case Transform transform:
                RefreshTransformOverrideState(transform);
                break;
            case Component component:
                RefreshComponentOverrideState(component);
                if (component is Script script)
                    script.ReapplyStoredFieldValues();
                break;
        }
    }

    public static void MarkAsAddedChild(Obj obj) {

        if (!obj.PrefabOverrides.Contains(AddedChildMarker))
            obj.PrefabOverrides.Add(AddedChildMarker);
    }

    public static bool IsAddedChild(Obj obj) => obj.PrefabOverrides.Contains(AddedChildMarker);

    public static void MarkAsAddedComponent(Component component) {

        if (!component.PrefabOverrides.Contains(AddedComponentMarker))
            component.PrefabOverrides.Add(AddedComponentMarker);
    }

    public static bool IsAddedComponent(Component component) => component.PrefabOverrides.Contains(AddedComponentMarker);

    public static bool IsAddedChildOverride(Obj obj) {

        var prefabRoot = obj.FindPrefabRoot();
        if (prefabRoot == null || ReferenceEquals(prefabRoot, obj)) return false;

        return IsAddedChild(obj) || !TryGetSourceObject(obj, out _);
    }

    public static bool IsAddedComponentOverride(Component component) {

        var prefabRoot = component.Obj.FindPrefabRoot();
        if (prefabRoot == null) return false;

        return IsAddedComponent(component);
    }

    public static void MarkAddedChildSubtree(Obj obj) {

        MarkAsAddedChild(obj);

        foreach (var child in obj.ChildEntries.Values)
            MarkAddedChildSubtree(child);
    }

    public static bool TryGetSourceObject(Obj obj, out Obj? sourceObj) {

        sourceObj = null;
        var prefabRoot = obj.FindPrefabRoot();
        if (prefabRoot == null) return false;

        if (!TryResolveSourceLevel(prefabRoot, out var prefabLevel) || prefabLevel == null) return false;

        var currentSource = GetPrefabRootObject(prefabLevel);
        if (currentSource == null) return false;

        if (ReferenceEquals(prefabRoot, obj)) {
            sourceObj = currentSource;
            return true;
        }

        var relativePath = new Stack<(string Name, int Occurrence)>();
        var current = obj;

        while (current != null && !ReferenceEquals(current, prefabRoot)) {
            relativePath.Push((current.Name, current.GetSiblingNameIndex()));
            current = current.Parent;
        }

        while (relativePath.Count > 0) {
            var segment = relativePath.Pop();
            if (!currentSource.ChildEntries.TryGetValue(segment.Name, segment.Occurrence, out var next))
                return false;
            currentSource = next;
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

        return TryGetPropertyValue(sourceObj, property.Name, out sourceValue);
    }

    public static bool TryGetTransformPropertyOverride(Transform transform, PropertyInfo property, out object? sourceValue) {

        sourceValue = null;
        var overrideKey = GetTransformOverrideKey(property.Name);
        if (!transform.HasPrefabOverride(overrideKey)) return false;
        if (!TryGetSourceObject(transform.Obj, out var sourceObj) || sourceObj == null) return false;

        var sourcePropertyName = property.Name == nameof(Transform.Euler) ? nameof(Transform.Euler) : overrideKey;
        return TryGetPropertyValue(sourceObj.Transform, sourcePropertyName, out sourceValue);
    }

    public static bool TryGetComponentPropertyOverride(Component component, PropertyInfo property, out object? sourceValue) {

        sourceValue = null;
        if (!component.HasPrefabOverride(property.Name)) return false;
        if (!TryGetSourceComponent(component, out var sourceComponent) || sourceComponent == null) return false;

        return TryGetPropertyValue(sourceComponent, property.Name, out sourceValue);
    }

    public static bool ApplyObjectPropertyToPrefab(Obj obj, PropertyInfo property, object? value) {

        if (!TryGetSourceObject(obj, out var sourceObj) || sourceObj == null) return false;

        if (!TrySetPropertyValue(sourceObj, property.Name, value)) return false;
        obj.SetPrefabOverride(property.Name, false);
        return SaveSourcePrefab(obj);
    }

    public static bool ApplyTransformPropertyToPrefab(Transform transform, PropertyInfo property, object? value) {

        var overrideKey = GetTransformOverrideKey(property.Name);
        if (!TryGetSourceObject(transform.Obj, out var sourceObj) || sourceObj == null) return false;

        var sourcePropertyName = property.Name == nameof(Transform.Euler) ? nameof(Transform.Euler) : overrideKey;
        if (!TrySetPropertyValue(sourceObj.Transform, sourcePropertyName, value)) return false;
        transform.SetPrefabOverride(overrideKey, false);
        return SaveSourcePrefab(transform.Obj);
    }

    public static bool ApplyComponentPropertyToPrefab(Component component, PropertyInfo property, object? value) {

        if (!TryGetSourceComponent(component, out var sourceComponent) || sourceComponent == null) return false;

        if (!TrySetPropertyValue(sourceComponent, property.Name, value)) return false;
        component.SetPrefabOverride(property.Name, false);
        return SaveSourcePrefab(component.Obj);
    }

    public static bool TryGetSourceScriptFieldValue(Script script, FieldInfo field, out object? sourceValue) {

        sourceValue = null;
        if (!TryGetSourceComponent(script, out var sourceComponent) || sourceComponent is not Script sourceScript) return false;

        var sourceAsset = sourceScript.GetAsset();
        if (sourceAsset?.ScriptType == null) return false;

        sourceValue = sourceScript.GetExposeFieldValue(field, sourceAsset);
        return true;
    }

    public static bool ApplyScriptExposeFieldToPrefab(Script script, FieldInfo field, object? value) {

        if (!TryGetSourceComponent(script, out var sourceComponent) || sourceComponent is not Script sourceScript) return false;

        sourceScript.SetExposeFieldValue(field, RemapExposeValueForPrefab(script, field, value));
        script.SetPrefabOverride(nameof(Script.ExposedValues), false);
        return SaveSourcePrefab(script.Obj);
    }

    private static object? RemapExposeValueForPrefab(Script instanceScript, FieldInfo field, object? value) {

        if (value == null) return null;
        if (!ScriptFieldUtility.IsSceneReferenceType(field.FieldType)) return value;

        var resolvedValue = ScriptFieldUtility.ResolveStoredValueForAssignment(value, field.FieldType, instanceScript.Obj);
        if (resolvedValue == null) return value;

        var instancePrefabRoot = instanceScript.Obj.FindPrefabRoot();
        if (instancePrefabRoot == null) return resolvedValue;

        var targetObj = resolvedValue switch {
            Obj obj => obj,
            ScytheScript targetScript => targetScript.Obj,
            Component component => component.Obj,
            _ => null
        };

        if (targetObj == null || !ReferenceEquals(targetObj.FindPrefabRoot(), instancePrefabRoot))
            return resolvedValue;

        return SceneReferenceValue.FromTarget(resolvedValue, instancePrefabRoot);
    }

    public static bool ApplyAddedComponentToPrefab(Component component) {

        if (!IsAddedComponentOverride(component)) return false;
        if (!TryGetSourceObject(component.Obj, out var sourceObj) || sourceObj == null) return false;

        var clone = CloneComponent(component, sourceObj);
        clone.PrefabOverrides.Clear();
        sourceObj.ComponentEntries.Add(clone);

        component.PrefabOverrides.Clear();
        return SaveSourcePrefab(component.Obj);
    }

    public static bool RevertAddedComponent(Component component) {

        if (!IsAddedComponentOverride(component)) return false;

        component.UnloadAndQuit();
        component.Obj.ComponentEntries.Remove(component);
        return true;
    }

    public static bool ApplyAddedChildToPrefab(Obj obj) {

        if (!IsAddedChildOverride(obj) || obj.Parent == null) return false;
        if (!TryGetSourceObject(obj.Parent, out var sourceParent) || sourceParent == null) return false;

        var clone = obj.DeepClone(sourceParent, preserveName: true);
        ClearInstanceOverrideMarkersPreservingNestedPrefabs(clone, preserveCurrentPrefabOverrides: HasDirectPrefabLink(clone));

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

        foreach (var child in obj.ChildEntries.Values.ToList())
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

        var targetComponentTypeIndices = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var (componentName, sourceComponent) in source.ComponentEntries) {
            var componentIndex = targetComponentTypeIndices.GetValueOrDefault(componentName, 0);
            targetComponentTypeIndices[componentName] = componentIndex + 1;

            if (!target.ComponentEntries.TryGetValue(componentName, componentIndex, out var targetComponent)) {
                targetComponent = CloneComponent(sourceComponent, target);
                target.ComponentEntries.Add(targetComponent);
            }

            targetComponent.PrefabOverrides.Remove(AddedComponentMarker);
            SyncComponentProperties(targetComponent, sourceComponent);
        }

        var targetExtraComponentIndices = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var targetComponent in target.ComponentEntries.Values) {
            var componentName = targetComponent.GetType().Name;
            var targetIndex = targetExtraComponentIndices.GetValueOrDefault(componentName, 0);
            targetExtraComponentIndices[componentName] = targetIndex + 1;

            if (source.ComponentEntries.TryGetValue(componentName, targetIndex, out _)) continue;

            MarkAsAddedComponent(targetComponent);
        }

        var sourceChildNameCounts = source.ChildEntries.Values
            .GroupBy(child => child.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var targetChildTypeIndices = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var targetChild in target.ChildEntries.Values.ToList()) {
            var childIndex = targetChildTypeIndices.GetValueOrDefault(targetChild.Name, 0);
            targetChildTypeIndices[targetChild.Name] = childIndex + 1;

            if (sourceChildNameCounts.TryGetValue(targetChild.Name, out var sourceCount) && childIndex < sourceCount)
                continue;

            if (IsAddedChild(targetChild))
                continue;

            targetChild.Dispose();
            target.ChildEntries.Remove(targetChild);
        }

        var targetChildNameIndices = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var (childName, sourceChild) in source.ChildEntries) {
            var childIndex = targetChildNameIndices.GetValueOrDefault(childName, 0);
            targetChildNameIndices[childName] = childIndex + 1;

            if (!target.ChildEntries.TryGetValue(childName, childIndex, out var targetChild)) {
                targetChild = sourceChild.DeepClone(target, preserveName: true);
                ClearInstanceOverrideMarkersPreservingNestedPrefabs(targetChild, preserveCurrentPrefabOverrides: HasDirectPrefabLink(targetChild));
            }

            SyncObject(targetChild, sourceChild);
        }

        if (!isPrefabRoot) {
            target.Prefab = source.Prefab;
            target.PrefabPath = source.PrefabPath;
        }
    }

    private static Component CloneComponent(Component sourceComponent, Obj owner) {

        var clone = (Component)(Activator.CreateInstance(sourceComponent.GetType(), owner) ?? throw new InvalidOperationException());
        ObjectGraph.CopyJsonState(sourceComponent, clone);
        clone.PrefabOverrides.Clear();
        return clone;
    }

    private static bool TryGetSourceComponent(Component component, out Component? sourceComponent) {

        sourceComponent = null;
        if (!TryGetSourceObject(component.Obj, out var sourceObj) || sourceObj == null) return false;
        if (!sourceObj.ComponentEntries.TryGetValue(component.GetType().Name, component.Obj.ComponentEntries.GetOccurrenceIndex(component), out var resolved)) return false;

        sourceComponent = resolved;
        return true;
    }

    private static void SyncComponentProperties(Component target, Component source) {

        foreach (var prop in target.GetType().GetProperties(InstanceFlags)) {

            if (!prop.CanRead || !prop.CanWrite) continue;
            if (prop.Name is nameof(Component.Obj) or nameof(Component.PrefabOverrides) or nameof(Component.IsLoaded) or nameof(Component.IsSelected)) continue;
            if (target.HasPrefabOverride(prop.Name)) continue;

            var sourceProp = source.GetType().GetProperty(prop.Name, InstanceFlags);
            if (sourceProp == null || !sourceProp.CanRead) continue;

            prop.SetValue(target, sourceProp.GetValue(source));
        }
    }

    private static void ClearOverrideMarkersRecursive(Obj obj) {

        obj.PrefabOverrides.Clear();
        obj.Transform.PrefabOverrides.Clear();

        foreach (var component in obj.ComponentEntries.Values)
            component.PrefabOverrides.Clear();

        foreach (var child in obj.ChildEntries.Values)
            ClearOverrideMarkersRecursive(child);
    }

    private static void ClearInstanceOverrideMarkersPreservingNestedPrefabs(Obj obj, bool preserveCurrentPrefabOverrides = false) {

        if (!(preserveCurrentPrefabOverrides && HasDirectPrefabLink(obj))) {
            obj.PrefabOverrides.Clear();
            obj.Transform.PrefabOverrides.Clear();

            foreach (var component in obj.ComponentEntries.Values)
                component.PrefabOverrides.Clear();
        }

        foreach (var child in obj.ChildEntries.Values) {
            if (HasDirectPrefabLink(child))
                continue;

            ClearInstanceOverrideMarkersPreservingNestedPrefabs(child);
        }
    }

    private static bool HasDirectPrefabLink(Obj obj) =>
        !string.IsNullOrWhiteSpace(obj.Prefab) || !string.IsNullOrWhiteSpace(obj.PrefabPath);

    private static void RefreshObjectOverrideStateRecursive(Obj obj) {

        RefreshObjectOverrideState(obj);
        RefreshTransformOverrideState(obj.Transform);

        foreach (var component in obj.ComponentEntries.Values)
            RefreshComponentOverrideState(component);

        foreach (var child in obj.ChildEntries.Values)
            RefreshObjectOverrideStateRecursive(child);
    }

    private static void RefreshObjectOverrideState(Obj obj) {

        if (ClearOverridesIfUnbound(obj, obj.PrefabOverrides)) return;

        UpdateObjectOverrideState(obj, nameof(Obj.Name), obj.Name);
    }

    private static void RefreshTransformOverrideState(Transform transform) {

        if (ClearOverridesIfUnbound(transform.Obj, transform.PrefabOverrides)) return;

        UpdateTransformOverrideState(transform, nameof(Transform.Pos), transform.Pos);
        UpdateTransformOverrideState(transform, nameof(Transform.Rot), transform.Rot);
        UpdateTransformOverrideState(transform, nameof(Transform.Scale), transform.Scale);
    }

    private static void RefreshComponentOverrideState(Component component) {

        if (ClearOverridesIfUnbound(component.Obj, component.PrefabOverrides)) return;

        if (IsAddedComponent(component)) {
            component.PrefabOverrides.Clear();
            MarkAsAddedComponent(component);
            return;
        }

        foreach (var property in component.GetType().GetProperties(InstanceFlags)) {
            if (!property.CanRead || property.GetIndexParameters().Length > 0) continue;
            if (property.Name is nameof(Component.Obj) or nameof(Component.PrefabOverrides) or nameof(Component.IsLoaded) or nameof(Component.IsSelected)) continue;
            if (!IsInspectablePrefabProperty(property)) continue;

            UpdateComponentOverrideState(component, property.Name, property.GetValue(component));
        }
    }

    private static bool IsInspectablePrefabProperty(PropertyInfo property) =>
        Attribute.IsDefined(property, typeof(LabelAttribute))
        || Attribute.IsDefined(property, typeof(JsonPropertyAttribute))
        || Attribute.IsDefined(property, typeof(RecordHistoryAttribute));

    public static bool HasExplicitOverrides(Obj obj) {

        var prefabRoot = obj.FindPrefabRoot();
        var objectOverrideCount = ReferenceEquals(prefabRoot, obj)
            ? obj.PrefabOverrides.Count(value => value != nameof(Obj.Name))
            : obj.PrefabOverrides.Count;

        if (objectOverrideCount > 0 || obj.Transform.PrefabOverrides.Count > 0)
            return true;

        foreach (var component in obj.ComponentEntries.Values)
            if (component.PrefabOverrides.Count > 0)
                return true;

        return false;
    }

    public static string GetTransformOverrideKey(string propertyName) =>
        propertyName == nameof(Transform.Euler) ? nameof(Transform.Rot) : propertyName;

    public static bool TryGetSourcePrefabFile(Obj obj, out string prefabFile) {

        prefabFile = "";

        var prefabRoot = obj.FindPrefabRoot();
        if (prefabRoot == null) return false;
        return TryResolveSourceFile(prefabRoot, out prefabFile);
    }

    public static bool RestoreSourcePrefabFile(string prefabFile, string json) {

        if (string.IsNullOrWhiteSpace(prefabFile)) return false;

        File.WriteAllText(prefabFile, json);
        RefreshSourcePrefabFile(prefabFile);
        return true;
    }

    public static bool RefreshSourcePrefabFile(string prefabFile) {

        if (string.IsNullOrWhiteSpace(prefabFile) || !File.Exists(prefabFile)) return false;

        ClearSourceCache();
        AssetManager.EnsureImported(prefabFile);
        RefreshOpenPrefabInstances(prefabFile);
        return true;
    }

    private static bool ValuesEqual(object? left, object? right) =>
        ObjectGraph.AreEqual(left, right);

    private static bool SaveSourcePrefab(Obj obj) {

        var prefabRoot = obj.FindPrefabRoot();
        if (prefabRoot == null) return false;
        if (!TryResolveSourceFile(prefabRoot, out var prefabFile)) return false;
        if (!SourceCache.TryGetValue(prefabFile, out var prefabLevel) || prefabLevel == null)
            return false;

        prefabLevel.Save();
        AssetManager.EnsureImported(prefabFile);
        RefreshOpenPrefabInstances(prefabFile);
        return true;
    }

    private static bool TryResolveSourceLevel(Obj prefabRoot, out Level? prefabLevel) {

        prefabLevel = null;
        if (!TryResolveSourceFile(prefabRoot, out var prefabFile)) return false;

        if (!SourceCache.TryGetValue(prefabFile, out prefabLevel)) {
            TryLoadPrefabLevel(prefabFile, out prefabLevel);
            SourceCache[prefabFile] = prefabLevel;
        }

        return prefabLevel != null;
    }

    private static bool TryResolveSourceFile(Obj prefabRoot, out string prefabFile) {

        prefabFile = "";
        var guid = prefabRoot.Prefab;
        var path = prefabRoot.PrefabPath;
        var asset = AssetManager.ResolveReference<PrefabAsset>(ref guid, ref path);
        if (asset == null || string.IsNullOrWhiteSpace(asset.File)) return false;

        prefabRoot.Prefab = guid;
        prefabRoot.PrefabPath = path;
        prefabFile = asset.File;
        return true;
    }

    private static bool TryGetPropertyValue(object target, string propertyName, out object? value) {

        value = null;
        var property = target.GetType().GetProperty(propertyName, InstanceFlags);
        if (property == null || !property.CanRead) return false;

        value = property.GetValue(target);
        return true;
    }

    private static bool TrySetPropertyValue(object target, string propertyName, object? value) {

        var property = target.GetType().GetProperty(propertyName, InstanceFlags);
        if (property == null || !property.CanWrite) return false;

        property.SetValue(target, value);
        return true;
    }

    private static bool ClearOverridesIfUnbound(Obj owner, ISet<string> overrides) {

        if (owner.FindPrefabRoot() != null) return false;

        overrides.Clear();
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

        foreach (var child in obj.ChildEntries.Values)
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

        foreach (var child in obj.ChildEntries.Values)
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
