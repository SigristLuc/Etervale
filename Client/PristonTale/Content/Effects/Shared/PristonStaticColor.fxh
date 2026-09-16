#ifndef PRISTON_STATICCOLOR_FXH
#define PRISTON_STATICCOLOR_FXH

#include "PristonCommon.fxh"
#include "PristonLampShadow.fxh"
#include "PristonVertexAnim.fxh"

float4x4 World;
float DrawOpacity;

struct VertexInput
{
    float4 Position  : POSITION0;
    float3 Normal    : NORMAL0;
    float4 Color     : COLOR0;
    float2 TexCoord  : TEXCOORD0;
    float4 LampColor : COLOR1;
};

struct PixelInput
{
    float4 Position      : SV_POSITION;
    float3 Normal        : TEXCOORD0;
    float4 Color         : COLOR0;
    float2 TexCoord      : TEXCOORD1;
    float3 WorldPosition : TEXCOORD2;
    float4 LampColor     : COLOR1;
};

PixelInput VS(VertexInput input)
{
    PixelInput output;
    float4 worldPosition = mul(input.Position, World);
    worldPosition.xyz += VertexAnimOffset(worldPosition.xyz);
    output.Position = mul(mul(worldPosition, View), Projection);
    output.Normal = NormalizeSurface(mul(input.Normal, (float3x3)World));
    output.Color = input.Color;
    output.Color.a *= DrawOpacity;
    output.TexCoord = input.TexCoord + UvScrollOffset;
    output.WorldPosition = worldPosition.xyz;
    output.LampColor = input.LampColor;
    return output;
}

float3 ShadeStatic(PixelInput input)
{
    [branch]
    if (SelfIllum >= 1.0f)
    {
        return ApplyBlink(float3(1.0f, 1.0f, 1.0f));
    }

    float3 normal = NormalizeSurface(input.Normal);
    float ndotl = saturate(dot(normal, -SunDirection));
    float shadow = CalculateShadow(input.WorldPosition, normal, ndotl);
    float3 lighting = AmbientColor + SunColor * ndotl * shadow + AccumulatePointLights(input.WorldPosition, normal);
    float3 bakedLamp = input.LampColor.rgb * LampFactor;
    // Unlit stage vertices are exactly black. Avoid walking eight lamp frusta and their atlas paths
    // when multiplying the result by this zero contribution cannot change the pixel.
    [branch]
    if (any(bakedLamp != 0.0f))
    {
        lighting += bakedLamp * LampShadow(input.WorldPosition);
    }

    return ApplyBlink(lerp(lighting, float3(1.0f, 1.0f, 1.0f), SelfIllum));
}

#endif
