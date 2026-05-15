using Raylib_cs;

namespace FPS;

internal class PistolAnimation : ScytheScript {
    
    [Expose] private Animation _animation;
    
    private int _track;

    public override void Loop(float dt) {
        
        var targetTrack = 0;

        if (Raylib.IsKeyDown(KeyboardKey.D) || Raylib.IsKeyDown(KeyboardKey.A) || 
            Raylib.IsKeyDown(KeyboardKey.W) || Raylib.IsKeyDown(KeyboardKey.S)) {
            
            targetTrack = 2;
        }

        if (targetTrack == _track) return;
        
        _track = targetTrack;
        _animation.Play(_track, 0.2f);
    }
}
