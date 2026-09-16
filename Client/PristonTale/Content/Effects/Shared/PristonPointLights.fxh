#ifndef PRISTON_POINTLIGHTS_FXH
#define PRISTON_POINTLIGHTS_FXH

// Transient combat lights come first; permanent lamps follow for skinned actors only.
// Static stage geometry receives only the transient prefix because its lamps are baked.
int PointLightCount;
int TransientLightCount;
float4 PointLightPositions[16]; // xyz = world pos, w = radius
float4 PointLightColors[16];    // rgb = colour, a = intensity

// Summed contribution of the active point lights at a world position. NOT shadowed by the sun
// (the directional shadow must not darken local lamps).
float3 AccumulatePointLights(float3 worldPos, float3 normal)
{
    float3 directLighting = float3(0.0f, 0.0f, 0.0f);
    for (int i = 0; i < 16; i++)
    {
        if (i >= PointLightCount)
        {
            break;
        }

        float3 toLight = PointLightPositions[i].xyz - worldPos;
        if (i < TransientLightCount)
        {
            // SetDynamicLight: radial contribution independent of the surface normal.
            float radiusSquared = PointLightPositions[i].w * PointLightPositions[i].w;
            float falloff = saturate(1.0f - dot(toLight, toLight) / max(radiusSquared, 0.001f));
            directLighting += PointLightColors[i].rgb * PointLightColors[i].a * falloff;
            continue;
        }

        float radius = PointLightPositions[i].w;
        float distanceSquared = dot(toLight, toLight);
        [branch]
        if (radius <= 0.0f || distanceSquared >= radius * radius)
        {
            continue;
        }

        float distanceToLight = sqrt(distanceSquared);
        float attenuation = saturate(1.0f - distanceToLight / radius);
        attenuation *= attenuation;
        float pointNdotL = saturate(dot(normal, toLight / max(distanceToLight, 0.001f)));
        directLighting += PointLightColors[i].rgb * PointLightColors[i].a * attenuation * (0.18f + pointNdotL * 0.82f);
    }
    return directLighting;
}

#endif
