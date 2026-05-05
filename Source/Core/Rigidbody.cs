using System.ComponentModel;
using System.Numerics;
using Raylib_cs;
using Newtonsoft.Json;
using Jitter2.Dynamics;
using Jitter2.Collision.Shapes;

internal class Rigidbody(Obj obj) : Component(obj) {

    public override Color LabelColor => Colors.GuiTypePhysics;
    public override string LabelIcon => Icons.FaCrosshairs;

    [Label("Static"), RecordHistory, JsonProperty, DefaultValue(false)]
    public bool IsStatic { get; set; }

    [Label("Gravity"), RecordHistory, JsonProperty, DefaultValue(true)]
    public bool Gravity { get; set; } = true;

    [Header("Material")]
    [Label("Friction"), RecordHistory, JsonProperty, DefaultValue(0.5f)]
    public float Friction {
        get;
        set {
            field = value;
            Body?.Friction = value;
        }
    } = 0.5f;

    [Label("Bounciness"), RecordHistory, JsonProperty, DefaultValue(0)]
    public float Bounciness {
        get;
        set {
            field = value;
            Body?.Restitution = value;
        }
    }

    [Label("Friction Combine"), RecordHistory, JsonProperty, DefaultValue(PhysicsCombineMode.Average)]
    public PhysicsCombineMode FrictionCombine { get; set; } = PhysicsCombineMode.Average;

    [Label("Bounce Combine"), RecordHistory, JsonProperty, DefaultValue(PhysicsCombineMode.Average)]
    public PhysicsCombineMode BounceCombine { get; set; } = PhysicsCombineMode.Average;

    [Header("Constraints")]
    [Label("Freeze Pos"), RecordHistory, JsonProperty]
    public Bool3 FreezePos { get; set; }

    [Label("Freeze Rot"), RecordHistory, JsonProperty]
    public Bool3 FreezeRot { get; set; }

    [JsonIgnore] public RigidBody? Body;

    private Vector3 _lastSyncedPos;
    private Quaternion _lastSyncedRot;

    public Vector3 Velocity {
        get => Body == null ? Vector3.Zero : Conversion.FromJitter(Body.Velocity);
        set {

            if (Body == null) return;

            Body.Velocity = Conversion.ToJitter(value);
            Body.SetActivationState(true);
        }
    }

    public Vector3 AngularVelocity {
        get => Body == null ? Vector3.Zero : Conversion.FromJitter(Body.AngularVelocity);
        set {

            if (Body == null) return;

            Body.AngularVelocity = Conversion.ToJitter(value);
            Body.SetActivationState(true);
        }
    }

    public void AddForce(Vector3 force) {

        if (Body == null) return;

        Body.AddForce(Conversion.ToJitter(force));
        Body.SetActivationState(true);
    }

    public override bool Load() {

        if (!CommandLine.Runtime && !Core.IsPlaying) return true;

        Body = Physics.World.CreateRigidBody();
        Body.Tag = this;

        Obj.DecomposeWorldMatrix(out var pos, out var rot, out var scale);

        if (Obj.Components.TryGetValue("BoxCollider", out var box)) {

            var boxCollider = (BoxCollider)box;

            if (!boxCollider.IsLoaded) {
                boxCollider.Load();
                boxCollider.IsLoaded = true;
            }

            if (boxCollider.Shape != null) {
                if (boxCollider.Center != Vector3.Zero) {
                    var scaledCenter = boxCollider.Center * scale;
                    Body.AddShape(new TransformedShape(boxCollider.Shape, Conversion.ToJitter(scaledCenter)), MassInertiaUpdateMode.Preserve);
                } else
                    Body.AddShape(boxCollider.Shape, MassInertiaUpdateMode.Preserve);
            }
        }

        if (Obj.Components.TryGetValue("SphereCollider", out var sphere)) {

            var sphereCollider = (SphereCollider)sphere;

            if (!sphereCollider.IsLoaded) {
                sphereCollider.Load();
                sphereCollider.IsLoaded = true;
            }

            if (sphereCollider.Shape != null) {
                if (sphereCollider.Center != Vector3.Zero) {
                    var scaledCenter = sphereCollider.Center * scale;
                    Body.AddShape(new TransformedShape(sphereCollider.Shape, Conversion.ToJitter(scaledCenter)), MassInertiaUpdateMode.Preserve);
                } else
                    Body.AddShape(sphereCollider.Shape, MassInertiaUpdateMode.Preserve);
            }
        }

        if (Obj.Components.TryGetValue("CapsuleCollider", out var capsule)) {

            var capsuleCollider = (CapsuleCollider)capsule;

            if (!capsuleCollider.IsLoaded) {
                capsuleCollider.Load();
                capsuleCollider.IsLoaded = true;
            }

            if (capsuleCollider.Shape != null) {
                if (capsuleCollider.Center != Vector3.Zero) {
                    var scaledCenter = capsuleCollider.Center * scale;
                    Body.AddShape(new TransformedShape(capsuleCollider.Shape, Conversion.ToJitter(scaledCenter)), MassInertiaUpdateMode.Preserve);
                } else
                    Body.AddShape(capsuleCollider.Shape, MassInertiaUpdateMode.Preserve);
            }
        }

        Body.Position = Conversion.ToJitter(pos);
        Body.Orientation = Conversion.ToJitter(rot);
        Body.MotionType = IsStatic ? MotionType.Static : MotionType.Dynamic;
        Body.AffectedByGravity = Gravity;
        Body.Friction = Friction;
        Body.Restitution = Bounciness;

        Body.SetMassInertia();

        _lastSyncedPos = pos;
        _lastSyncedRot = rot;

        Physics.Register(this);

        return true;
    }

    public override void Logic() {

        if ((!CommandLine.Runtime && !Core.IsPlaying) || Body == null) return;

        // Check for manual transform changes (Script or Teleport)
        if (Obj.Transform.Pos != _lastSyncedPos) {

            Body.Position = Conversion.ToJitter(Obj.Transform.Pos);
            _lastSyncedPos = Obj.Transform.Pos;
        }

        if (Obj.Transform.Rot != _lastSyncedRot) {

            Body.Orientation = Conversion.ToJitter(Obj.Transform.Rot);
            _lastSyncedRot = Obj.Transform.Rot;
        }

        if (Body.MotionType == MotionType.Dynamic) {

            // Constraints
            if (FreezePos.X || FreezePos.Y || FreezePos.Z) {

                var vel = Body.Velocity;
                if (FreezePos.X) vel.X = 0;
                if (FreezePos.Y) vel.Y = 0;
                if (FreezePos.Z) vel.Z = 0;
                Body.Velocity = vel;
            }

            if (FreezeRot.X || FreezeRot.Y || FreezeRot.Z) {

                var angVel = Body.AngularVelocity;
                if (FreezeRot.X) angVel.X = 0;
                if (FreezeRot.Y) angVel.Y = 0;
                if (FreezeRot.Z) angVel.Z = 0;
                Body.AngularVelocity = angVel;
            }

            // Sync physics to transform
            Obj.Transform.Pos = Conversion.FromJitter(Body.Position);
            Obj.Transform.Rot = Conversion.FromJitter(Body.Orientation);

            _lastSyncedPos = Obj.Transform.Pos;
            _lastSyncedRot = Obj.Transform.Rot;
        }
    }

    public override void Unload() {

        if (Body != null) Physics.World.Remove(Body);
        Physics.Unregister(this);
    }
}
