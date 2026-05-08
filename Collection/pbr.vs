#version 330

// vertex input
in vec3 vertex_pos;
in vec2 vertex_tex_pos;
in vec3 vertex_normal;
in vec4 vertex_tangent;
in vec4 vertex_color;

// uniform input
uniform mat4 mvp;
uniform mat4 matModel;
uniform mat4 matNormal;
uniform vec3 lightPos;
uniform vec4 difColor;

// fragment output
out vec3 frag_pos;
out vec2 frag_tex_pos;
out vec4 frag_color;
out vec3 frag_normal;
out vec4 frag_pos_light_space;
out mat3 TBN;

uniform mat4 lightVP;

const float normal_offset = 0.1;

void main() {
    
    // binormal from vertex normal and tangent
    vec3 vertexBinormal = cross(vertex_normal, vertex_tangent.xyz)*vertex_tangent.w;

    // fragment normal based on normal transformations
    mat3 normalMatrix = transpose(inverse(mat3(matModel)));

    // fragment position based on model transformations
    frag_pos = vec3(matModel*vec4(vertex_pos, 1.0));

    frag_tex_pos = vertex_tex_pos;
    frag_normal = normalize(normalMatrix*vertex_normal);
    vec3 fragTangent = normalize(normalMatrix*vertex_tangent.xyz);
    fragTangent = normalize(fragTangent - dot(fragTangent, frag_normal)*frag_normal);
    vec3 fragBinormal = normalize(normalMatrix*vertexBinormal);
    fragBinormal = cross(frag_normal, fragTangent);

    TBN = transpose(mat3(fragTangent, fragBinormal, frag_normal));
    frag_pos_light_space = lightVP * matModel * vec4(vertex_pos, 1.0);

    // final vertex position
    gl_Position = mvp*vec4(vertex_pos, 1.0);
}
