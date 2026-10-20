// Volumetric sunlight through height fog (PristonVolumetricLight), after the scene's surfaces:
//  - March (quarter resolution): along each view ray to the surface (the pre-pass's linear depth, at most MaxDistance),
//    twelve jittered samples of a fog whose density falls exponentially with height; each sample is lit by the sun
//    through the cascaded shadow map (Lighting/Shading.hlsli's atlas, one tap), with a Henyey-Greenstein phase, and
//    attenuated by the fog in front of it;
//  - Composite: the in-scattered light, upsampled from the four nearest texels weighted bilinearly and steeply by depth
//    (at an edge the texel of the pixel's own surface wins), added to the scene.
// The distance fog of the lit passes stays; this adds the light shafts and the near haze the sun lights.
#include "../Core/Common.hlsli"
#include "../Core/Fullscreen.hlsli"

#define MAX_CASCADES 4

cbuffer VolumetricConstants : register(b0)
{
    float4 VolumeSize;          // xy quarter-resolution size (px), zw full-resolution size (px)
    float4 VolumeProjection;    // x tan(half fov x), y tan(half fov y), z max distance (world), w frame noise offset
    float3 CameraRight;
    float FogDensity;           // extinction per world unit at the reference height
    float3 CameraUp;
    float FogFalloff;           // 1 / scale height (per world unit)
    float3 CameraForward;
    float FogReferenceHeight;   // world height where the density is FogDensity
    float3 CameraPosition;
    float PhaseG;               // Henyey-Greenstein anisotropy
    float3 SunDirection;        // toward the sun
    float CascadeCount;
    float3 SunRadiance;         // pre-exposed radiance of a white Lambertian surface facing the sun
    float VolumePadding;
    float4x4 CascadeViewProjection[MAX_CASCADES];
    float4 CascadeAtlas[MAX_CASCADES];
    float4 CascadeSplits;
};

Texture2D<float4> DepthMap : register(t0);
Texture2D<float4> ShadowMap : register(t1);
Texture2D<float4> Inscatter : register(t2);
SamplerState DepthSampler : register(s0) = sampler_state
{
    Texture = (DepthMap); MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};
SamplerState ShadowSampler : register(s1) = sampler_state
{
    Texture = (ShadowMap); MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};
SamplerState InscatterSampler : register(s2) = sampler_state
{
    Texture = (Inscatter); MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};

static const uint Steps = 12;
static const float SkyDepth = 50000.0;

float3 ViewRay(float2 uv)
{
    return CameraForward + (uv.x * 2.0 - 1.0) * VolumeProjection.x * CameraRight + (1.0 - uv.y * 2.0) * VolumeProjection.y * CameraUp;
}

float FogDensityAt(float height)
{
    return FogDensity * exp(-max(height - FogReferenceHeight, -2000.0) * FogFalloff);
}

float SunVisibility(float3 position, float viewDepth)
{
    uint count = clamp((uint)CascadeCount, 1u, (uint)MAX_CASCADES);
    if (viewDepth >= CascadeSplits[count - 1])
    {
        return 1.0;
    }

    uint c = 0;
    [unroll]
    for (uint k = 0; k < MAX_CASCADES - 1; k++)
    {
        c += (k + 1 < count && viewDepth >= CascadeSplits[k]) ? 1 : 0;
    }

    float4 light = mul(float4(position, 1.0), CascadeViewProjection[c]);
    float3 projected = light.xyz / light.w;
    float2 local = saturate(float2(projected.x, -projected.y) * 0.5 + 0.5);
    float4 tile = CascadeAtlas[c];
    float stored = ShadowMap.SampleLevel(ShadowSampler, tile.xy + local * tile.zw, 0).r;
    return saturate(projected.z) > stored + 1.0e-4 ? 0.0 : 1.0;
}

float4 PS_March(FullscreenOut i) : SV_Target0
{
    float2 pixel = floor(i.Uv * VolumeSize.xy);
    float2 uv = (pixel + 0.5) / VolumeSize.xy;
    // The nearest of four depths in the 4x4 block: thin foreground (actors, branches) keeps texels of its own, so the
    // upsampling never has to paint it with the fog behind it.
    float2 quarter = 1.0 / VolumeSize.zw;
    float depth = DepthMap.SampleLevel(DepthSampler, uv + quarter * float2(-1.0, -1.0), 0).r;
    depth = min(depth, DepthMap.SampleLevel(DepthSampler, uv + quarter * float2(1.0, -1.0), 0).r);
    depth = min(depth, DepthMap.SampleLevel(DepthSampler, uv + quarter * float2(-1.0, 1.0), 0).r);
    depth = min(depth, DepthMap.SampleLevel(DepthSampler, uv + quarter * float2(1.0, 1.0), 0).r);
    float3 ray = ViewRay(uv);
    float rayLength = length(ray);
    float3 direction = ray / rayLength;
    // View depth along the forward axis to distance along the ray.
    float distance = min(depth >= SkyDepth ? VolumeProjection.z : depth * rayLength, VolumeProjection.z);
    float2 q = pixel + VolumeProjection.w * 5.588238;
    float jitter = frac(52.9829189 * frac(dot(q, float2(0.06711056, 0.00583715))));
    float cosTheta = dot(direction, SunDirection);
    float g = PhaseG;
    float phase = (1.0 - g * g) / (4.0 * PI * pow(max(1.0 + g * g - 2.0 * g * cosTheta, 1.0e-4), 1.5));
    float stepLength = distance / Steps;
    float3 scattered = 0.0;
    float opticalDepth = 0.0;
    [loop]
    for (uint k = 0; k < Steps; k++)
    {
        float t = (k + jitter) * stepLength;
        float3 position = CameraPosition + direction * t;
        float density = FogDensityAt(position.y);
        float visibility = SunVisibility(position, t / rayLength);
        opticalDepth += density * stepLength;
        scattered += visibility * density * exp(-opticalDepth) * stepLength;
    }

    // The sun's radiance is that of a white surface facing it (E / pi): the in-scattered radiance is E * phase.
    return float4(min(SunRadiance * PI * phase * scattered, HdrCap), depth);
}

float4 PS_Composite(FullscreenOut i) : SV_Target0
{
    float2 full = i.Uv * VolumeSize.zw;
    float depth = DepthMap.SampleLevel(DepthSampler, i.Uv, 0).r;
    float2 lowPixel = full * (VolumeSize.xy / VolumeSize.zw) - 0.5;
    float2 base = floor(lowPixel);
    float2 f = lowPixel - base;
    float3 sum = 0.0;
    float weights = 0.0;
    [unroll]
    for (int y = 0; y < 2; y++)
    {
        [unroll]
        for (int x = 0; x < 2; x++)
        {
            float4 s = Inscatter.SampleLevel(InscatterSampler, (base + float2(x, y) + 0.5) / VolumeSize.xy, 0);
            float bilinear = (x == 0 ? 1.0 - f.x : f.x) * (y == 0 ? 1.0 - f.y : f.y) + 1.0e-3;
            // Depth weight steep enough to pick the nearest-depth texel at an edge (no fog bled onto a silhouette).
            float relative = abs(s.w - depth) / max(depth, 1.0);
            float w = bilinear / (1.0e-4 + relative * relative);
            sum += s.rgb * w;
            weights += w;
        }
    }

    return float4(sum / max(weights, 1.0e-6), 0.0);
}

technique March { pass P0 { VertexShader = compile vs_6_0 VS_Fullscreen(); PixelShader = compile ps_6_0 PS_March(); } }
technique Composite { pass P0 { VertexShader = compile vs_6_0 VS_Fullscreen(); PixelShader = compile ps_6_0 PS_Composite(); } }
