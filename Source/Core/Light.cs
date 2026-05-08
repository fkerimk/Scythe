using System.ComponentModel;
using System.Numerics;
using Newtonsoft.Json;
using Raylib_cs;

internal class Light(Obj obj) : Component(obj) {

    public override string LabelIcon  => Icons.FaLightbulbO;
    public override Color  LabelColor => Colors.GuiTypeLight;

    [Label("Enabled"), JsonProperty, RecordHistory, DefaultValue(true)]
    public bool Enabled { get; set; } = true;

    [Label("Type"), JsonProperty, RecordHistory, DefaultValue(1)]
    public int Type { get; set => field = (int)Raymath.Clamp(value, 0, 2); } = 1;

    [Label("Color"), JsonProperty, RecordHistory]
    public Color ScytheColor { get; set; } = Color.White;

    [Label("Intensity"), JsonProperty, RecordHistory, DefaultValue(2)]
    public float Intensity { get; set; } = 2;

    [Label("Range"), JsonProperty, RecordHistory, DefaultValue(10)]
    public float Range { get; set; } = 10;

    [Label("Shadows"), JsonProperty, RecordHistory, DefaultValue(true)]
    public bool Shadows { get; set; } = true;

    [Label("Shadow Strength"), JsonProperty, RecordHistory, DefaultValue(1)]
    public float ShadowStrength { get; set; } = 1;

    private Vector3 _pos    = Vector3.Zero;
    private Vector3 _target = Vector3.Zero;

    public void Update(int index) {

        var pbr = AssetManager.Get<ShaderAsset>("Collection/pbr.vs");

        if (pbr == null) return;

        Raylib.SetShaderValue(pbr.Shader, pbr.GetLoc($"lights[{index}].enabled"),   Enabled ? 1 : 0,         ShaderUniformDataType.Int);
        Raylib.SetShaderValue(pbr.Shader, pbr.GetLoc($"lights[{index}].type"),      Type,                    ShaderUniformDataType.Int);
        Raylib.SetShaderValue(pbr.Shader, pbr.GetLoc($"lights[{index}].position"),  _pos,                    ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(pbr.Shader, pbr.GetLoc($"lights[{index}].target"),    _target,                 ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(pbr.Shader, pbr.GetLoc($"lights[{index}].color"),     ScytheColor.ToVector4(), ShaderUniformDataType.Vec4);
        Raylib.SetShaderValue(pbr.Shader, pbr.GetLoc($"lights[{index}].intensity"), Intensity,               ShaderUniformDataType.Float);
    }

    public override void Logic() {

        Obj.DecomposeWorldMatrix(out var worldPos, out _, out _);

        _pos    = worldPos;
        _target = _pos + Obj.Fwd * (Type == 0 ? 1 : Range);
    }

    public override void Render3D() {

        if (Core.IsPreviewRender) return;

        if (IsSelected) {

            var gizmoColor = Raylib.ColorAlpha(ScytheColor, 0.3f);

            switch (Type) {

                case 1: Raylib.DrawSphereWires(_pos, Range, 8, 8, gizmoColor); break;

                case 2: {

                    var baseCenter = _pos + Obj.Fwd * Range;
                    var coneRadius = Range;

                    for (var i = 0; i < 8; i++) {

                        var angle     = i       / 8f * MathF.PI * 2f;
                        var nextAngle = (i + 1) / 8f * MathF.PI * 2f;

                        var offset1 = Obj.Right * MathF.Cos(angle)     * coneRadius + Obj.Up * MathF.Sin(angle)     * coneRadius;
                        var offset2 = Obj.Right * MathF.Cos(nextAngle) * coneRadius + Obj.Up * MathF.Sin(nextAngle) * coneRadius;

                        var point1 = baseCenter + offset1;
                        var point2 = baseCenter + offset2;

                        Raylib.DrawLine3D(point1, point2, gizmoColor);
                    }

                    // sides
                    const float sides = 4;

                    for (var i = 0; i < sides; i++) {

                        var angle = i / sides * MathF.PI * 2f;

                        var offset = Obj.Right * MathF.Cos(angle) * coneRadius + Obj.Up * MathF.Sin(angle) * coneRadius;
                        var point  = baseCenter                                + offset;

                        Raylib.DrawLine3D(_pos, point, gizmoColor);
                    }

                    break;
                }
            }
        }

        if (Core.IsPlaying) return;

        if ((!OldConfig.Runtime.DrawLights || !CommandLine.Runtime) && (!OldConfig.Editor.DrawLights || CommandLine.Runtime)) return;

        Raylib.DrawSphereWires(_pos, 0.1f, 8, 8, Enabled ? Raylib.ColorAlpha(ScytheColor, 0.8f) : Raylib.ColorAlpha(ScytheColor, 0.2f));
    }
}
