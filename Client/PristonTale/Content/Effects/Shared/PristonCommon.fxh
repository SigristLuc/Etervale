#ifndef PRISTON_COMMON_FXH
#define PRISTON_COMMON_FXH

float4x4 View;
float4x4 Projection;
float3 AmbientColor;
float3 SunDirection;
float3 SunColor;
float SelfIllum;
float LampFactor;
float2 UvScrollOffset;
// MaterialBlink gates the global [0.35, 1] brightness pulse.
float BlinkPulse;
float MaterialBlink;
texture Texture;

// MULTIMIX reuses UV0: mode 0 disables slot1, 1 adds it, and 2 modulates it.
// Slot1 keeps an independent scroll offset.
float SecondTextureMode;
float2 UvScrollOffset2;
texture Texture2;

float3 ApplyBlink(float3 color)
{
    return color * lerp(1.0f, BlinkPulse, MaterialBlink);
}

float3 NormalizeSurface(float3 value)
{
    // Opposing interpolated normals or degenerate legacy transforms may yield zero.
    // Preserve ambient light instead of allowing normalize(0) to propagate NaNs through the pixel.
    return value * rsqrt(max(dot(value, value), 1.0e-12f));
}

sampler TextureSampler = sampler_state
{
    Texture = <Texture>;
    AddressU = Wrap; AddressV = Wrap;
    MinFilter = Anisotropic; MagFilter = Anisotropic; MipFilter = Linear; MaxAnisotropy = 16;
};

sampler TextureSampler2 = sampler_state
{
    Texture = <Texture2>;
    AddressU = Wrap; AddressV = Wrap;
    MinFilter = Anisotropic; MagFilter = Anisotropic; MipFilter = Linear; MaxAnisotropy = 16;
};

float3 ApplyMultiMix(float3 baseColor, float2 baseUv0)
{
    if (SecondTextureMode < 0.5f)
        return baseColor;

    float3 second = tex2D(TextureSampler2, baseUv0 + UvScrollOffset2).rgb;
    return SecondTextureMode > 1.5f ? baseColor * second : baseColor + second;
}

#include "PristonShadow.fxh"
#include "PristonPointLights.fxh"

#endif
