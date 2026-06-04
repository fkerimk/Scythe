#version 330

in vec2 frag_tex_pos;
in vec4 current_clip;
in vec4 previous_clip;
flat in int frag_has_history;

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

    if (frag_has_history == 0 || abs(current_clip.w) < 0.00001 || abs(previous_clip.w) < 0.00001) {
        finalColor = vec4(0.5, 0.5, 0.0, 0.0);
        return;
    }

    vec2 current_uv = current_clip.xy / current_clip.w * 0.5 + 0.5;
    vec2 previous_uv = previous_clip.xy / previous_clip.w * 0.5 + 0.5;
    vec2 velocity = clamp(current_uv - previous_uv, vec2(-1.0), vec2(1.0));

    finalColor = vec4(velocity * 0.5 + 0.5, 0.0, 1.0);
}
