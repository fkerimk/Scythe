#version 330

in vec3 vertex_pos;
in vec2 vertex_tex_pos;

uniform mat4 mvp;

out vec2 frag_tex_pos;

void main() {
    frag_tex_pos = vertex_tex_pos;
    gl_Position = mvp * vec4(vertex_pos, 1.0);
}
