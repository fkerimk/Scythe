#version 330

in vec3 fragPosition;
in vec4 fragColor;

out vec4 finalColor;

uniform vec3 cameraPos;
uniform float fadeRadius;

void main() {
    float distance = length(fragPosition - cameraPos);
    float alpha = 1.0 - smoothstep(fadeRadius * 0.5, fadeRadius, distance);
    
    if (alpha <= 0.0) discard;
    
    finalColor = vec4(fragColor.rgb, fragColor.a * alpha);
}
