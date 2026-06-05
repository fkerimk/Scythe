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

vec3 RGBToYCoCg(vec3 c) {
    return vec3(
        c.r * 0.25 + c.g * 0.5 + c.b * 0.25,
        c.r * 0.5 - c.b * 0.5,
        -c.r * 0.25 + c.g * 0.5 - c.b * 0.25
    );
}

vec3 YCoCgToRGB(vec3 c) {
    return vec3(
        c.x + c.y - c.z,
        c.x + c.z,
        c.x - c.y - c.z
    );
}

vec3 ClipAABB(vec3 history, vec3 minColor, vec3 maxColor) {
    vec3 center = 0.5 * (maxColor + minColor);
    vec3 extent = 0.5 * (maxColor - minColor) + vec3(0.0001);
    vec3 offset = history - center;
    vec3 unit = abs(offset / extent);
    float maxUnit = max(unit.x, max(unit.y, unit.z));

    if (maxUnit > 1.0) {
        return center + offset / maxUnit;
    }

    return history;
}

vec3 ResolveCurrent(vec2 uv, vec2 texelSize) {
    vec3 center = texture(texture0, uv).rgb;
    vec3 cross =
        texture(texture0, uv + vec2(texelSize.x, 0.0)).rgb +
        texture(texture0, uv - vec2(texelSize.x, 0.0)).rgb +
        texture(texture0, uv + vec2(0.0, texelSize.y)).rgb +
        texture(texture0, uv - vec2(0.0, texelSize.y)).rgb;

    return center * 0.6 + cross * 0.1;
}

void main() {
    vec2 texelSize = 1.0 / vec2(textureSize(texture0, 0));

    // 1. Current Color
    vec3 color = ResolveCurrent(fragTexCoord, texelSize);

    if (hasHistory == 0) {
        finalColor = vec4(color, 1.0);
        return;
    }

    // 2. Motion-vector reprojection
    float depth = texture(depthTexture, fragTexCoord).r;

    if (depth >= 0.999999) {
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
	    
    // 4. Neighborhood sampling and clipping.
    vec3 minColor = vec3(100.0);
    vec3 maxColor = vec3(-100.0);
	    
    vec3 m1 = vec3(0.0);
    vec3 m2 = vec3(0.0);
	
    for(int x = -1; x <= 1; x++) {
        for(int y = -1; y <= 1; y++) {
            vec2 sampleUv = clamp(fragTexCoord + vec2(float(x), float(y)) * texelSize, vec2(0.0), vec2(1.0));
            vec3 s = RGBToYCoCg(texture(texture0, sampleUv).rgb);
            minColor = min(minColor, s);
            maxColor = max(maxColor, s);
	            
            m1 += s;
            m2 += s * s;
        }
    }
    
    history = RGBToYCoCg(history);
    color = RGBToYCoCg(color);

    if (varianceClip > 0) {
	        
        vec3 mu = m1 / 9.0;
        vec3 sigma = sqrt(abs(m2 / 9.0 - mu * mu));
	        
        float clipScale = max(scale, 1.25);
        minColor = max(minColor, mu - clipScale * sigma);
        maxColor = min(maxColor, mu + clipScale * sigma);
        history = ClipAABB(history, minColor, maxColor);
    } else {
        history = clamp(history, minColor, maxColor);
    }
	    
    // 5. Blend. Fast pixels get less history so disocclusion trails decay quickly.
    float velocityLen = length(velocity * vec2(textureSize(texture0, 0)));
    float motionBlend = clamp(blendFactor + velocityLen * 0.015, blendFactor, 0.65);
    vec3 result = YCoCgToRGB(mix(history, color, motionBlend));
	    
    finalColor = vec4(clamp(result, 0.0, 1.0), 1.0);
}
