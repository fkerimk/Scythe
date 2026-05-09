using Raylib_cs;

// ReSharper disable FieldCanBeMadeReadOnly.Global
// ReSharper disable ConvertToConstant.Global
internal class RenderSettings {

    public float AmbientIntensity = 0.75f;
    public Color AmbientColor = Color.White;
    public float ShadowFovScale = 1.0f;
    public float ShadowBias = 0.00005f;

    public PostProcessingSettings PostProcessing = new();
}

internal class PostProcessingSettings {

    public BloomSettings Bloom = new();
    public BlurSettings Blur = new();
    public GrayscaleSettings Grayscale = new();
    public PosterizationSettings Posterization = new();
    public DreamVisionSettings DreamVision = new();
    public PixelizerSettings Pixelizer = new();
    public CrossHatchingSettings CrossHatching = new();
    public CrossStitchingSettings CrossStitching = new();
    public PredatorSettings Predator = new();
    public SobelSettings Sobel = new();
    public ScanlinesSettings Scanlines = new();
    public FisheyeSettings Fisheye = new();
    public SsaoSettings Ssao = new();
    public FxaaSettings Fxaa = new();
    public SmaaSettings Smaa = new();
    public TaaSettings Taa = new();
}

internal class FxaaSettings {
    public bool Enabled = false;
}

internal class SmaaSettings {
    public bool Enabled = false;
}

internal class TaaSettings {
    public bool Enabled = true;
    public float BlendFactor = 0.1f;
    public bool VarianceClip = true;
    public float Scale = 1f;
}

internal class SsaoSettings {
    public bool Enabled = true;
    public float Radius = 0.25f;
    public float Bias = 0.025f;
    public float Intensity = 1.0f;
}

internal class BloomSettings {
    public bool Enabled = true;
    public float Intensity = 0.1f;
}

internal class BlurSettings {
    public bool Enabled = false;
    public float Radius = 1.0f;
}

internal class GrayscaleSettings {
    public bool Enabled = false;
}

internal class PosterizationSettings {
    public bool Enabled = false;
    public float Levels = 10.0f;
}

internal class DreamVisionSettings {
    public bool Enabled = false;
}

internal class PixelizerSettings {
    public bool Enabled = false;
    public float Size = 4.0f;
}

internal class CrossHatchingSettings {
    public bool Enabled = false;
}

internal class CrossStitchingSettings {
    public bool Enabled = false;
    public float Size = 6.0f;
}

internal class PredatorSettings {
    public bool Enabled = false;
}

internal class SobelSettings {
    public bool Enabled = false;
}

internal class ScanlinesSettings {
    public bool Enabled = false;
}

internal class FisheyeSettings {
    public bool Enabled = false;
}