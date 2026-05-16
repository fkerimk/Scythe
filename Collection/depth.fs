#version 330

in vec2 frag_tex_pos;

out vec4 finalColor;

uniform sampler2D albedo_map;
uniform vec4 albedo_color;
uniform vec2 tiling;
uniform vec2 offset;
uniform float alpha_cutoff;
uniform int use_tex_albedo;

void main() {
    vec2 uv = vec2(frag_tex_pos.x * tiling.x + offset.x, frag_tex_pos.y * tiling.y + offset.y);
    float alpha = (use_tex_albedo == 1 ? texture(albedo_map, uv).a : 1.0) * albedo_color.a;
    float effective_alpha_cutoff = max(alpha_cutoff, 1.0 / 255.0);

    if (alpha < effective_alpha_cutoff) discard;

    finalColor = vec4(1.0);
}
