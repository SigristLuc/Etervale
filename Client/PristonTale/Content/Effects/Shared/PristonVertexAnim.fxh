#ifndef PRISTON_VERTEXANIM_FXH
#define PRISTON_VERTEXANIM_FXH

// Shared by color and depth passes so displaced geometry and shadows remain aligned.
// WindParams packs (amplitudeX, amplitudeZ, bottomY, inverseHeight).

float VertexAnimTime;
float MaterialWaterRipple;
float4 WindParams;

static const float WaterRippleAmplitude = 9.0f;

float3 WaterRippleOffset(float3 worldPos)
{
    float t = VertexAnimTime;
    float dx = sin(worldPos.x * 0.025f + worldPos.z * 0.013f + t * 1.7f);
    float dz = cos(worldPos.z * 0.025f + worldPos.x * 0.013f + t * 1.9f);
    return float3(dx, 0.0f, dz) * (WaterRippleAmplitude * MaterialWaterRipple);
}

float3 WindOffset(float3 worldPos)
{
    // Height weight: 0 at the mesh bottom, 1 at the top (anchored bottom, like WindMeshBottom).
    float weight = saturate((worldPos.y - WindParams.z) * WindParams.w);
    float t = VertexAnimTime;
    // A slow period and small spatial term keep foliage motion coherent.
    float sx = sin(t * 0.45f + worldPos.z * 0.006f);
    float sz = sin(t * 0.50f + worldPos.x * 0.006f);
    return float3(WindParams.x * sx, 0.0f, WindParams.y * sz) * weight;
}

float3 VertexAnimOffset(float3 worldPos)
{
    float3 offset = float3(0.0f, 0.0f, 0.0f);
    [branch]
    if (MaterialWaterRipple != 0.0f)
    {
        offset += WaterRippleOffset(worldPos);
    }

    [branch]
    if (WindParams.x != 0.0f || WindParams.y != 0.0f)
    {
        offset += WindOffset(worldPos);
    }

    return offset;
}

#endif
