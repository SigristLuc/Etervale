// Image-based lighting from the sky dome (PristonEnvironmentLighting), rebuilt when the sky changes:
//  - Octahedral: the six 90-degree faces of the dome (rendered display-referred like the sky behind the scene) to an HDR
//    octahedral map, decoded and divided by the frame's nominal display scale; below the horizon the ground's bounce;
//  - Prefilter: the GGX-prefiltered radiance for one roughness level of the specular atlas (Karis 2013, split sum),
//    importance-sampled from the octahedral map;
//  - Irradiance (compute): the order-2 spherical harmonics of the sky (Ramamoorthi and Hanrahan 2001), scaled so that an
//    upward surface receives the light model's sky light, plus a uniform ground below; stored convolved with the
//    cosine lobe and divided by pi, the shaders' light unit. Entry 9 holds the scale for the specular atlas.
#include "../Core/Common.hlsli"
#include "../Core/Color.hlsli"
#include "../Core/Fullscreen.hlsli"
#include "../Lighting/Octahedral.hlsli"

cbuffer EnvironmentConstants : register(b0)
{
    float4 EnvParams;          // x nominal display scale, y roughness (Prefilter), z octahedral size (px), w sample count
    float3 SkyAmbientTarget;   // the light model's sky light on an upward surface (pre-exposed E / pi)
    float EnvPadding0;
    float3 GroundAmbientTarget; // the ground's bounce on a downward surface
    float EnvPadding1;
};

Texture2D<float4> FaceAtlas : register(t0);     // six faces side by side, 8-bit sRGB display values
Texture2D<float4> Octahedral : register(t1);    // HDR octahedral radiance
StructuredBuffer<float4> PreviousSh : register(t2);
SamplerState FaceSampler : register(s0) = sampler_state
{
    Texture = (FaceAtlas); MinFilter = Linear; MagFilter = Linear; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};
SamplerState OctahedralSampler : register(s1) = sampler_state
{
    Texture = (Octahedral); MinFilter = Linear; MagFilter = Linear; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};

RWStructuredBuffer<float4> Sh : register(u0);

// Lanczos sigma factors of the order-2 bands: sinc(pi l / 3).
static const float ShWindowBand1 = 0.826993;
static const float ShWindowBand2 = 0.413497;

static const float3 FaceForward[6] = { float3(1, 0, 0), float3(-1, 0, 0), float3(0, 1, 0), float3(0, -1, 0), float3(0, 0, 1), float3(0, 0, -1) };
static const float3 FaceUp[6] = { float3(0, 1, 0), float3(0, 1, 0), float3(0, 0, -1), float3(0, 0, 1), float3(0, 1, 0), float3(0, 1, 0) };

// The dome as seen in direction d: the cube face of d's major axis, projected like Matrix.CreateLookAt and a 90-degree
// perspective (PristonEnvironmentLighting.FaceView).
float3 SkyRadiance(float3 d)
{
    float3 a = abs(d);
    uint face = a.x >= a.y && a.x >= a.z ? (d.x >= 0.0 ? 0 : 1) : a.y >= a.z ? (d.y >= 0.0 ? 2 : 3) : (d.z >= 0.0 ? 4 : 5);
    float3 forward = FaceForward[face];
    float3 back = -forward;
    float3 right = SafeNormalize(cross(FaceUp[face], back));
    float3 up = cross(back, right);
    float along = max(dot(forward, d), 1.0e-4);
    float2 ndc = float2(dot(right, d), dot(up, d)) / along;
    float2 local = clamp(float2(ndc.x, -ndc.y) * 0.5 + 0.5, 0.01, 0.99);
    float3 display = FaceAtlas.SampleLevel(FaceSampler, float2((face + local.x) / 6.0, local.y), 0).rgb;
    return SrgbToLinear(display) / EnvParams.x;
}

float4 PS_Octahedral(FullscreenOut i) : SV_Target0
{
    float3 d = OctahedralDecode(i.Uv);
    float3 sky = SkyRadiance(d);
    // The ground in the light model's units, converted to the atlas's (divided by its scale).
    float scale = max(PreviousSh[9].x, 1.0e-4);
    float3 ground = GroundAmbientTarget / scale;
    float below = smoothstep(-0.02, -0.15, d.y);
    return float4(min(lerp(sky, ground, below), HdrCap), 1.0);
}

float RadicalInverse(uint bits)
{
    bits = (bits << 16u) | (bits >> 16u);
    bits = ((bits & 0x55555555u) << 1u) | ((bits & 0xAAAAAAAAu) >> 1u);
    bits = ((bits & 0x33333333u) << 2u) | ((bits & 0xCCCCCCCCu) >> 2u);
    bits = ((bits & 0x0F0F0F0Fu) << 4u) | ((bits & 0xF0F0F0F0u) >> 4u);
    bits = ((bits & 0x00FF00FFu) << 8u) | ((bits & 0xFF00FF00u) >> 8u);
    return float(bits) * 2.3283064365386963e-10;
}

float3 ImportanceSampleGgx(float2 xi, float a, float3 n)
{
    float phi = 2.0 * PI * xi.x;
    float cosTheta = sqrt((1.0 - xi.y) / (1.0 + (a * a - 1.0) * xi.y));
    float sinTheta = sqrt(1.0 - cosTheta * cosTheta);
    float3 h = float3(sinTheta * cos(phi), sinTheta * sin(phi), cosTheta);
    float3 up = abs(n.y) < 0.999 ? float3(0, 1, 0) : float3(1, 0, 0);
    float3 tangentX = normalize(cross(up, n));
    float3 tangentY = cross(n, tangentX);
    return tangentX * h.x + tangentY * h.y + n * h.z;
}

float4 PS_Prefilter(FullscreenOut i) : SV_Target0
{
    float3 n = OctahedralDecode(i.Uv);
    float roughness = EnvParams.y;
    [branch]
    if (roughness <= 0.0)
    {
        return float4(Octahedral.SampleLevel(OctahedralSampler, i.Uv, 0).rgb, 1.0);
    }

    float a = roughness * roughness;
    uint count = (uint)EnvParams.w;
    float3 sum = 0.0;
    float weight = 0.0;
    [loop]
    for (uint k = 0; k < count; k++)
    {
        float2 xi = float2((k + 0.5) / count, RadicalInverse(k));
        float3 h = ImportanceSampleGgx(xi, a, n);
        float3 l = 2.0 * dot(n, h) * h - n;
        float nl = dot(n, l);
        if (nl > 0.0)
        {
            sum += Octahedral.SampleLevel(OctahedralSampler, OctahedralEncode(l), 0).rgb * nl;
            weight += nl;
        }
    }

    return float4(sum / max(weight, 1.0e-4), 1.0);
}

// Real spherical harmonics, bands 0-2.
void ShBasis(float3 d, out float b[9])
{
    b[0] = 0.282095;
    b[1] = 0.488603 * d.y;
    b[2] = 0.488603 * d.z;
    b[3] = 0.488603 * d.x;
    b[4] = 1.092548 * d.x * d.y;
    b[5] = 1.092548 * d.y * d.z;
    b[6] = 0.315392 * (3.0 * d.z * d.z - 1.0);
    b[7] = 1.092548 * d.x * d.z;
    b[8] = 0.546274 * (d.x * d.x - d.y * d.y);
}

// Cosine-lobe convolution per band, divided by pi (A0 = pi, A1 = 2 pi / 3, A2 = pi / 4).
static const float BandScale[9] = { 1.0, 2.0 / 3.0, 2.0 / 3.0, 2.0 / 3.0, 0.25, 0.25, 0.25, 0.25, 0.25 };

float3 EvaluateSh(float3 c[9], float3 n)
{
    float b[9];
    ShBasis(n, b);
    float3 e = 0.0;
    [unroll]
    for (uint k = 0; k < 9; k++)
    {
        e += c[k] * (b[k] * BandScale[k]);
    }

    return e;
}

// Share of the light model's colour in the sky's irradiance.
static const float SkyHueShare = 0.65;

#define SH_THREADS 256
#define SH_GRID 32
groupshared float3 Partial[SH_THREADS];
groupshared float PartialWeight[SH_THREADS];

[numthreads(SH_THREADS, 1, 1)]
void CS_Irradiance(uint index : SV_GroupIndex)
{
    // Every thread integrates four texels of a 32x32 grid over the octahedral map. The solid angle of an octahedral
    // texel is proportional to 1 / |d|^3 for the unnormalised direction on the octahedron.
    float3 c[9];
    [unroll]
    for (uint z = 0; z < 9; z++)
    {
        c[z] = 0.0;
    }

    float weight = 0.0;
    [unroll]
    for (uint t = 0; t < 4; t++)
    {
        uint cell = index * 4 + t;
        float2 uv = (float2(cell % SH_GRID, cell / SH_GRID) + 0.5) / SH_GRID;
        float2 p = uv * 2.0 - 1.0;
        float3 raw = float3(p.x, 1.0 - abs(p.x) - abs(p.y), p.y);
        float3 d = OctahedralDecode(uv);
        if (d.y < 0.0)
        {
            continue;
        }

        float length3 = pow(dot(raw, raw), 1.5);
        float w = 1.0 / max(length3, 1.0e-4);
        float3 radiance = Octahedral.SampleLevel(OctahedralSampler, uv, 0).rgb;
        float b[9];
        ShBasis(d, b);
        [unroll]
        for (uint k = 0; k < 9; k++)
        {
            c[k] += radiance * (b[k] * w);
        }

        weight += w;
    }

    // The whole sphere's weight is 4 pi: the upper hemisphere's texels carry half of the total octahedral weight.
    PartialWeight[index] = weight;
    GroupMemoryBarrierWithGroupSync();
    for (uint stride = SH_THREADS / 2; stride > 0; stride >>= 1)
    {
        if (index < stride)
        {
            PartialWeight[index] += PartialWeight[index + stride];
        }

        GroupMemoryBarrierWithGroupSync();
    }

    float hemisphereWeight = PartialWeight[0];
    float3 sky[9];
    [unroll]
    for (uint coefficient = 0; coefficient < 9; coefficient++)
    {
        Partial[index] = c[coefficient];
        GroupMemoryBarrierWithGroupSync();
        for (uint s = SH_THREADS / 2; s > 0; s >>= 1)
        {
            if (index < s)
            {
                Partial[index] += Partial[index + s];
            }

            GroupMemoryBarrierWithGroupSync();
        }

        sky[coefficient] = Partial[0] * (2.0 * PI / max(hemisphereWeight, 1.0e-6));
        GroupMemoryBarrierWithGroupSync();
    }

    if (index != 0)
    {
        return;
    }

    // The hemispheres' step at the horizon rings in an order-2 expansion (dark undersides and bright bands on curved
    // surfaces): the bands are windowed by Lanczos sigma factors, sinc(l / 3), before the sky is scaled. The specular
    // atlas's scale comes from the unwindowed sky (the reflections are not dimmed or brightened by the window).
    float specularScale = Luminance(SkyAmbientTarget) / max(Luminance(EvaluateSh(sky, float3(0, 1, 0))), 1.0e-6);
    [unroll]
    for (uint w = 1; w < 9; w++)
    {
        sky[w] *= w < 4 ? ShWindowBand1 : ShWindowBand2;
    }

    // Scale the sky to the light model's sky light on an upward surface, keeping its direction; the colour is mostly the
    // light model's (SkyHueShare), the rest the dome's (its art hues are strong, a teal night or a purple dusk).
    float3 up = EvaluateSh(sky, float3(0, 1, 0));
    float scale = Luminance(SkyAmbientTarget) / max(Luminance(up), 1.0e-6);
    float3 tint = lerp(1.0, clamp(SkyAmbientTarget / max(up * scale, 1.0e-6), 0.25, 4.0), SkyHueShare);
    [unroll]
    for (uint t2 = 0; t2 < 9; t2++)
    {
        sky[t2] *= tint;
    }

    // A uniform ground of radiance G below the horizon, projected on the basis: by symmetry only the constant term and
    // the y term are non-zero (its step windowed like the sky's).
    float3 g = GroundAmbientTarget;
    [unroll]
    for (uint k = 0; k < 9; k++)
    {
        Sh[k] = float4(sky[k] * scale, 0.0);
    }

    Sh[0] = float4(sky[0] * scale + g * (0.282095 * 2.0 * PI), 0.0);
    Sh[1] = float4(sky[1] * scale - g * (0.488603 * PI * ShWindowBand1), 0.0);
    Sh[9] = float4(specularScale, 0.0, 0.0, 1.0);
}

technique Octahedral { pass P0 { VertexShader = compile vs_6_0 VS_Fullscreen(); PixelShader = compile ps_6_0 PS_Octahedral(); } }
technique Prefilter { pass P0 { VertexShader = compile vs_6_0 VS_Fullscreen(); PixelShader = compile ps_6_0 PS_Prefilter(); } }
technique Irradiance { pass P0 { ComputeShader = compile cs_6_0 CS_Irradiance(); } }
