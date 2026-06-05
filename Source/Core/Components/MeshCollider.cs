using System.Numerics;
using Jitter2.Collision.Shapes;
using Jitter2.LinearMath;
using Newtonsoft.Json;
using Raylib_cs;
using static Raylib_cs.Raylib;

internal class MeshCollider(Obj obj) : Component(obj) {

    public override Color LabelColor => Colors.GuiTypePhysics;
    public override string LabelIcon => Icons.FaCube;

    [Label("Convex"), JsonProperty, RecordHistory]
    public bool Convex { get; set; } = true;

    [Label("Center"), JsonProperty, RecordHistory]
    public Vector3 Center { get; set; } = Vector3.Zero;

    [JsonIgnore] public List<RigidBodyShape> Shapes { get; } = [];
    [JsonIgnore] public List<TriangleMesh> TriangleMeshes { get; } = [];

    public override bool Load() {

        Shapes.Clear();
        TriangleMeshes.Clear();

        if (!TryGetModel(out var model)) return false;

        var scale = GetPhysicsScale();
        var importScale = model.AssetRef is { IsLoaded: true } ? model.AssetRef.Settings.ImportScale : 1f;
        var vertices = BuildVertices(model, scale, importScale);

        if (vertices.Length == 0) return true;

        if (Convex) {
            try {
                Shapes.Add(new PointCloudShape(vertices));
            } catch (ArgumentException) {
                return true;
            } catch (InvalidOperationException) {
                return true;
            }

            return true;
        }

        foreach (var mesh in model.Meshes) {

            if (mesh.Vertices.Length == 0 || mesh.Indices.Length < 3) continue;

            var meshVertices = new JVector[mesh.Vertices.Length];
            for (var i = 0; i < mesh.Vertices.Length; i++)
                meshVertices[i] = Conversion.ToJitter(mesh.Vertices[i] * importScale * scale);

            var indices = new int[mesh.Indices.Length];
            var valid = true;
            for (var i = 0; i < mesh.Indices.Length; i++) {
                if (mesh.Indices[i] > int.MaxValue) {
                    valid = false;
                    break;
                }

                indices[i] = (int)mesh.Indices[i];
            }

            if (!valid) continue;

            try {
                var triangleMesh = new TriangleMesh(meshVertices, indices, true);
                TriangleMeshes.Add(triangleMesh);
                Shapes.AddRange(TriangleShape.CreateAllShapes(triangleMesh));
            } catch (ArgumentException) {
                // Invalid source mesh data should not prevent the object from loading.
            } catch (IndexOutOfRangeException) {
            } catch (TriangleMesh.DegenerateTriangleException) {
            }
        }

        return true;
    }

    public override void Render3D() {

        if (!IsSelected || CommandLine.Runtime || !TryGetModel(out var model)) return;

        var colorVisible = Color.Lime;
        var colorHidden = ColorAlpha(Color.Lime, 0.15f);

        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthTest();
        DrawModelWire(model, colorHidden);
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthTest();

        DrawModelWire(model, colorVisible);
    }

    private bool TryGetModel(out Model model) {

        model = null!;

        if (!Obj.ComponentEntries.TryGetValue("Model", out var component)) return false;

        model = (Model)component;
        if (!model.IsLoaded) {
            if (!model.Load()) return false;
            model.IsLoaded = true;
        }

        return model.AssetRef is { IsLoaded: true } && model.Meshes.Count > 0;
    }

    private Vector3 GetPhysicsScale() {

        var isPrefabInstance = Obj.FindPrefabRoot() != null;
        if (isPrefabInstance) return Obj.Transform.Scale;

        Obj.DecomposeWorldMatrix(out _, out _, out var scale);
        return scale;
    }

    private static JVector[] BuildVertices(Model model, Vector3 scale, float importScale) {

        var count = model.Meshes.Sum(mesh => mesh.Vertices.Length);
        var vertices = new JVector[count];
        var index = 0;

        foreach (var mesh in model.Meshes)
        foreach (var vertex in mesh.Vertices)
            vertices[index++] = Conversion.ToJitter(vertex * importScale * scale);

        return vertices;
    }

    private void DrawModelWire(Model model, Color color) {

        var importScale = model.AssetRef is { IsLoaded: true } ? model.AssetRef.Settings.ImportScale : 1f;

        foreach (var mesh in model.Meshes) {

            for (var i = 0; i + 2 < mesh.Indices.Length; i += 3) {

                var i0 = mesh.Indices[i];
                var i1 = mesh.Indices[i + 1];
                var i2 = mesh.Indices[i + 2];

                if (i0 >= mesh.Vertices.Length || i1 >= mesh.Vertices.Length || i2 >= mesh.Vertices.Length) continue;

                var v0 = ToWorld(mesh.Vertices[i0] * importScale + Center);
                var v1 = ToWorld(mesh.Vertices[i1] * importScale + Center);
                var v2 = ToWorld(mesh.Vertices[i2] * importScale + Center);

                DrawLine3D(v0, v1, color);
                DrawLine3D(v1, v2, color);
                DrawLine3D(v2, v0, color);
            }
        }
    }

    private Vector3 ToWorld(Vector3 local) => Raymath.Vector3Transform(local, Obj.WorldMatrix);
}
