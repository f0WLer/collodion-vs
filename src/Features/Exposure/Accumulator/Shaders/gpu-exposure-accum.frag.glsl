// Accumulation pass: adds one new BGRA8 sample (optionally sRGB-linearised) to the running RGBA32F sum.
// Mirrors the per-frame accumulation stage of EmulsionProcessor.ApplyInPlace(), including the linearisation branch.
#version 330 core
in  vec2 v_uv;
out vec4 out_sum;

uniform sampler2D u_sample;   // RGBA8 - new frame blit from virtual camera
uniform sampler2D u_accum;    // RGBA32F - running channel sums
uniform bool      u_linearize;
uniform bool      u_log_accum; // true --> accumulate log(1+s·k) per frame for log-space integration
uniform float     u_dev_strength; // grain sensitivity - applied inside log so it shapes the curve

float srgbToLinear(float c) {
    return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4);
}

void main() {
    vec3 s    = texture(u_sample, v_uv).rgb;
    vec3 prev = texture(u_accum,  v_uv).rgb;
    if (u_linearize)
        s = vec3(srgbToLinear(s.r), srgbToLinear(s.g), srgbToLinear(s.b));
    if (u_log_accum)
        s = log(1.0 + s * u_dev_strength);  // k inside log: controls toe shape, not just output scale
    out_sum = vec4(prev + s, 1.0);
}
