#ifndef PRISTON_SKINNEDCOLOR_FXH
#define PRISTON_SKINNEDCOLOR_FXH

#include "PristonCommon.fxh"
#include "PristonSkinning.fxh"

// Skinned shaders omit the lamp-shadow atlas to stay within the vs_3_0 register budget.

// FS_REFLEX replaces authored UVs with a view-reflection sphere map.
float3 CameraPosition;
float MaterialReflex;
float3 ActorColorAdd;

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
    float4 Position      : SV_POSITION;
    float3 Normal        : TEXCOORD0;
    float4 Color         : COLOR0;
    float2 TexCoord      : TEXCOORD1;
    float3 WorldPosition : TEXCOORD2;
};

PixelInput VS(VertexInput input)
{
    PixelInput output;
    // Dynamic indexing of a constant array in vs_3_0: cast float to int. The compiler emits an a0
    // (address register) indexed load, which is legal relative addressing of constant registers.
    int bone = (int)input.BoneIndex;
    float3 worldPos3 = SkinPosition(input.Position, bone);
    float4 worldPosition = float4(worldPos3, 1.0f);
    output.Position = mul(mul(worldPosition, View), Projection);
    float3 worldNormal = NormalizeSurface(SkinNormal(input.Normal, bone));
    output.Normal = worldNormal;
    output.Color = input.Color;
    output.TexCoord = input.TexCoord + UvScrollOffset;
    [branch]
    if (MaterialReflex != 0.0f)
    {
        float3 eye = NormalizeSurface(worldPosition.xyz - CameraPosition);
        float2 reflexUV = reflect(eye, worldNormal).xy * float2(0.5f, -0.5f) + 0.5f;
        output.TexCoord = lerp(output.TexCoord, reflexUV, MaterialReflex);
    }
    output.WorldPosition = worldPosition.xyz;
    return output;
}

// Vertex color is baked lamp light, not a texture tint.
float3 ShadeSkinned(PixelInput input)
{
    [branch]
    if (SelfIllum >= 1.0f)
    {
        return ApplyBlink(float3(1.0f, 1.0f, 1.0f));
    }

    float3 normal = NormalizeSurface(input.Normal);
    float ndotl = saturate(dot(normal, -SunDirection));
    float shadow = CalculateShadow(input.WorldPosition, normal, ndotl);
    return ApplyBlink(AmbientColor + SunColor * ndotl * shadow
         + AccumulatePointLights(input.WorldPosition, normal)
         + input.Color.rgb * LampFactor);
}

#endif
