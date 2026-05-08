#version 330

in vec2 fragTexCoord;
in vec4 fragColor;

uniform sampler2D texture0;
uniform float radius = 1.0;
uniform vec2 resolution;

out vec4 finalColor;

void main()
{
    vec2 size = 1.0 / resolution;
    vec4 sum = vec4(0.0);
    
    // Simple 9-tap box blur for adjustable radius
    for (float x = -2.0; x <= 2.0; x++)
    {
        for (float y = -2.0; y <= 2.0; y++)
        {
            sum += texture(texture0, fragTexCoord + vec2(x, y) * size * radius);
        }
    }

    finalColor = (sum / 25.0) * fragColor;
}