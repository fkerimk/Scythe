using System.Numerics;
using Raylib_cs;
using Newtonsoft.Json;
using Jitter2.Collision.Shapes;

internal class SphereCollider(Obj obj) : Component(obj) {

    public override Color LabelColor => Colors.GuiTypePhysics;
    public override string LabelIcon => Icons.FaDotCircleO;

    [RecordHistory]
    [JsonProperty]
    [Label("Radius")]
    public float Radius { get; set; } = 0.5f;

    [RecordHistory]
    [JsonProperty]
    [Label("Center")]
    public Vector3 Center { get; set; } = Vector3.Zero;

    [JsonIgnore] public SphereShape? Shape;

    public override bool Load() {

        var isPrefabInstance = Obj.FindPrefabRoot() != null;
        Vector3 scale;
        if (isPrefabInstance) {
            scale = Obj.Transform.Scale;
        } else {
            Obj.DecomposeWorldMatrix(out _, out _, out scale);
        }
        var maxScale = MathF.Max(scale.X, MathF.Max(scale.Y, scale.Z));
        Shape = new SphereShape(Radius * maxScale);

        return true;
    }

    public override void Render3D() {

        if (!IsSelected || CommandLine.Runtime) return;

        Obj.DecomposeWorldMatrix(out var pos, out var rot, out var scale);

        // Physics logic re-calculation
        var maxScale = MathF.Max(scale.X, MathF.Max(scale.Y, scale.Z));
        var worldRadius = Radius * maxScale;

        // Manual Transform: (Center * Scale) * Rot + Pos
        var scaledCenter = Center * scale;
        var rotatedCenter = Vector3.Transform(scaledCenter, rot);
        var worldCenter = pos + rotatedCenter;

        var colorVisible = Color.Lime;
        var colorHidden = Raylib.ColorAlpha(Color.Lime, 0.15f);

        // Hidden
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthTest();
        Raylib.DrawSphereWires(worldCenter, worldRadius, 16, 16, colorHidden);
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthTest();

        // Visible
        Raylib.DrawSphereWires(worldCenter, worldRadius, 16, 16, colorVisible);
    }
}