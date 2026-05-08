#version 330

in vec2 fragTexCoord;
in vec4 fragColor;

uniform sampler2D texture0;
uniform sampler2D depthTexture;

uniform mat4 matProjection;
uniform mat4 matProjectionInverse;

uniform float ssaoRadius = 0.5;
uniform float ssaoBias = 0.025;
uniform float ssaoIntensity = 2.0;
uniform vec2 renderSize;

out vec4 finalColor;

// Interleaved Gradient Noise (VERY stable for jittering)
float IGN(vec2 p) {
    return fract(52.9829189 * fract(dot(p, vec2(0.06711056, 0.00583715))));
}

vec3 GetViewPos(vec2 uv) {
    float depth = texture(depthTexture, uv).r;
    vec4 ndc = vec4(uv * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
    vec4 viewPos = matProjectionInverse * ndc;
    return viewPos.xyz / viewPos.w;
}

// Reconstruct normal from depth to avoid flat surface occlusion
vec3 GetNormal(vec2 uv, vec3 p) {
    vec2 off = 1.0 / renderSize;
    vec3 pR = GetViewPos(uv + vec2(off.x, 0.0));
    vec3 pU = GetViewPos(uv + vec2(0.0, off.y));
    return normalize(cross(pR - p, pU - p));
}

void main() {
    vec4 color = texture(texture0, fragTexCoord);
    float depth = texture(depthTexture, fragTexCoord).r;
    
    if (depth >= 0.999) {
        finalColor = color * fragColor;
        return;
    }

    vec3 viewPos = GetViewPos(fragTexCoord);
    vec3 normal = GetNormal(fragTexCoord, viewPos);
    
    float noise = IGN(gl_FragCoord.xy);
    float occlusion = 0.0;
    
    const int samples = 24;
    float screenRadius = ssaoRadius / max(0.1, abs(viewPos.z));

    for (int i = 0; i < samples; i++) {
        // Spiral with jitter
        float angle = (float(i) + noise) * 2.39996; // Golden angle
        float dist = float(i) / float(samples);
        
        vec2 offsetUV = vec2(cos(angle), sin(angle)) * dist * screenRadius;
        vec2 sampleUV = fragTexCoord + offsetUV;
        
        if (sampleUV.x < 0.0 || sampleUV.x > 1.0 || sampleUV.y < 0.0 || sampleUV.y > 1.0) continue;

        vec3 samplePos = GetViewPos(sampleUV);
        vec3 v = samplePos - viewPos;
        
        // Simple SSAO comparison
        float d = length(v);
        float occ = max(0.0, dot(normal, v) - ssaoBias) / (d + 0.01);
        
        // Falloff
        float rangeCheck = smoothstep(0.0, 1.0, ssaoRadius / (abs(viewPos.z - samplePos.z) + 0.01));
        occlusion += occ * rangeCheck;
    }

    occlusion = 1.0 - (occlusion / float(samples)) * ssaoIntensity;
    occlusion = clamp(occlusion, 0.0, 1.0);
    
    // Very basic 2x2 local blur to reduce grain without a separate pass
    // This samples neighbors and averages to soften the jitter
    // (Disabled for now to see the raw improvement of Golden Angle + Normals)
    
    finalColor = vec4(color.rgb * occlusion, color.a) * fragColor;
}
