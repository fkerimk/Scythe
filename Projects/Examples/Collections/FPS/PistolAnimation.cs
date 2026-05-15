using Raylib_cs;

internal class PistolAnimation : ScytheScript {
    
    [Expose] private Animation _animationComponent;
    
    private int _track;

    public override void Loop(float dt) {
        
        var targetTrack = 0;

        if (Raylib.IsKeyDown(KeyboardKey.D) || Raylib.IsKeyDown(KeyboardKey.A) || 
            Raylib.IsKeyDown(KeyboardKey.W) || Raylib.IsKeyDown(KeyboardKey.S)) {
            
            targetTrack = 2;
        }

        if (targetTrack == _track) return;
        
        _track = targetTrack;
        _animationComponent.Play(_track, 0.2f);
    }
}
