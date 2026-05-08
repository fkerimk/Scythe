#version 330

in vec3 fragPosition;
in vec3 fragNormal;
in vec2 fragTexCoord;
in vec4 fragColor;

out vec4 finalColor;

void main() {

    gl_FragDepth = 0.0;
    finalColor = fragColor;
}