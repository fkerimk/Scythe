#version 330
in vec2 fragTexCoord;
in vec4 fragColor;
out vec4 finalColor;

uniform sampler2D texture0;
uniform vec2 textureSize;
uniform float outlineSize;
uniform vec4 outlineColor;

void main()
{
    // Read alpha from the mask texture
    vec4 center = texture(texture0, fragTexCoord);
    
    // If we are strictly inside the object (alpha > 0), typically we don't draw the outline *over* it,
    // unless we want a specific effect. 
    // Standard "Unity Selection" outline allows the object to be seen, and draws outline around it.
    if (center.a > 0.0) {
        // Check if we want to show the underlying object. 
        // We output transparent so the main render (behind this layer) shows through.
        finalColor = vec4(0.0);
        return;
    }
    
    vec2 texelSize = 1.0 / textureSize;
    float alphaMax = 0.0;
    
    // Check neighbors to detect edge
    // We check a simple pattern. For wider outlines, we might need more samples.
    // Using a loop for flexibility.
    
    float steps = ceil(outlineSize);
    
    // Optimization: Check 8 points at 'outlineSize' distance?
    // Or scan area. Scanning area is safer for avoiding gaps.
    
    for (float x = -steps; x <= steps; x++) {
        for (float y = -steps; y <= steps; y++) {
             // skip center
             if (x == 0.0 && y == 0.0) continue;
             
             // Check if dist is within circle (optional, for rounded corners)
             if (length(vec2(x, y)) > steps) continue;
             
             float a = texture(texture0, fragTexCoord + vec2(x, y) * texelSize).a;
             alphaMax = max(alphaMax, a);
        }
    }
    
    if (alphaMax > 0.0) {
        finalColor = outlineColor;
    } else {
        finalColor = vec4(0.0);
    }
}
