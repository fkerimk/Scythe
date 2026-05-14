using System.Numerics;

/// <summary>
/// Base class for all Scythe user scripts. Inherit from this instead of defining a plain class.
/// Provides access to the owning Obj and common helpers.
/// </summary>
internal abstract class ScytheScript {

    /// <summary>The Obj this script is attached to.</summary>
    public Obj Obj { get; internal set; } = null!;

    //Convenience shortcuts
    public Transform  Transform  => Obj.Transform;
    public Vector3    Pos        { get => Obj.Pos;  set => Obj.Pos = value; }
    public Quaternion Rot        { get => Obj.Rot;  set => Obj.Rot = value; }
    public Vector3    Up         => Obj.Up;
    public Vector3    Fwd        => Obj.Fwd;
    public Vector3    Right      => Obj.Right;
    public Vector3    FwdFlat    => Obj.FwdFlat;
    public Vector3    RightFlat  => Obj.RightFlat;
    public string     Name       => Obj.Name;
    public Obj?       Parent     => Obj.Parent;
    public Obj        Root       => Obj.GetRoot();

    /// <summary>Called once before the first Loop.</summary>
    public virtual void Start() { }

    /// <summary>Called every frame.</summary>
    public virtual void Loop(float dt) { }

    /// <summary>Find a component or attached ScytheScript on this Obj by type.</summary>
    public T? GetComponent<T>() where T : class => Obj.GetComponent<T>();

    public List<T> GetComponents<T>() where T : class => Obj.GetComponents<T>();

    /// <summary>Find a component in a descendant chain by object path segments.</summary>
    public Component? FindComponent(params string[] names) => Obj.FindComponent(names);

    public Obj? GetChildAt(int index) => Obj.GetChildAt(index);

    /// <summary>Re-parent this Obj.</summary>
    public void SetParent(Obj? parent, bool keepWorld = false) => Obj.SetParent(parent, keepWorld);
}
