#version 330

in vec3 vertex_pos;
in vec2 vertex_tex_pos;

uniform mat4 mvp;
uniform mat4 matPrevModel;
uniform mat4 matPrevViewProj;
uniform int hasHistory;

out vec2 frag_tex_pos;
out vec4 current_clip;
out vec4 previous_clip;
flat out int frag_has_history;

void main() {
    frag_tex_pos = vertex_tex_pos;
    current_clip = mvp * vec4(vertex_pos, 1.0);
    previous_clip = matPrevViewProj * matPrevModel * vec4(vertex_pos, 1.0);
    frag_has_history = hasHistory;
    gl_Position = current_clip;
}
