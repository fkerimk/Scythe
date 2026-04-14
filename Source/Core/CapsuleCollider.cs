using System.Numerics;
using Raylib_cs;
using Newtonsoft.Json;
using Jitter2.Collision.Shapes;

internal class CapsuleCollider(Obj obj) : Component(obj) {

    public override Color LabelColor => Colors.GuiTypePhysics;
    public override string LabelIcon => Icons.FaDotCircleO;

    [Label("Height"), JsonProperty, RecordHistory]
    public float Height { get; set; } = 1.0f;

    [Label("Radius"), JsonProperty, RecordHistory]
    public float Radius { get; set; } = 0.5f;

    [Label("Center"), JsonProperty, RecordHistory]
    public Vector3 Center { get; set; } = Vector3.Zero;

    [JsonIgnore] public CapsuleShape? Shape;

    public override bool Load() {

        Obj.DecomposeWorldMatrix(out _, out _, out var scale);
        var maxScale = MathF.Max(scale.X, scale.Z);

        Shape = new CapsuleShape(Radius * maxScale, Height * scale.Y);

        return true;
    }

    public override void Render3D() {

        if (!IsSelected || CommandLine.Runtime) return;

        Obj.DecomposeWorldMatrix(out var pos, out var rot, out var scale);

        // Physics logic
        var maxScale = MathF.Max(scale.X, scale.Z);
        var h = Height * scale.Y;
        var r = Radius * maxScale;

        // Transform Center to world
        var scaledCenter = Center * scale;
        var rotatedCenter = Vector3.Transform(scaledCenter, rot);
        var worldCenter = pos + rotatedCenter;

        var up = Vector3.TransformNormal(Vector3.UnitY, Obj.WorldMatrix);

        var offsetDist = Height * scale.Y / 2.0f - Radius * maxScale;
        if (offsetDist < 0) offsetDist = 0;

        // Local UP is (0,1,0). Rotated UP is Vector3.Transform(Vector3.UnitY, Rot);
        var worldUp = Vector3.Transform(Vector3.UnitY, rot); // Pure rotation

        var topPos = worldCenter + worldUp * offsetDist;
        var botPos = worldCenter - worldUp * offsetDist;

        var colorVisible = Color.Lime;
        var colorHidden = Raylib.ColorAlpha(Color.Lime, 0.15f);

        // Hidden
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthTest();
        Raylib.DrawCapsuleWires(topPos, botPos, r, 16, 16, colorHidden);
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthTest();

        // Visible
        Raylib.DrawCapsuleWires(topPos, botPos, r, 16, 16, colorVisible);
    }
}