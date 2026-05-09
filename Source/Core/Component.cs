using System.Numerics;
using Newtonsoft.Json;
using Raylib_cs;

[JsonObject(MemberSerialization.OptIn)]
internal class Component(Obj obj) {

    public readonly Obj Obj = obj;
    [JsonProperty] public HashSet<string> PrefabOverrides { get; set; } = [];

    public virtual string LabelIcon => Icons.FaDotCircleO;
    public virtual Color LabelColor => Colors.GuiTypeObject;

    public virtual bool Load() => true;
    public virtual void Loop(bool is2D, bool isLogic, bool isRender) { }
    public virtual void Logic() { }
    public virtual void Render3D() { }
    public virtual void Render2D() { }
    public virtual void Unload() { }
    public virtual void Quit() { }

    public void UnloadAndQuit() {

        if (IsLoaded) Unload();
        Quit();
        IsLoaded = false;
    }

    public bool IsLoaded;
    public bool IsSelected => Obj.IsSelected;

    public Vector3 Up => Obj.Up;
    public Vector3 Fwd => Obj.Fwd;
    public Vector3 Right => Obj.Right;
    public Vector3 FwdFlat => Obj.FwdFlat;
    public Vector3 RightFlat => Obj.RightFlat;

    public Vector3 Pos {
        get => Obj.Transform.Pos;
        set => Obj.Transform.Pos = value;
    }

    public Quaternion Rot {
        get => Obj.Transform.Rot;
        set => Obj.Transform.Rot = value;
    }

    public bool HasPrefabOverride(string propertyName) => PrefabOverrides.Contains(propertyName);

    public void SetPrefabOverride(string propertyName, bool isOverridden) {

        if (string.IsNullOrWhiteSpace(propertyName)) return;

        if (isOverridden)
            PrefabOverrides.Add(propertyName);
        else
            PrefabOverrides.Remove(propertyName);
    }
}
