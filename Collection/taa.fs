#version 330

in vec2 fragTexCoord;
in vec4 fragColor;

out vec4 finalColor;

uniform sampler2D texture0;       // Current Frame (Jittered)
uniform sampler2D depthTexture;   // Depth
uniform sampler2D historyTexture; // Previous Frame (Accumulated)

uniform mat4 matViewProjInv;      // Inverse of Current Jittered ViewProj
uniform mat4 matPrevViewProj;     // Previous Jittered ViewProj
uniform vec2 jitter;              // Current Jitter

// Settings (could be uniforms)
uniform float blendFactor;
uniform int varianceClip;
uniform float scale;

// Helper to linearize depth if needed, but we reconstruct position directly
float GetDepth(vec2 uv) {
    return texture(depthTexture, uv).r;
}

void main() {
    
    // 1. Current Color
    vec3 color = texture(texture0, fragTexCoord).rgb;
    
    // 2. Reprojection
    float depth = texture(depthTexture, fragTexCoord).r;
    
    // Clip Space Position
    // Depth is 0..1 in OpenGL/Raylib usually? Raylib uses OpenGL backend.
    // If we assume standard GL depth:
    vec4 clipPos = vec4(fragTexCoord.x * 2.0 - 1.0, (fragTexCoord.y * 2.0 - 1.0), depth * 2.0 - 1.0, 1.0);
    
    // Reconstruct World Position
    vec4 worldPos = matViewProjInv * clipPos;
    worldPos /= worldPos.w;
    
    // Project to Previous Clip Space
    vec4 prevClip = matPrevViewProj * worldPos;
    prevClip /= prevClip.w;
    
    // Convert to UV
    vec2 prevUV = prevClip.xy * 0.5 + 0.5;
    
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
    
    for(int x = -1; x <= 1; x++) {
        for(int y = -1; y <= 1; y++) {
            vec3 s = textureOffset(texture0, fragTexCoord, ivec2(x, y)).rgb;
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
        vec3 v_unit = v_clip.xyz / e_clip;
        vec3 a_unit = abs(v_unit);
        float ma_unit = max(a_unit.x, max(a_unit.y, a_unit.z));
        
        if (ma_unit > 1.0) {
            history = p_clip + v_clip / ma_unit;
        }
        
    } else {
        // Fallback to simple min/max
        history = clamp(history, minColor, maxColor);
    }
    
    // 5. Blend
    // Dynamic blend factor? 
    // If valid history, blend.
    vec3 result = mix(history, color, blendFactor);
    
    finalColor = vec4(result, 1.0);
}
