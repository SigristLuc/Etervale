// Direct and ambient lighting of a Surface (Materials/Material.hlsli): the sun with its cascaded shadows (PCF or PCSS),
// the screen-space occlusion (ambient occlusion and contact shadows), the local lights and a sky/ground hemispherical
// ambient. Requires FRAME_CONSTANTS, LIGHTING_CONSTANTS and LIGHTING_RESOURCES.
#ifndef LIGHTING_SHADING_HLSLI
#define LIGHTING_SHADING_HLSLI

#include "Brdf.hlsli"
#include "Octahedral.hlsli"

// Share of a cascade's far end over which it hands over to the next one (dithered), and the last one fades out.
static const float CascadeBlendBand = 0.12;
// PCSS: the deepest blocker distance (world units) the blocker search covers, and the largest filter radius (texels).
static const float PcssSearchDepth = 600.0;
static const float PcssMaxRadiusTexels = 9.0;
// Lamp shadows: a soft source, so a wide kernel and a little bounce light left in them.
static const float LampShadowStrength = 0.8;
static const float LampShadowRadiusTexels = 2.5;

static const float2 PoissonDisk[16] =
{
    float2(-0.942016, -0.399062), float2( 0.945586, -0.768907),
    float2(-0.094184, -0.929388), float2( 0.344959,  0.293877),
    float2(-0.915885,  0.457714), float2(-0.815442, -0.879124),
    float2(-0.382775,  0.276768), float2( 0.974844,  0.756484),
    float2( 0.443233, -0.975115), float2( 0.537430, -0.473734),
    float2(-0.264969, -0.418930), float2( 0.791975,  0.190902),
    float2(-0.241888,  0.997065), float2(-0.814100,  0.914376),
    float2( 0.199841,  0.786414), float2( 0.143832, -0.141008)
};

// Interleaved gradient noise (Jimenez 2014) of a pixel, offset per frame when the temporal anti-aliasing integrates it.
float PixelNoise(float2 pixel)
{
    float2 p = pixel + NoiseParams.x * 5.588238;
    return frac(52.9829189 * frac(dot(p, float2(0.06711056, 0.00583715))));
}

float ShadowDepthAt(float2 uv)
{
    return ShadowMap.SampleLevel(ShadowSampler, uv, 0).r;
}

// Visibility of one cascade. The atlas tile holds the orthographic light's z (Passes/Surface.fx ShadowCaster), which is
// linear in light-space depth; D3D clip conventions (the Vulkan compiler flips Y), so uv = (x, -y) * 0.5 + 0.5.
float CascadeVisibility(uint c, float3 worldPosition, float3 normal, float nl, float2x2 rotation)
{
    float texelWorld = CascadeTexelWorld[c];
    float depthRange = CascadeDepthRange[c];
    float normalOffset = texelWorld * (0.8 + 1.6 * (1.0 - nl));
    float4 lightPosition = mul(float4(worldPosition + normal * normalOffset, 1.0), CascadeViewProjection[c]);
    float3 projected = lightPosition.xyz / lightPosition.w;
    float2 local = float2(projected.x, -projected.y) * 0.5 + 0.5;
    float4 tile = CascadeAtlas[c];
    float2 texel = ShadowTexel.xy;
    float2 lo = tile.xy + texel;
    float2 hi = tile.xy + tile.zw - texel;
    float2 centre = tile.xy + local * tile.zw;
    float reference = saturate(projected.z) - texelWorld * (0.5 + 1.5 * (1.0 - nl)) / depthRange;
    float radius = ShadowTexel.z;
    [branch]
    if (ShadowParams.y > 0.5)
    {
        // PCSS (Fernando 2005) for a directional light: blockers within the reach of the widest penumbra, then a
        // filter as wide as the penumbra their mean depth gives, (receiver - blocker) * tan(light radius).
        float lightTan = ShadowParams.z;
        float search = clamp(PcssSearchDepth * lightTan / texelWorld, 1.5, PcssMaxRadiusTexels);
        float blockerSum = 0.0;
        float blockers = 0.0;
        [unroll]
        for (int b = 0; b < 16; b++)
        {
            float stored = ShadowDepthAt(clamp(centre + mul(PoissonDisk[b], rotation) * texel * search, lo, hi));
            if (stored < reference)
            {
                blockerSum += stored;
                blockers += 1.0;
            }
        }

        if (blockers < 0.5)
        {
            return 1.0;
        }

        float penumbraWorld = (reference - blockerSum / blockers) * depthRange * lightTan;
        radius = clamp(penumbraWorld / texelWorld, 1.0, PcssMaxRadiusTexels);
    }

    float occluded = 0.0;
    [unroll]
    for (int i = 0; i < 16; i++)
    {
        float stored = ShadowDepthAt(clamp(centre + mul(PoissonDisk[i], rotation) * texel * radius, lo, hi));
        occluded += reference > stored ? 1.0 : 0.0;
    }

    return 1.0 - occluded / 16.0;
}

// Sun visibility in [0, 1]: the cascade by view depth, dithered into the next one over the blend band; the last
// cascade fades out toward the end of the shadow distance.
float SunShadow(float3 worldPosition, float3 normal, float nl, float viewDepth, float2 pixel)
{
    uint count = clamp((uint)ShadowParams.x, 1u, (uint)MAX_CASCADES);
    [branch]
    if (ShadowParams.w <= 0.0 || nl <= 0.0 || viewDepth >= CascadeSplits[count - 1])
    {
        return 1.0;
    }

    uint c = 0;
    [unroll]
    for (uint k = 0; k < MAX_CASCADES - 1; k++)
    {
        c += (k + 1 < count && viewDepth >= CascadeSplits[k]) ? 1 : 0;
    }

    float noise = PixelNoise(pixel);
    float split = CascadeSplits[c];
    float start = c == 0 ? 0.0 : CascadeSplits[c - 1];
    float band = (split - viewDepth) / max((split - start) * CascadeBlendBand, 1.0e-3);
    float fade = 1.0;
    if (band < 1.0)
    {
        if (c + 1 < count)
        {
            c += noise > band ? 1 : 0;
        }
        else
        {
            fade = saturate(band);
        }
    }

    float angle = noise * 2.0 * PI;
    float2 direction = float2(cos(angle), sin(angle));
    float2x2 rotation = float2x2(direction.x, -direction.y, direction.y, direction.x);
    float visibility = CascadeVisibility(c, worldPosition, normal, nl, rotation);
    return 1.0 - ShadowParams.w * (1.0 - visibility) * fade;
}

// The screen-space occlusion at a pixel (Passes/AmbientOcclusion.fx): x ambient occlusion, y contact shadow visibility.
// The map is half resolution; the four nearest texels are weighted bilinearly and by how close their depth is.
float2 ScreenOcclusion(float2 pixel, float viewDepth)
{
    [branch]
    if (ShadowTexel.w < 0.5)
    {
        return 1.0;
    }

    float2 position = pixel * 0.5 - 0.5;
    float2 base = floor(position);
    float2 f = position - base;
    float2 sum = 0.0;
    float weights = 0.0;
    [unroll]
    for (int y = 0; y < 2; y++)
    {
        [unroll]
        for (int x = 0; x < 2; x++)
        {
            float4 s = OcclusionMap.SampleLevel(OcclusionSampler, (base + float2(x, y) + 0.5) * OcclusionParams.xy, 0);
            float bilinear = (x == 0 ? 1.0 - f.x : f.x) * (y == 0 ? 1.0 - f.y : f.y) + 1.0e-3;
            float relative = abs(s.z - viewDepth) / max(viewDepth, 1.0);
            float w = bilinear / (1.0e-4 + relative * relative);
            sum += s.xy * w;
            weights += w;
        }
    }

    float2 occlusion = sum / weights;
    return float2(lerp(1.0, occlusion.x, OcclusionParams.w), lerp(1.0, occlusion.y, OcclusionParams.z));
}

// The lamps' cube shadows (Lighting/PristonLampShadows.cs): six faces per lamp side by side in its row, each a 90-degree
// view along a major axis (FaceForward, up FaceUp, as Matrix.CreateLookAt builds it), holding the distance to the lamp
// over its radius. Four wide Poisson taps soften the edge; a share of the light stays in the shadow (bounce from the
// lamp-lit surroundings).
static const float3 FaceForward[6] = { float3(1, 0, 0), float3(-1, 0, 0), float3(0, 1, 0), float3(0, -1, 0), float3(0, 0, 1), float3(0, 0, -1) };
static const float3 FaceUp[6] = { float3(0, 1, 0), float3(0, 1, 0), float3(0, 0, -1), float3(0, 0, 1), float3(0, 1, 0), float3(0, 1, 0) };

float LampShadow(uint row, float3 fromLight, float radius, float nl)
{
    float3 a = abs(fromLight);
    uint face = a.x >= a.y && a.x >= a.z ? (fromLight.x >= 0.0 ? 0 : 1)
              : a.y >= a.z ? (fromLight.y >= 0.0 ? 2 : 3)
              : (fromLight.z >= 0.0 ? 4 : 5);
    float3 forward = FaceForward[face];
    float3 back = -forward;
    float3 right = SafeNormalize(cross(FaceUp[face], back));
    float3 up = cross(back, right);
    float along = dot(forward, fromLight);
    float2 ndc = float2(dot(right, fromLight), dot(up, fromLight)) / max(along, 1.0e-3);
    float2 local = float2(ndc.x, -ndc.y) * 0.5 + 0.5;
    float faceSize = LampShadowParams.z;
    float2 texel = LampShadowParams.xy;
    // Keep the taps one texel inside the face.
    float2 inner = clamp(local * faceSize, 1.0, faceSize - 1.0);
    float2 origin = float2(face, row) * faceSize;
    float distance = length(fromLight) / radius;
    float reference = distance - (0.004 + 0.012 * (1.0 - nl));
    float lit = 0.0;
    [unroll]
    for (int k = 0; k < 4; k++)
    {
        float2 offset = PoissonDisk[k * 4] * LampShadowRadiusTexels;
        float2 position = clamp(inner + offset, 1.0, faceSize - 1.0);
        float stored = LampShadowMap.SampleLevel(LampShadowSampler, (origin + position) * texel, 0).r;
        lit += reference <= stored ? 0.25 : 0.0;
    }

    return 1.0 - LampShadowStrength * (1.0 - lit);
}

// Specular occlusion from the ambient occlusion (Lagarde and de Rousiers 2014).
float SpecularOcclusion(float nv, float ao, float roughness)
{
    return saturate(pow(nv + ao, exp2(-16.0 * roughness - 1.0)) - 1.0 + ao);
}

// Light reaching a surface, split for the debug views and for the material's own terms.
struct Lighting
{
    float3 Diffuse;
    float3 Specular;
};

static const float LampWrap = 0.25;
// Lamps fall off with the inverse square of the distance (PristonRenderer: the colour is the illuminance at
// LampReferenceDistance), held constant inside LampMinDistance (the lamp's own size), and windowed to zero at the
// radius by (1 - (d / r)^4)^2 (Karis 2013). The reference is one distance for every lamp, so a lamp's intensity is
// comparable whatever its range: a small brazier is set dimmer, not given a shorter reference.
static const float LampReferenceDistance = 150.0;
static const float LampMinDistance = 45.0;

// Local lights keep the legacy radius-limited falloff, (1 - d/r)^2, so the authored lamp ranges still hold; transient
// combat lights stay radial (normal-independent), as the game has always lit them.
// The lights of the pixel's tile (Passes/LightCull.fx), or all of them without tile masks.
uint LocalLightMask(float2 pixel)
{
    uint count = (uint)LocalLightCount;
    uint all = count >= 32 ? 0xffffffffu : (1u << count) - 1u;
    [branch]
    if (TileParams.z < 0.5)
    {
        return all;
    }

    uint2 tile = (uint2)(pixel * TileParams.y);
    return TileLightMasks[tile.y * (uint)TileParams.x + tile.x] & all;
}

void AddLocalLights(float3 worldPosition, float3 n, float3 v, float3 albedo, float3 f0, float metallic, float a,
                    float2 pixel, inout Lighting lighting)
{
    uint transients = (uint)TransientLightCount;
    uint mask = LocalLightMask(pixel);
    [loop]
    while (mask != 0)
    {
        uint i = firstbitlow(mask);
        mask &= mask - 1u;

        float3 toLight = LocalLightPosition[i].xyz - worldPosition;
        float radius = LocalLightPosition[i].w;
        float distanceSquared = dot(toLight, toLight);
        [branch]
        if (radius <= 0.0 || distanceSquared >= radius * radius)
        {
            continue;
        }

        if (i < transients)
        {
            float falloff = saturate(1.0 - distanceSquared / (radius * radius));
            lighting.Diffuse += LocalLightColor[i].rgb * falloff * albedo * (1.0 - metallic);
            continue;
        }

        float distanceToLight = sqrt(distanceSquared);
        float3 l = toLight / max(distanceToLight, 1.0e-3);
        float ratio = distanceSquared / (radius * radius);
        float window = saturate(1.0 - ratio * ratio);
        float inverseSquare = LampReferenceDistance * LampReferenceDistance / max(distanceSquared, LampMinDistance * LampMinDistance);
        float3 light = LocalLightColor[i].rgb * (inverseSquare * window * window);
        // The lamps are large, soft sources: a quarter of their light wraps around to surfaces facing away (the legacy
        // lamp light's 0.25 + 0.75 n.l); the specular lobe keeps the exact n.l.
        float nl = saturate(dot(n, l));
        float row = LocalLightColor[i].w - 1.0;
        [branch]
        if (row >= 0.0)
        {
            light *= LampShadow((uint)row, -toLight, radius, nl);
        }

        float3 diffuseLight = light * (LampWrap + (1.0 - LampWrap) * nl);
        float3 unusedDiffuse = 0.0;
        AddDirectLight(l, light, n, v, albedo, f0, metallic, a, unusedDiffuse, lighting.Specular);
        lighting.Diffuse += (1.0 - F_SchlickRough(f0, saturate(dot(v, SafeNormalize(l + v))), sqrt(a))) * (1.0 - metallic) * albedo * diffuseLight;
    }
}

// Irradiance (E / pi) from the sky's spherical harmonics (Passes/Environment.fx), cosine-convolved per band.
float3 EnvironmentIrradiance(float3 n)
{
    float3 e = EnvironmentSh[0].rgb * 0.282095;
    e += (EnvironmentSh[1].rgb * n.y + EnvironmentSh[2].rgb * n.z + EnvironmentSh[3].rgb * n.x) * (0.488603 * 2.0 / 3.0);
    e += (EnvironmentSh[4].rgb * (1.092548 * n.x * n.y) + EnvironmentSh[5].rgb * (1.092548 * n.y * n.z)
        + EnvironmentSh[6].rgb * (0.315392 * (3.0 * n.z * n.z - 1.0)) + EnvironmentSh[7].rgb * (1.092548 * n.x * n.z)
        + EnvironmentSh[8].rgb * (0.546274 * (n.x * n.x - n.y * n.y))) * 0.25;
    return max(e, 0.0);
}

// One roughness level of the prefiltered atlas: level k is (base / 2^k) square at x = 2 base (1 - 2^-k).
float3 EnvironmentLevel(float2 octahedral, float level)
{
    float base = EnvironmentParams.y;
    float size = base * exp2(-level);
    float offset = 2.0 * base - 2.0 * size;
    float2 pixel = clamp(octahedral * size, 0.5, size - 0.5);
    return EnvironmentMap.SampleLevel(EnvironmentSampler, (float2(offset, 0.0) + pixel) / float2(EnvironmentParams.z, base), 0).rgb;
}

// Prefiltered radiance toward r for a perceptual roughness (levels at roughness k / (levels - 1)), in the light model's
// units (the atlas's scale, EnvironmentSh[9].x).
float3 EnvironmentSpecular(float3 r, float roughness)
{
    float level = saturate(roughness) * (EnvironmentParams.w - 1.0);
    float level0 = floor(level);
    float level1 = min(level0 + 1.0, EnvironmentParams.w - 1.0);
    float2 octahedral = OctahedralEncode(r);
    float3 radiance = lerp(EnvironmentLevel(octahedral, level0), EnvironmentLevel(octahedral, level1), level - level0);
    return radiance * EnvironmentSh[9].x;
}

// Image-based lighting: the sky's irradiance and its prefiltered radiance with the split-sum BRDF (Karis 2013), the
// multiple-scattering energy of rough metals restored (Fdez-Aguera 2019), occluded by the ambient occlusion. Without an
// environment, a hemispherical sky/ground ambient.
// ao occludes the sky's light, specularVisibility its reflections (ao and the authored light).
void AddAmbient(float3 n, float3 v, float3 albedo, float3 f0, float metallic, float roughness, float ao, float specularVisibility,
                inout Lighting lighting)
{
    float nvEnvironment = saturate(dot(n, v));
    [branch]
    if (EnvironmentParams.x > 0.5)
    {
        float2 brdf = BrdfLut.SampleLevel(BrdfLutSampler, float2(nvEnvironment, roughness), 0).rg;
        // F = f0 + (F90 - f0) Fc integrates to f0 A + F90 B, with the rough surface's grazing reflectance as F90.
        float3 single = f0 * brdf.x + GrazingReflectance(f0, roughness) * brdf.y;
        float3 energy = 1.0 + f0 * (1.0 / max(brdf.x + brdf.y, 1.0e-3) - 1.0);
        float3 r = reflect(-v, n);
        lighting.Diffuse += EnvironmentIrradiance(n) * albedo * ((1.0 - metallic) * ao);
        lighting.Specular += EnvironmentSpecular(r, roughness) * single * energy * SpecularOcclusion(nvEnvironment, specularVisibility, roughness);
        return;
    }

    float up = n.y * 0.5 + 0.5;
    float3 irradiance = lerp(GroundAmbient, SkyAmbient, up);
    float nv = saturate(dot(n, v));
    float3 fresnel = f0 + (max(1.0 - roughness, f0) - f0) * pow(1.0 - nv, 5.0);
    float3 reflected = reflect(-v, n);
    float3 environment = lerp(GroundAmbient, SkyAmbient, reflected.y * 0.5 + 0.5);
    lighting.Diffuse += irradiance * albedo * ((1.0 - metallic) * ao);
    lighting.Specular += environment * fresnel * SpecularOcclusion(nv, specularVisibility, roughness);
}

#endif
