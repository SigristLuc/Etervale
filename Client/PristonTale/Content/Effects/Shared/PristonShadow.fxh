#ifndef PRISTON_SHADOW_FXH
#define PRISTON_SHADOW_FXH

float4x4 LightViewProjection;
float ShadowBias;
float ShadowMaxSlopeBias;
float ShadowNormalOffset;
float ShadowStrength;
float2 ShadowTexelSize;
bool ShadowsEnabled;
bool ReceivesShadow;
texture ShadowMap;

sampler ShadowSampler = sampler_state
{
    Texture = <ShadowMap>;
    AddressU = Clamp; AddressV = Clamp;
    MinFilter = Point; MagFilter = Point; MipFilter = None;
};

// A world-rotated Poisson disk avoids the grid artifacts of wide regular PCF kernels.
// Slope bias and animated-normal offset suppress grazing and low-poly self-shadow acne.
#define PCF_RADIUS_TEXELS 2.5f

static const float2 PoissonDisk[16] =
{
    float2(-0.942016f, -0.399062f), float2( 0.945586f, -0.768907f),
    float2(-0.094184f, -0.929388f), float2( 0.344959f,  0.293877f),
    float2(-0.915885f,  0.457714f), float2(-0.815442f, -0.879124f),
    float2(-0.382775f,  0.276768f), float2( 0.974844f,  0.756484f),
    float2( 0.443233f, -0.975115f), float2( 0.537430f, -0.473734f),
    float2(-0.264969f, -0.418930f), float2( 0.791975f,  0.190902f),
    float2(-0.241888f,  0.997065f), float2(-0.814100f,  0.914376f),
    float2( 0.199841f,  0.786414f), float2( 0.143832f, -0.141008f)
};

// Polynomial hashing avoids per-pixel trigonometry while keeping rotation world-stable.
float2 ShadowRotation(float3 worldPos)
{
    float2 cell = floor(worldPos.xz * 0.25f);
    float3 hash = frac(float3(cell.x, cell.y, cell.x) * float3(0.1031f, 0.1030f, 0.0973f));
    hash += dot(hash, hash.yxz + 33.33f);
    float2 direction = frac((hash.xx + hash.yz) * hash.zy) * 2.0f - 1.0f;
    return direction * rsqrt(max(dot(direction, direction), 1.0e-4f));
}

float CalculateShadow(float3 worldPos, float3 unitNormal, float ndotl)
{
    [branch]
    if (!ShadowsEnabled || !ReceivesShadow || ndotl <= 0.0f || ShadowStrength <= 0.0f)
    {
        return 1.0f;
    }

    float normalOffsetScale = 0.25f + 0.75f * (1.0f - ndotl);
    float3 receiverPosition = worldPos + unitNormal * ShadowNormalOffset * normalOffsetScale;
    float4 lightPosition = mul(float4(receiverPosition, 1.0f), LightViewProjection);
    float3 projected = lightPosition.xyz / lightPosition.w;
    float2 shadowUv = projected.xy * 0.5f + 0.5f;
    shadowUv.y = 1.0f - shadowUv.y;
    if (shadowUv.x < 0.0f || shadowUv.x > 1.0f || shadowUv.y < 0.0f || shadowUv.y > 1.0f)
    {
        return 1.0f;
    }

    float currentDepth = saturate(projected.z);
    float bias = max(ShadowBias, ShadowMaxSlopeBias * (1.0f - ndotl));
    float radius = ShadowTexelSize.x * PCF_RADIUS_TEXELS;

    float2 rotation = ShadowRotation(worldPos);
    float2x2 sampleTransform = float2x2(rotation.x, -rotation.y, rotation.y, rotation.x) * radius;
    float referenceDepth = currentDepth - bias;

    float shadowSamples = 0.0f;
    [unroll] for (int i = 0; i < 16; i++)
    {
        float2 off = mul(PoissonDisk[i], sampleTransform);
        shadowSamples += referenceDepth > tex2D(ShadowSampler, shadowUv + off).r ? 1.0f : 0.0f;
    }

    float occlusion = shadowSamples / 16.0f;
    return lerp(1.0f, 1.0f - ShadowStrength, occlusion);
}

#endif
