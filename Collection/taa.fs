#version 330

in vec2 fragTexCoord;
in vec4 fragColor;

out vec4 finalColor;

uniform sampler2D texture0;       // Current Frame (Jittered)
uniform sampler2D depthTexture;   // Depth
uniform sampler2D historyTexture; // Previous Frame (Accumulated)
uniform sampler2D velocityTexture; // Screen-space currentUV - previousUV

uniform mat4 matViewProjInv;      // Inverse of Current Jittered ViewProj
uniform mat4 matPrevViewProj;     // Previous Jittered ViewProj
uniform vec2 jitter;              // Current Jitter

// Settings (could be uniforms)
uniform float blendFactor;
uniform int varianceClip;
uniform float scale;
uniform int hasHistory;

// Helper to linearize depth if needed, but we reconstruct position directly
float GetDepth(vec2 uv) {
    return texture(depthTexture, uv).r;
}

void main() {

    // 1. Current Color
    vec3 color = texture(texture0, fragTexCoord).rgb;

    if (hasHistory == 0) {
        finalColor = vec4(color, 1.0);
        return;
    }

    // 2. Motion-vector reprojection
    float depth = texture(depthTexture, fragTexCoord).r;

    if (depth >= 0.9999) {
        finalColor = vec4(color, 1.0);
        return;
    }

    vec4 velocitySample = texture(velocityTexture, fragTexCoord);

    if (velocitySample.a < 0.5) {
        finalColor = vec4(color, 1.0);
        return;
    }

    vec2 velocity = velocitySample.xy * 2.0 - 1.0;
    vec2 prevUV = fragTexCoord - velocity;
    
    // 3. Sample History
    // Validate UV to avoid ghosting from outside screen
    if (prevUV.x < 0.0 || prevUV.x > 1.0 || prevUV.y < 0.0 || prevUV.y > 1.0) {
        finalColor = vec4(color, 1.0);
        return;
    }
    
    vec3 history = texture(historyTexture, prevUV).rgb;
    
    // 4. Neighborhood Clamping (To fix ghosting)
    // 4. Neighborhood Sampling & Clamping
    vec3 minColor = vec3(100.0);
    vec3 maxColor = vec3(-100.0);
    
    vec3 m1 = vec3(0.0);
    vec3 m2 = vec3(0.0);
    
    vec2 texelSize = 1.0 / vec2(textureSize(texture0, 0));

    for(int x = -1; x <= 1; x++) {
        for(int y = -1; y <= 1; y++) {
            vec2 sampleUv = clamp(fragTexCoord + vec2(float(x), float(y)) * texelSize, vec2(0.0), vec2(1.0));
            vec3 s = texture(texture0, sampleUv).rgb;
            minColor = min(minColor, s);
            maxColor = max(maxColor, s);
            
            m1 += s;
            m2 += s * s;
        }
    }
    
    // Variance Clipping logic
    if (varianceClip > 0) {
        
        vec3 mu = m1 / 9.0;
        vec3 sigma = sqrt(abs(m2 / 9.0 - mu * mu));
        
        minColor = mu - scale * sigma;
        maxColor = mu + scale * sigma;
        
        // AABB Clipping (better than hard clamping for color consistency)
        // Intersect the line from history to color with the AABB
        vec3 p_clip = 0.5 * (maxColor + minColor);
        vec3 e_clip = 0.5 * (maxColor - minColor);
        
        vec3 v_clip = history - p_clip;
        vec3 v_unit = v_clip.xyz / max(e_clip, vec3(0.0001));
        vec3 a_unit = abs(v_unit);
        float ma_unit = max(a_unit.x, max(a_unit.y, a_unit.z));
        
        if (ma_unit > 1.0) {
            history = p_clip + v_clip / ma_unit;
        }
        
    } else {
        // Fallback to simple min/max
        history = clamp(history, minColor, maxColor);
    }
    
    // 5. Blend. Fast pixels get less history so disocclusion trails decay quickly.
    float velocityLen = length(velocity * vec2(textureSize(texture0, 0)));
    float motionBlend = clamp(blendFactor + velocityLen * 0.015, blendFactor, 0.65);
    vec3 result = mix(history, color, motionBlend);
    
    finalColor = vec4(result, 1.0);
}
