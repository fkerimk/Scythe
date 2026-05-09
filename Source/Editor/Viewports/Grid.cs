using System.Numerics;
using Raylib_cs;

internal static class Grid {

    public static void Draw(Camera3D? camera) {

        if (camera == null) return;

        var gridPos = new Vector3((int)camera.Position.X, 0, (int)camera.Position.Z);

        var grid = AssetManager.Get<ShaderAsset>("Collection/grid.vs");

        if (grid == null) return;

        Raylib.BeginShaderMode(grid.Shader);
        Raylib.SetShaderValue(grid.Shader, grid.GetLoc("cameraPos"), camera.Position, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(grid.Shader, grid.GetLoc("fadeRadius"), 50.0f, ShaderUniformDataType.Float);

        Rlgl.PushMatrix();
        Rlgl.Translatef(gridPos.X, gridPos.Y, gridPos.Z);

        const int slices = 50;
        const float spacing = 1f;
        const float half = slices * spacing;

        Raylib.BeginBlendMode(BlendMode.Alpha);

        for (var i = 0; i <= slices * 2; i++) {

            var pos = -half + i * spacing;

            var startX = new Vector3(-half, 0, pos);
            var endX = new Vector3(half, 0, pos);
            Raylib.DrawLine3D(startX, endX, Colors.Grid);

            var startZ = new Vector3(pos, 0, -half);
            var endZ = new Vector3(pos, 0, half);
            Raylib.DrawLine3D(startZ, endZ, Colors.Grid);
        }

        Raylib.EndBlendMode();
        Rlgl.PopMatrix();
        Raylib.EndShaderMode();
    }
}