#ifndef PRISTON_SKINNEDDEPTH_FXH
#define PRISTON_SKINNEDDEPTH_FXH

#include "PristonDepthCore.fxh"
#include "PristonSkinning.fxh"

struct VertexInput
{
    float4 Position  : POSITION0;
    float3 Normal    : NORMAL0;
    float4 Color     : COLOR0;
    float2 TexCoord  : TEXCOORD0;
    float  BoneIndex : BLENDINDICES0;
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
    int bone = (int)input.BoneIndex;
    float4 worldPosition = float4(SkinPosition(input.Position, bone), 1.0f);
    output.Position = mul(mul(worldPosition, View), Projection);
#ifndef PRISTON_OPAQUE_DEPTH
    output.TexCoord = input.TexCoord + UvScrollOffset;
#endif
    output.Depth = output.Position.zw;
    return output;
}

#endif
