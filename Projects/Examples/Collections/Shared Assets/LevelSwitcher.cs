using Raylib_cs;

namespace SharedAssets;

internal class LevelSwitcher : ScytheScript {

    private string[] _levels = [ "FPS", "Dwarf", "Blocks", "Cube" ];
    private int levelId = 0;
    
    public override void Loop(float dt) {

        if (Raylib.IsKeyPressed(KeyboardKey.Left))
            SwitchLevel(levelId + 1);
        
        if (Raylib.IsKeyPressed(KeyboardKey.Right))
            SwitchLevel(levelId - 1);
    }

    private void SwitchLevel(int id) {

        levelId = (int)Raymath.Repeat(id, _levels.Length);
    }
}