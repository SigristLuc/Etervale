#ifndef PRISTON_LAMPSHADOW_FXH
#define PRISTON_LAMPSHADOW_FXH

// Static-only atlas for nearby lamp shadows; skinned shaders reserve those registers for bones.
#define MAX_LAMP_SHADOWS 8
int LampShadowCount;
float2 LampShadowTexel;                 // one-texel offset in atlas UV space (for PCF)
float4x4 LampShadowVP[MAX_LAMP_SHADOWS];
float4 LampShadowPos[MAX_LAMP_SHADOWS]; // xyz = lamp world pos, w = radius
float4 LampShadowTile[MAX_LAMP_SHADOWS];// xy = atlas UV offset, zw = atlas UV scale
texture LampShadowAtlas;

sampler LampShadowSampler = sampler_state
{
    Texture = <LampShadowAtlas>; AddressU = Clamp; AddressV = Clamp;
    MinFilter = Point; MagFilter = Point; MipFilter = None;
};

// Distance and tile-border fades hide the square edge of each lamp frustum.
float SampleLampShadowTile(float4x4 vp, float4 lampPosRadius, float4 tile, float3 worldPos)
{
    // Outside the lamp radius influence is exactly zero; avoid matrix work and four atlas reads.
    float3 toLamp = lampPosRadius.xyz - worldPos;
    float distanceSquared = dot(toLamp, toLamp);
    [branch]
    if (distanceSquared >= lampPosRadius.w * lampPosRadius.w)
    {
        return 1.0f;
    }

    float4 lp = mul(float4(worldPos, 1.0f), vp);
    if (lp.w <= 0.0f)
        return 1.0f;
    float3 proj = lp.xyz / lp.w;
    float2 local = proj.xy * float2(0.5f, -0.5f) + 0.5f;
    if (local.x < 0.0f || local.x > 1.0f || local.y < 0.0f || local.y > 1.0f)
        return 1.0f;

    float current = proj.z;
    float bias = 0.0025f;
    float2 t = LampShadowTexel;
    // Clamp to texel centres: the exact upper tile edge belongs to the adjacent lamp.
    float2 lo = tile.xy + t * 0.5f;
    float2 hi = tile.xy + tile.zw - t * 0.5f;
    float2 baseUV = tile.xy + local * tile.zw;
    float occ = 0.0f;
    occ += (current - bias > tex2Dlod(LampShadowSampler, float4(clamp(baseUV + float2(-0.5f, -0.5f) * t, lo, hi), 0.0f, 0.0f)).r) ? 1.0f : 0.0f;
    occ += (current - bias > tex2Dlod(LampShadowSampler, float4(clamp(baseUV + float2( 0.5f, -0.5f) * t, lo, hi), 0.0f, 0.0f)).r) ? 1.0f : 0.0f;
    occ += (current - bias > tex2Dlod(LampShadowSampler, float4(clamp(baseUV + float2(-0.5f,  0.5f) * t, lo, hi), 0.0f, 0.0f)).r) ? 1.0f : 0.0f;
    occ += (current - bias > tex2Dlod(LampShadowSampler, float4(clamp(baseUV + float2( 0.5f,  0.5f) * t, lo, hi), 0.0f, 0.0f)).r) ? 1.0f : 0.0f;
    occ *= 0.25f;

    float dist = sqrt(distanceSquared);
    float influence = saturate(1.0f - dist / lampPosRadius.w);
    float2 edge = min(local, 1.0f - local);
    float border = saturate(min(edge.x, edge.y) / 0.12f);
    return 1.0f - occ * influence * border;
}

float LampShadow(float3 worldPos)
{
    float s = 1.0f;
    for (int i = 0; i < MAX_LAMP_SHADOWS; i++)
    {
        if (i >= LampShadowCount)
            break;
        s *= SampleLampShadowTile(LampShadowVP[i], LampShadowPos[i], LampShadowTile[i], worldPos);
    }
    return s;
}

#endif
