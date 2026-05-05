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

            var oldTrack = _track;

            if (_asset.Animations.Count == 0 || value < 0)
                _track = 0;
            else if (value >= _asset.Animations.Count)
                _track = _asset.Animations.Count - 1;
            else
                _track = value;

            if (_track == oldTrack) return;

            if (CommandLine.Runtime || Core.IsPlaying) {

                if (IsPlaying) StartPlayback(_track, 0.25f, false);

            } else if (EditorPreviewPlaying)
                StartPlayback(_track, 0.25f, true);
        }
    }

    [Label("Is Playing"), JsonProperty, RecordHistory]
    public bool IsPlaying { get; set; } = true;

    [Label("Looping"), JsonProperty, RecordHistory]
    public bool Looping { get; set; } = true;

    [JsonIgnore]
    public bool EditorPreviewEnabled { get; private set; }

    [JsonIgnore]
    public bool EditorPreviewPlaying { get; private set; }

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
        EditorPreviewEnabled = false;
        EditorPreviewPlaying = false;

        return true;
    }

    public void Play(int trackIndex, float blendTime = 0.25f) {

        StartPlayback(trackIndex, blendTime, false);
    }

    public void PlayPreview(float blendTime = 0.25f) {

        if (EditorPreviewEnabled && !EditorPreviewPlaying) {

            EditorPreviewPlaying = true;
            return;
        }

        StartPlayback(_track, blendTime, true);
    }

    public void PausePreview() {

        if (!EditorPreviewEnabled) return;
        EditorPreviewPlaying = false;
    }

    public void StopPreview() {

        EditorPreviewEnabled = false;
        EditorPreviewPlaying = false;
        _lastTrack = -1;
        _blendWeight = 1.0f;
        _blendTimer = 0f;
        _frameRaw = 0f;
        _prevFrameRaw = 0f;
    }

    public float CurrentTime {
        get {

            var clip = CurrentClip;
            if (clip == null) return 0f;
            if (clip.TicksPerSecond == 0) return 0f;

            return _frameRaw / (float)clip.TicksPerSecond;
        }
        set {

            var clip = CurrentClip;
            if (clip == null) return;

            var duration = DurationSeconds;
            var clamped = Math.Clamp(value, 0f, duration);
            _frameRaw = clamped * (float)clip.TicksPerSecond;
            EditorPreviewEnabled = true;
            EditorPreviewPlaying = false;

            if (!Looping && _frameRaw >= clip.Duration)
                EditorPreviewPlaying = false;

            _lastTrack = -1;
            _blendWeight = 1.0f;
            _blendTimer = 0f;
        }
    }

    public float DurationSeconds {
        get {

            var clip = CurrentClip;
            if (clip == null || clip.TicksPerSecond == 0) return 0f;

            return (float)(clip.Duration / clip.TicksPerSecond);
        }
    }

    public float CurrentFrame {
        get => _frameRaw;
        set {

            var clip = CurrentClip;
            if (clip == null) return;

            _frameRaw = Math.Clamp(value, 0f, (float)clip.Duration);
            EditorPreviewEnabled = true;
            EditorPreviewPlaying = false;
            _lastTrack = -1;
            _blendWeight = 1.0f;
            _blendTimer = 0f;
        }
    }

    public float DurationFrames => CurrentClip == null ? 0f : (float)CurrentClip.Duration;

    public bool HasPreviewClip => CurrentClip != null;

    private AnimationClip? CurrentClip =>
        _asset is { IsLoaded: true } && _asset.Animations.Count > 0 && _track >= 0 && _track < _asset.Animations.Count
            ? _asset.Animations[_track]
            : null;

    private void StartPlayback(int trackIndex, float blendTime, bool editorPreviewMode) {

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

        if (editorPreviewMode)
            EditorPreviewEnabled = true;

        if (editorPreviewMode)
            EditorPreviewPlaying = true;
        else
            IsPlaying = true;
    }

    public override void Logic() {

        if (_asset is not { IsLoaded: true } || _asset.Animations.Count == 0) return;
        if (!Obj.Components.TryGetValue("Model", out var component) || component is not Model { IsLoaded: true } model) return;

        var modelAsset = model.AssetRef;

        if (!modelAsset.IsLoaded) return;

        var inRuntime = CommandLine.Runtime || Core.IsPlaying;
        var shouldAdvance = inRuntime ? IsPlaying : EditorPreviewPlaying;

        if (!inRuntime) {

            if (!EditorPreviewEnabled) {

                ApplyBindPose(model);
                return;
            }
        }

        if (!inRuntime && !HasPreviewClip) return;
        if (inRuntime && !IsPlaying) return;

        if (shouldAdvance) UpdateTimers(inRuntime);

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

    private static void ApplyBindPose(Model model) {

        AssimpLoader.ApplyBindPose(model.AssetRef.RootNode, Matrix4x4.Identity, model.AssetRef.GlobalInverse, model.BoneMap);

        foreach (var mesh in model.Meshes) AssimpLoader.SkinMesh(mesh, model.Bones);
    }

    private void UpdateTimers(bool inRuntime) {

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
                if (_blendWeight >= 1.0f) {

                    if (inRuntime)
                        IsPlaying = false;
                    else
                        EditorPreviewPlaying = false;
                }
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
