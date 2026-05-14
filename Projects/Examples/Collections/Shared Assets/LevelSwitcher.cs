using Raylib_cs;

namespace SharedAssets;

internal class LevelSwitcher : ScytheScript {

    [Expose] private string previousLevelName;
    [Expose] private string nextLevelName;
    
    public override void Loop(float dt) {

        if (Raylib.IsKeyPressed(KeyboardKey.Left))
            SwitchLevel(previousLevelName);
        
        if (Raylib.IsKeyPressed(KeyboardKey.Right))
            SwitchLevel(nextLevelName);
    }

    private void SwitchLevel(string name) {
        
        var oldLevelIndex = Core.ActiveLevelIndex;

        Core.OpenLevel("name"); 

        if (oldLevelIndex != -1)
            Core.CloseLevel(oldLevelIndex);
    }
}