#version 330

in vec2 fragTexCoord;
in vec4 fragColor;

uniform sampler2D texture0;      // Scene Color
uniform vec2 renderSize;

out vec4 finalColor;

// SMAA-like Thresholds
#define SMAA_THRESHOLD 0.1
#define SMAA_MAX_SEARCH_STEPS 8
#define SMAA_LOCAL_CONTRAST_ADAPTATION_FACTOR 2.0

float GetLuma(vec3 color) {
    return dot(color, vec3(0.299, 0.587, 0.114));
}

void main() {
    vec2 texelSize = 1.0 / renderSize;
    vec3 colorM = texture(texture0, fragTexCoord).rgb;
    float lumaM = GetLuma(colorM);

    // 1. Edge Detection
    float lumaL = GetLuma(texture(texture0, fragTexCoord + vec2(-texelSize.x, 0.0)).rgb);
    float lumaR = GetLuma(texture(texture0, fragTexCoord + vec2(texelSize.x, 0.0)).rgb);
    float lumaU = GetLuma(texture(texture0, fragTexCoord + vec2(0.0, -texelSize.y)).rgb);
    float lumaD = GetLuma(texture(texture0, fragTexCoord + vec2(0.0, texelSize.y)).rgb);

    vec2 delta = abs(lumaM - vec2(lumaR, lumaD));
    vec2 edges = step(SMAA_THRESHOLD, delta);

    if (dot(edges, vec2(1.0, 1.0)) == 0.0) {
        finalColor = vec4(colorM, 1.0) * fragColor;
        return;
    }

    // 2. Simple Morphological Blending (SMAA-inspired search)
    // We look for the "length" of the edge to determine how much to blend
    vec2 blend = vec2(0.0);
    
    if (edges.x > 0.0) { // Horizontal edge
        float count = 1.0;
        for (int i = 1; i <= SMAA_MAX_SEARCH_STEPS; i++) {
            float l = GetLuma(texture(texture0, fragTexCoord + vec2(float(i) * texelSize.x, 0.0)).rgb);
            if (abs(l - lumaM) < SMAA_THRESHOLD) break;
            count += 1.0;
        }
        blend.x = count / float(SMAA_MAX_SEARCH_STEPS);
    }

    if (edges.y > 0.0) { // Vertical edge
        float count = 1.0;
        for (int i = 1; i <= SMAA_MAX_SEARCH_STEPS; i++) {
            float l = GetLuma(texture(texture0, fragTexCoord + vec2(0.0, float(i) * texelSize.y)).rgb);
            if (abs(l - lumaM) < SMAA_THRESHOLD) break;
            count += 1.0;
        }
        blend.y = count / float(SMAA_MAX_SEARCH_STEPS);
    }

    // 3. Neighborhood Blending
    vec3 outColor = colorM;
    if (blend.x > 0.0) {
        vec3 colorR = texture(texture0, fragTexCoord + vec2(texelSize.x, 0.0)).rgb;
        outColor = mix(outColor, colorR, blend.x * 0.5);
    }
    if (blend.y > 0.0) {
        vec3 colorD = texture(texture0, fragTexCoord + vec2(0.0, texelSize.y)).rgb;
        outColor = mix(outColor, colorD, blend.y * 0.5);
    }

    finalColor = vec4(outColor, 1.0) * fragColor;
}
