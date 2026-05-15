using Raylib_cs;

namespace SharedAssets;

internal class LevelSwitcher : ScytheScript {

    [Config, FindAsset("LevelAsset")] private string[] levels = [];

    private int _levelId;

    public override void Loop(float dt) {

        if (Raylib.IsKeyPressed(KeyboardKey.Left))
            SwitchLevel(_levelId - 1);

        if (Raylib.IsKeyPressed(KeyboardKey.Right))
            SwitchLevel(_levelId + 1);
    }

    private void SwitchLevel(int id) {

        if (levels.Length == 0) return;

        _levelId = (int)Raymath.Repeat(id, levels.Length);
        Core.SwitchLevel(levels[_levelId]);
    }
}
