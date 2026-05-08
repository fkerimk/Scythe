#version 330

#define max_lights 100
#define pi 3.14159265358979323846

// light struct
struct light {
    int enabled;
    int type;
    vec3 position;
    vec3 target;
    vec4 color;
    float intensity;
};

uniform light lights[max_lights];

// fragment input (from vs)
in vec3 frag_pos;
in vec2 frag_tex_pos;
in vec4 frag_color;
in vec3 frag_normal;
in vec4 frag_pos_light_space;
in mat3 TBN;

// final output
out vec4 final_color;

// uniform input
uniform int light_count;
uniform sampler2D albedo_map;
uniform vec4 albedo_color;
uniform sampler2D metallic_map;
uniform float metallic_value;
uniform sampler2D roughness_map;
uniform float roughness_value;
uniform sampler2D occlusion_map;
uniform float aoValue;
uniform sampler2D normal_map;
uniform float normalValue;
uniform int is_directx_normal;
uniform sampler2D emissive_map; // r:geight g:emissive
uniform vec4  emissive_color;
uniform float emissive_intensity;
uniform vec3 view_pos;
uniform sampler2D shadowMap;
uniform int shadow_light_index;
uniform float shadow_strength;
uniform float shadow_bias;
uniform int shadow_map_resolution;

uniform vec2 tiling;
uniform vec2 offset;

uniform int use_tex_albedo;
uniform int use_tex_normal;
uniform int use_tex_metallic;
uniform int use_tex_roughness;
uniform int use_tex_occlusion;
uniform int use_tex_emissive;
uniform vec3 ambient_color;
uniform float ambient_intensity;
uniform float alpha_cutoff;
uniform int receive_shadows;

float compute_shadow(vec4 shadowPos, vec3 n, vec3 l) {
    
    vec3 projCoords = shadowPos.xyz / shadowPos.w;
    projCoords = projCoords * 0.5 + 0.5;
    
    if (projCoords.z > 1.0) return 1.0;
    
    // Tighter bias for better definition
    float bias = max(shadow_bias * (1.0 - dot(n, l)), shadow_bias * 0.1);
    float shadow = 0.0;
    vec2 texelSize = 1.0 / textureSize(shadowMap, 0);
    
    // Higher quality 5x5 PCF with offset weights
    for(int x = -2; x <= 2; ++x) {
        for(int y = -2; y <= 2; ++y) {
            float pcfDepth = texture(shadowMap, projCoords.xy + vec2(x, y) * texelSize).r; 
            shadow += (projCoords.z - bias) > pcfDepth  ? 1.0 : 0.0;        
        }    
    }
    
    shadow /= 25.0;
    return 1.0 - (shadow * shadow_strength);
}

// reflectivity in 0.0 to 1.0
vec3 schlick_fresnel(float h_dot_v, vec3 refl) {

    return refl + (1.0 - refl) * pow(1.0 - h_dot_v, 5.0);
}

float ggx_distribution(float n_dot_h, float roughness) {

    float a = roughness * roughness * roughness * roughness;
    float d = n_dot_h * n_dot_h * (a - 1.0) + 1.0;

    d = pi * d * d;

    return (a / max(d, 0.0000001));
}

float geom_smith(float n_dot_v, float n_dot_l, float roughness) {

    float r = roughness + 1.0;
    float k = r * r / 8.0;
    float ik = 1.0 - k;

    float ggx1 = n_dot_v / (n_dot_v * ik + k);
    float ggx2 = n_dot_l / (n_dot_l * ik + k);

    return ggx1 * ggx2;
}

vec4 compute_pbr() {

    vec4 albedo = texture(albedo_map, vec2(frag_tex_pos.x * tiling.x + offset.x, frag_tex_pos.y * tiling.y + offset.y));
    albedo = vec4(pow(albedo.rgb, vec3(2.2)), albedo.a);
    albedo *= vec4(pow(albedo_color.rgb, vec3(2.2)), albedo_color.a);
    
    float metallic = clamp(metallic_value, 0.0, 1.0);
    float roughness = clamp(roughness_value, 0.04, 1.0);
    float ao = clamp(aoValue, 0.0, 1.0);
    
    if (use_tex_metallic == 1) metallic = clamp(texture(metallic_map, vec2(frag_tex_pos.x * tiling.x + offset.x, frag_tex_pos.y * tiling.y + offset.y)).r + metallic_value, 0.0, 1.0);
    if (use_tex_roughness == 1) roughness = clamp(texture(roughness_map, vec2(frag_tex_pos.x * tiling.x + offset.x, frag_tex_pos.y * tiling.y + offset.y)).r + roughness_value, 0.04, 1.0);
    if (use_tex_occlusion == 1) ao = clamp(texture(occlusion_map, vec2(frag_tex_pos.x * tiling.x + offset.x, frag_tex_pos.y * tiling.y + offset.y)).r + aoValue - 1.0, 0.0, 1.0);

    vec3 n = normalize(frag_normal);

    if (use_tex_normal == 1) {

        vec3 normalSample = texture(normal_map, vec2(frag_tex_pos.x * tiling.x + offset.x, frag_tex_pos.y * tiling.y + offset.y)).rgb;
        if (is_directx_normal == 1) normalSample.g = 1.0 - normalSample.g;
        normalSample = normalize(normalSample * 2.0 - 1.0);
        normalSample = mix(vec3(0.0, 0.0, 1.0), normalSample, normalValue);
        n = normalize(normalSample * TBN);
    }

    vec3 v = normalize(view_pos - frag_pos);

    vec3 emissive = vec3(0);

    if (use_tex_emissive == 1) {

        emissive = texture(emissive_map, vec2(frag_tex_pos.x * tiling.x + offset.x, frag_tex_pos.y * tiling.y + offset.y)).g * emissive_color.rgb * emissive_intensity;
    }

    vec3 base_refl = mix(vec3(0.04), albedo.rgb, metallic);
    vec3 light_accum = vec3(0.0);

    for (int i = 0; i < light_count; i++) {

        if (lights[i].enabled == 0) continue;

        vec3 l = vec3(0.0);
        float attenuation = 1.0;
        float range_attenuation = 1.0;

        // light direction calculation
        if (lights[i].type == 0) {

            l = normalize(lights[i].position - lights[i].target);
            attenuation = 1.0;
            range_attenuation = 1.0;

        } else if (lights[i].type == 1) {

            vec3 toLight = lights[i].position - frag_pos;
            float dist = length(toLight);
            l = normalize(toLight);
            
            // range: target - position
            float range = length(lights[i].target - lights[i].position);
            range_attenuation = smoothstep(range, 0.0, dist);
            
            attenuation = range_attenuation / (1.0 + dist * dist * 0.05);

        } else if (lights[i].type == 2) {

            vec3 toLight = lights[i].position - frag_pos;
            float dist = length(toLight);

            l = normalize(toLight);
            
            // range: target - position
            float range = length(lights[i].target - lights[i].position);
            range_attenuation = smoothstep(range, 0.0, dist);
            
            vec3 spot_dir = normalize(lights[i].target - lights[i].position);
            float cos_angle = dot(-l, spot_dir);
            float spot_effect = smoothstep(0.0, 1.0, (cos_angle - 0.5) / max(0.5, 1.0 - 0.5));
            
            attenuation = range_attenuation * spot_effect / (1.0 + dist * dist * 0.05);
        }

        vec3 h = normalize(v + l);

        float n_dot_v = max(dot(n, v), 0.0000001);
        float n_dot_l = max(dot(n, l), 0.0);
        float h_dot_v = max(dot(h, v), 0.0);
        float n_dot_h = max(dot(n, h), 0.0);

        if (n_dot_l <= 0.0) continue;

        float D = ggx_distribution(n_dot_h, roughness);
        float G = geom_smith(n_dot_v, n_dot_l, roughness);
        vec3 F = schlick_fresnel(h_dot_v, base_refl);

        vec3 kspec = (D * G * F) / max(4.0 * n_dot_v * n_dot_l, 0.001);
        vec3 kdiff = (vec3(1.0) - F) * (1.0 - metallic);

        float shadow = 1.0;
        if (receive_shadows == 1 && i == shadow_light_index) {
            shadow = compute_shadow(frag_pos_light_space, n, l);
        }

        vec3 radiance = pow(lights[i].color.rgb, vec3(2.2)) * lights[i].intensity * attenuation * shadow;
        light_accum += (kdiff * albedo.rgb / pi + kspec) * radiance * n_dot_l;
    }
    
    vec3 ambientLight = pow(ambient_color, vec3(2.2)) * ambient_intensity;
    
    return vec4(light_accum + albedo.rgb * ambientLight * ao + emissive, albedo.a);
}

void main() {

    vec4 finalPbr = compute_pbr();
    
    if (finalPbr.a < alpha_cutoff) discard;
    
    vec3 color = finalPbr.rgb;

    // hdr tonemapping
    color = color / (color + vec3(1.0));
    
    // gamma correction
    color = pow(color, vec3(1.0 / 2.2));

    final_color = vec4(color, finalPbr.a);
}