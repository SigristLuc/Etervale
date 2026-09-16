#ifndef PRISTON_STATICDEPTH_FXH
#define PRISTON_STATICDEPTH_FXH

#include "PristonDepthCore.fxh"
#include "PristonVertexAnim.fxh"

float4x4 World;

struct VertexInput
{
    float4 Position : POSITION0;
    float3 Normal   : NORMAL0;
    float4 Color    : COLOR0;
    float2 TexCoord : TEXCOORD0;
};

struct PixelInput
{
    float4 Position : SV_POSITION;
#ifndef PRISTON_OPAQUE_DEPTH
    float2 TexCoord : TEXCOORD0;
    float2 Depth    : TEXCOORD1;
#else
    float2 Depth    : TEXCOORD0;
#endif
};

PixelInput VS(VertexInput input)
{
    PixelInput output;
    float4 worldPosition = mul(input.Position, World);
    worldPosition.xyz += VertexAnimOffset(worldPosition.xyz);
    output.Position = mul(mul(worldPosition, View), Projection);
#ifndef PRISTON_OPAQUE_DEPTH
    output.TexCoord = input.TexCoord + UvScrollOffset;
#endif
    // Interpolate homogeneous z/w components, then divide in the pixel shader.
    // Dividing here would perspective-correct an already divided depth a second time for lamps.
    output.Depth = output.Position.zw;
    return output;
}

#endif
