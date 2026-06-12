// Develop pass: converts the RGBA32F accumulated sums to a tone-mapped RGBA8 output.
// Mirrors EmulsionProcessor physics exactly, including weight normalisation and output normalisation.
#version 330 core
in  vec2 v_uv;
out vec4 out_color;

uniform sampler2D u_accum;
uniform float u_inv_ref;
uniform bool  u_spectral;
uniform bool  u_hd_curve;
uniform float u_red_sens;
uniform float u_green_sens;
uniform float u_blue_sens;
uniform float u_dev_strength;
uniform float u_gamma;
uniform float u_norm;         // output normalisation: full-correct-exposure maps to 1.0
uniform float u_inertia;      // E below this threshold --> zero density
uniform bool  u_log_accum;    // true --> E was accumulated in log-space (log1p per frame)
uniform float u_reciprocity;  // Schwarzschild reciprocity factor; <1.0 --> long exposure underperforms

float density(float E, float g) {
    E *= u_reciprocity;
    if (E < u_inertia) return 0.0;
    float d = u_log_accum
        ? max(E, 0.0)                                              // k already baked into accumulation
        : max(log(1.0 + E * u_dev_strength) / log(10.0), 0.0);   // linear path: k lives here
    return pow(d, g);
}

void main() {
    vec3 sum = texture(u_accum, v_uv).rgb;
    vec3 E   = sum * u_inv_ref;

    vec3 result;
    if (u_spectral) {
        float e = E.r * u_red_sens + E.g * u_green_sens + E.b * u_blue_sens;
        float v = u_hd_curve ? density(e, u_gamma) : e;
        result  = vec3(clamp(v * u_norm, 0.0, 1.0));
    } else {
        if (u_hd_curve) {
            // Collapse to Rec.601 luma first so the output is greyscale, matching EmulsionProcessor.
            float luma = 0.299 * E.r + 0.587 * E.g + 0.114 * E.b;
            result = vec3(clamp(density(luma, u_gamma) * u_norm, 0.0, 1.0));
        } else {
            result = clamp(E, 0.0, 1.0);
        }
    }
    out_color = vec4(result, 1.0);
}
