using System.Numerics;
using Raylib_cs;
using static Raylib_cs.Raylib;

internal static class PostProcessing {

    private static Dictionary<string, Shader> _shaders = new();
    private static RenderTexture2D            _tempRt;
    private static RenderTexture2D            _tempRt2;
    private static bool                       _extReady;

    // TAA
    private static RenderTexture2D _historyRt;
    private static Matrix4x4       _prevViewProj;
    private static int             _frameIndex;
    private static Vector2         _jitter;
    private static bool            _hasHistory;

    public static bool HasHistory => _hasHistory;
    public static Matrix4x4 PreviousViewProj => _hasHistory ? _prevViewProj : Core.LastViewMatrix * Core.LastProjectionMatrix;

    public static void Init() {

        var shaderFiles = new[] { "bloom", "blur", "cross_hatching", "cross_stitching", "dream_vision", "fisheye", "fxaa", "smaa", "grayscale", "pixelizer", "posterization", "predator", "scanlines", "sobel", "ssao", "taa" };

        foreach (var name in shaderFiles) {
            var shaderAsset = AssetManager.GetOrImport<ShaderAsset>($"Collection/{name}.fs");

            if (shaderAsset is { Shader.Id: not 0 })
                _shaders[name] = shaderAsset.Shader;
            else
                TraceLog(TraceLogLevel.Error, $"POSTPRO: Failed to resolve shader {name} from built-in collection");
        }

        _extReady = true;
    }

    private static void EnsureRt(int width, int height) {

        if (_tempRt.Texture.Width == width && _tempRt.Texture.Height == height) return;

        if (_tempRt.Texture.Id != 0) UnloadRenderTexture(_tempRt);
        _tempRt = LoadRenderTexture(width, height);
        SetTextureWrap(_tempRt.Texture, TextureWrap.Clamp);

        if (_tempRt2.Texture.Id != 0) UnloadRenderTexture(_tempRt2);
        _tempRt2 = LoadRenderTexture(width, height);
        SetTextureWrap(_tempRt2.Texture, TextureWrap.Clamp);
    }

    public static void Apply(RenderTexture2D target, Texture2D? velocityTexture = null) {

        if (!_extReady) Init();

        if (target.Texture.Id == 0) return;

        var settings = Core.RenderSettings.PostProcessing;
        var width    = target.Texture.Width;
        var height   = target.Texture.Height;

        EnsureRt(width, height);

        var currentSource = target;
        var currentDest   = _tempRt;

        // Helper to apply a shader
        void ApplyShader(string shaderName, Action<Shader>? setUniforms = null) {

            if (!_shaders.TryGetValue(shaderName, out var shader)) return;

            BeginTextureMode(currentDest);
            ClearBackground(Color.Blank);

            BeginShaderMode(shader);

            var resLoc = GetShaderLocation(shader, "renderSize");
            if (resLoc != -1) SetShaderValue(shader, resLoc, new Vector2(width, height), ShaderUniformDataType.Vec2);

            var wLoc = GetShaderLocation(shader, "renderWidth");
            if (wLoc != -1) SetShaderValue(shader, wLoc, (float)width, ShaderUniformDataType.Float);

            var hLoc = GetShaderLocation(shader, "renderHeight");
            if (hLoc != -1) SetShaderValue(shader, hLoc, (float)height, ShaderUniformDataType.Float);

            var resolLoc = GetShaderLocation(shader, "resolution");
            if (resolLoc != -1) SetShaderValue(shader, resolLoc, new Vector2(width, height), ShaderUniformDataType.Vec2);

            setUniforms?.Invoke(shader);

            DrawTextureRec(currentSource.Texture, new Rectangle(0, 0, width, -height), Vector2.Zero, Color.White);

            EndShaderMode();
            EndTextureMode();

            // Swap for next pass
            currentSource = currentDest;
            currentDest   = currentDest.Id == _tempRt.Id ? _tempRt2 : _tempRt;
        }

        // Apply SSAO first
        if (settings.Ssao.Enabled)
            ApplyShader(
                "ssao",
                (s) => {

                    SetShaderValue(s, GetShaderLocation(s, "ssaoRadius"),    settings.Ssao.Radius,    ShaderUniformDataType.Float);
                    SetShaderValue(s, GetShaderLocation(s, "ssaoBias"),      settings.Ssao.Bias,      ShaderUniformDataType.Float);
                    SetShaderValue(s, GetShaderLocation(s, "ssaoIntensity"), settings.Ssao.Intensity, ShaderUniformDataType.Float);

                    // Set depth texture to slot 1
                    int depthLoc = GetShaderLocation(s, "depthTexture");

                    if (depthLoc != -1) {
                        Rlgl.ActiveTextureSlot(1);
                        Rlgl.EnableTexture(target.Depth.Id);
                        SetShaderValue(s, depthLoc, 1, ShaderUniformDataType.Int);
                        Rlgl.ActiveTextureSlot(0);
                    }

                    if (Core.ActiveCamera != null) {
                        var proj = Core.LastProjectionMatrix;
                        SetShaderValueMatrix(s, GetShaderLocation(s, "matProjection"),        proj);
                        SetShaderValueMatrix(s, GetShaderLocation(s, "matProjectionInverse"), Raymath.MatrixInvert(proj));
                    }
                }
            );

        // TAA
        if (settings.Taa.Enabled) {

            // Ensure History RT
            if (_historyRt.Texture.Id == 0 || _historyRt.Texture.Width != width || _historyRt.Texture.Height != height) {
                if (_historyRt.Texture.Id != 0) UnloadRenderTexture(_historyRt);
                _historyRt = LoadRenderTexture(width, height);
                SetTextureWrap(_historyRt.Texture, TextureWrap.Clamp);
                SetTextureFilter(_historyRt.Texture, TextureFilter.Bilinear);
                _hasHistory = false;
            }

            ApplyShader(
                "taa",
                (s) => {

                    SetShaderValueMatrix(s, GetShaderLocation(s, "matViewProjInv"),  Raymath.MatrixInvert(Core.LastViewMatrix * Core.LastProjectionMatrix));
                    SetShaderValueMatrix(s, GetShaderLocation(s, "matPrevViewProj"), _prevViewProj);
                    SetShaderValue(s, GetShaderLocation(s,       "jitter"),       _jitter,                           ShaderUniformDataType.Vec2);
                    SetShaderValue(s, GetShaderLocation(s,       "blendFactor"),  settings.Taa.BlendFactor,          ShaderUniformDataType.Float);
                    SetShaderValue(s, GetShaderLocation(s,       "varianceClip"), settings.Taa.VarianceClip ? 1 : 0, ShaderUniformDataType.Int);
                    SetShaderValue(s, GetShaderLocation(s,       "scale"),        settings.Taa.Scale,                ShaderUniformDataType.Float);
                    SetShaderValue(s, GetShaderLocation(s,       "hasHistory"),   _hasHistory ? 1 : 0,               ShaderUniformDataType.Int);

                    int historyLoc = GetShaderLocation(s, "historyTexture");

                    if (historyLoc != -1) {
                        Rlgl.ActiveTextureSlot(2);
                        Rlgl.EnableTexture(_historyRt.Texture.Id);
                        SetShaderValue(s, historyLoc, 2, ShaderUniformDataType.Int);
                        Rlgl.ActiveTextureSlot(0);
                    }

                    int depthLoc = GetShaderLocation(s, "depthTexture");

                    if (depthLoc != -1) {
                        Rlgl.ActiveTextureSlot(1);
                        Rlgl.EnableTexture(target.Depth.Id);
                        SetShaderValue(s, depthLoc, 1, ShaderUniformDataType.Int);
                        Rlgl.ActiveTextureSlot(0);
                    }

                    int velocityLoc = GetShaderLocation(s, "velocityTexture");

                    if (velocityLoc != -1 && velocityTexture.HasValue && velocityTexture.Value.Id != 0) {
                        Rlgl.ActiveTextureSlot(3);
                        Rlgl.EnableTexture(velocityTexture.Value.Id);
                        SetShaderValue(s, velocityLoc, 3, ShaderUniformDataType.Int);
                        Rlgl.ActiveTextureSlot(0);
                    }
                }
            );

            // Setup next frame history
            _prevViewProj = Core.LastViewMatrix * Core.LastProjectionMatrix;

            // Copy result to history
            BeginTextureMode(_historyRt);
            DrawTextureRec(currentSource.Texture, new Rectangle(0, 0, width, -height), Vector2.Zero, Color.White);
            EndTextureMode();
            _hasHistory = true;
        }

        if (settings.Bloom.Enabled) ApplyShader("bloom", (s) => SetShaderValue(s, GetShaderLocation(s, "intensity"), settings.Bloom.Intensity, ShaderUniformDataType.Float));
        if (settings.Blur.Enabled) ApplyShader("blur",   (s) => SetShaderValue(s, GetShaderLocation(s, "radius"),    settings.Blur.Radius,     ShaderUniformDataType.Float));
        if (settings.Grayscale.Enabled) ApplyShader("grayscale");
        if (settings.Posterization.Enabled) ApplyShader("posterization", (s) => SetShaderValue(s, GetShaderLocation(s, "numColors"), settings.Posterization.Levels, ShaderUniformDataType.Float));
        if (settings.DreamVision.Enabled) ApplyShader("dream_vision");
        if (settings.Pixelizer.Enabled)
            ApplyShader(
                "pixelizer",
                (s) => {
                    SetShaderValue(s, GetShaderLocation(s, "pixelWidth"),  settings.Pixelizer.Size, ShaderUniformDataType.Float);
                    SetShaderValue(s, GetShaderLocation(s, "pixelHeight"), settings.Pixelizer.Size, ShaderUniformDataType.Float);
                }
            );
        if (settings.CrossHatching.Enabled) ApplyShader("cross_hatching");
        if (settings.CrossStitching.Enabled) ApplyShader("cross_stitching", (s) => SetShaderValue(s, GetShaderLocation(s, "stitchingSize"), settings.CrossStitching.Size, ShaderUniformDataType.Float));
        if (settings.Predator.Enabled) ApplyShader("predator");
        if (settings.Sobel.Enabled) ApplyShader("sobel");
        if (settings.Scanlines.Enabled) ApplyShader("scanlines", (s) => SetShaderValue(s, GetShaderLocation(s, "time"), (float)GetTime(), ShaderUniformDataType.Float));
        if (settings.Fisheye.Enabled) ApplyShader("fisheye");
        if (settings.Fxaa.Enabled) ApplyShader("fxaa");
        if (settings.Smaa.Enabled) ApplyShader("smaa");

        // If the final result is not in target, copy it back
        if (currentSource.Id != target.Id) {

            BeginTextureMode(target);
            DrawTextureRec(currentSource.Texture, new Rectangle(0, 0, width, -height), Vector2.Zero, Color.White);
            EndTextureMode();
        }
    }

    public static void ApplyJitter(Camera3D camera, bool advanceFrame = true) {

        if (!Core.RenderSettings.PostProcessing.Taa.Enabled) return;

        var width  = GetScreenWidth();
        var height = GetScreenHeight();

        if (advanceFrame) {
            // Halton Sequence (2, 3)
            var jX = GetHalton(_frameIndex % 16 + 1, 2) - 0.5f;
            var jY = GetHalton(_frameIndex % 16 + 1, 3) - 0.5f;

            _jitter = new Vector2(jX * 2.0f / width, jY * 2.0f / height);
        }

        var proj = Rlgl.GetMatrixProjection();

        proj.M13 += _jitter.X;
        proj.M23 += _jitter.Y;

        Rlgl.SetMatrixProjection(proj);

        if (advanceFrame) _frameIndex++;
    }

    private static float GetHalton(int index, int baseVal) {

        var result = 0.0f;
        var f      = 1.0f;

        while (index > 0) {

            f      /= baseVal;
            result += f * (index % baseVal);
            index  /= baseVal;
        }

        return result;
    }

    public static void Shutdown() {

        if (_tempRt.Texture.Id    != 0) UnloadRenderTexture(_tempRt);
        if (_tempRt2.Texture.Id   != 0) UnloadRenderTexture(_tempRt2);
        if (_historyRt.Texture.Id != 0) UnloadRenderTexture(_historyRt);
        _hasHistory = false;
        _shaders.Clear();
    }
}
