using System.Numerics;
using Newtonsoft.Json;
using Raylib_cs;

internal class Animation(Obj obj) : Component(obj) {

    public override string LabelIcon => Icons.FaPlayCircle;
    public override Color LabelColor => Colors.GuiTypeAnimation;

    [Label("Path"), JsonProperty, RecordHistory, FindAsset("AnimationAsset")]
    public string Path { get; set; } = "";

    [Label("Track"), JsonProperty, RecordHistory]
    public int Track {
        get => _track;
        set {
            if (!IsLoaded) return;

            if (_asset.Animations.Count == 0 || value < 0)
                _track = 0;
            else if (value >= _asset.Animations.Count)
                _track = _asset.Animations.Count - 1;
            else
                _track = value;

            // Trigger blend when track changes at runtime if already playing
            if (IsPlaying && _lastTrack != -1 && _track != _lastTrack) Play(_track);
        }
    }

    [Label("Is Playing"), JsonProperty, RecordHistory]
    public bool IsPlaying { get; set; } = true;

    [Label("Looping"), JsonProperty, RecordHistory]
    public bool Looping { get; set; } = true;

    private int _track;
    private float _frameRaw;
    private float _prevFrameRaw;

    // Blending state
    private int _lastTrack = -1;
    private float _blendWeight = 1.0f;
    private float _blendDuration = 0.25f;
    private float _blendTimer;

    private AnimationAsset _asset = null!;

    public override bool Load() {

        var loaded = AssetManager.Get<AnimationAsset>(Path);

        if (loaded is not { IsLoaded: true }) return false;

        _asset = loaded;
        _track = (int)Raymath.Clamp(_track, 0, _asset.Animations.Count - 1);

        return true;
    }

    public void Play(int trackIndex, float blendTime = 0.25f) {

        if (trackIndex < 0 || trackIndex >= _asset.Animations.Count) return;

        if (blendTime <= 0) {

            _track = trackIndex;
            _lastTrack = -1;
            _frameRaw = 0;
            _blendWeight = 1.0f;

        } else {

            _lastTrack = _track;
            _prevFrameRaw = _frameRaw;
            _track = trackIndex;
            _frameRaw = 0; // Start new track from beginning
            _blendWeight = 0.0f;
            _blendDuration = blendTime;
            _blendTimer = 0f;
        }

        IsPlaying = true;
    }

    public override void Logic() {

        if (_asset is not { IsLoaded: true } || _asset.Animations.Count == 0) return;
        if (!IsPlaying || !Obj.Components.TryGetValue("Model", out var component) || component is not Model { IsLoaded: true } model) return;

        var modelAsset = model.AssetRef;

        if (!modelAsset.IsLoaded) return;

        UpdateTimers();

        var currentClip = _asset.Animations[_track];

        if (_lastTrack != -1 && _lastTrack < _asset.Animations.Count) {

            var prevClip = _asset.Animations[_lastTrack];

            // Blending two animations
            AssimpLoader.UpdateAnimationBlended(modelAsset.RootNode, prevClip, _prevFrameRaw, currentClip, _frameRaw, _blendWeight, Matrix4x4.Identity, modelAsset.GlobalInverse, model.BoneMap);

        } else {

            // Single animation
            AssimpLoader.UpdateAnimation(modelAsset.RootNode, currentClip, _frameRaw, Matrix4x4.Identity, modelAsset.GlobalInverse, model.BoneMap);
        }

        foreach (var mesh in model.Meshes) AssimpLoader.SkinMesh(mesh, model.Bones);
    }

    private void UpdateTimers() {

        var dt = Raylib.GetFrameTime();
        var currentClip = _asset.Animations[_track];

        // Update current frame
        _frameRaw += dt * (float)currentClip.TicksPerSecond;

        if (_frameRaw >= currentClip.Duration) {

            if (Looping)
                _frameRaw %= (float)currentClip.Duration;
            else {
                _frameRaw = (float)currentClip.Duration;

                // If we are not looping, we might want to stop playing if blend is finished
                if (_blendWeight >= 1.0f) IsPlaying = false;
            }
        }

        // Update previous frame if blending
        if (_lastTrack != -1) {

            var prevClip = _asset.Animations[_lastTrack];

            _prevFrameRaw += dt * (float)prevClip.TicksPerSecond;

            if (_prevFrameRaw >= prevClip.Duration) {

                if (Looping)
                    _prevFrameRaw %= (float)prevClip.Duration;
                else
                    _prevFrameRaw = (float)prevClip.Duration;
            }

            // Update blend weight
            _blendTimer += dt;
            _blendWeight = Math.Clamp(_blendTimer / _blendDuration, 0f, 1f);

            if (_blendWeight >= 1.0f) _lastTrack = -1;
        }
    }
}