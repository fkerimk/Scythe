using Raylib_cs;

internal class PistolAnimation : ScytheScript {
    
    private Animation? _anim;
    private int _track = 0;

    public override void Start() {
        _anim = GetComponent<Animation>();
    }

    public override void Loop(float dt) {
        int targetTrack = 0;

        if (Raylib.IsKeyDown(KeyboardKey.D) || Raylib.IsKeyDown(KeyboardKey.A) || 
            Raylib.IsKeyDown(KeyboardKey.W) || Raylib.IsKeyDown(KeyboardKey.S)) {
            targetTrack = 2;
        }

        if (targetTrack != _track) {
            _track = targetTrack;
            _anim?.Play(_track, 0.2f);
        }
    }
}
