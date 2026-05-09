using System.Numerics;
using Raylib_cs;
using Newtonsoft.Json;
using Jitter2.Collision.Shapes;

internal class BoxCollider(Obj obj) : Component(obj) {

    public override Color LabelColor => Colors.GuiTypePhysics;
    public override string LabelIcon => Icons.FaCube;

    [Label("Size"), JsonProperty, RecordHistory]
    public Vector3 Size { get; set; } = Vector3.One;

    [Label("Center"), JsonProperty, RecordHistory]
    public Vector3 Center { get; set; } = Vector3.Zero;

    [JsonIgnore] public BoxShape? Shape;

    public override bool Load() {

        var isPrefabInstance = Obj.FindPrefabRoot() != null;
        Vector3 scale;
        if (isPrefabInstance) {
            scale = Obj.Transform.Scale;
        } else {
            Obj.DecomposeWorldMatrix(out _, out _, out scale);
        }
        var width = MathF.Max(0.001f, MathF.Abs(Size.X * scale.X));
        var height = MathF.Max(0.001f, MathF.Abs(Size.Y * scale.Y));
        var length = MathF.Max(0.001f, MathF.Abs(Size.Z * scale.Z));
        Shape = new BoxShape(width, height, length);

        return true;
    }

    public override void Render3D() {

        if (!IsSelected || CommandLine.Runtime) return;

        var colorVisible = Color.Lime;
        var colorHidden = Raylib.ColorAlpha(Color.Lime, 0.15f);

        // Box scale works correctly with WorldMatrix
        Rlgl.PushMatrix();
        Rlgl.MultMatrixf(Obj.WorldMatrix);

        // Hidden
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthTest();
        Raylib.DrawCubeWires(Center, Size.X, Size.Y, Size.Z, colorHidden);
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthTest();

        // Visible
        Raylib.DrawCubeWires(Center, Size.X, Size.Y, Size.Z, colorVisible);

        Rlgl.PopMatrix();
    }
}
