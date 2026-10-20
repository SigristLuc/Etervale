// Shared constants and helpers of the SM 6.0 library (Content/Shaders, README.md there). Conventions of every shader:
//  - world space is Y-up, in Priston units; matrices are row-vector XNA matrices (mul(v, M));
//  - colours are linear Rec.709. Scene passes write pre-exposed HDR radiance into the HalfVector4 scene target and never tonemap:
//    exposure, the display transform and the sRGB encoding happen once, in Passes/PostFinal.fx;
//  - light colours are given as the radiance of a white Lambertian surface facing the light (illuminance / pi), so a
//    lit diffuse surface is albedo * colour * n.l and the specular lobe carries the matching factor of pi;
//  - each effect has exactly one cbuffer per stage (MonoGame's Vulkan effects), and every texture and sampler has an
//    explicit register, the sampler on the same index as its texture.
#ifndef CORE_COMMON_HLSLI
#define CORE_COMMON_HLSLI

static const float PI = 3.14159265358979;

// Largest finite half float, and the cap HDR outputs stay below so blending never produces infinities.
static const float HdrCap = 60000.0;

static const float3 LuminanceWeights = float3(0.2126, 0.7152, 0.0722);

float Luminance(float3 c)
{
    return dot(c, LuminanceWeights);
}

// Opposing interpolated normals and degenerate legacy transforms can produce a zero vector; keep it finite.
float3 SafeNormalize(float3 v)
{
    return v * rsqrt(max(dot(v, v), 1.0e-12));
}

#endif
