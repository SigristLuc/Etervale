#ifndef PRISTON_SKINNING_FXH
#define PRISTON_SKINNING_FXH

// Each affine bone stores three transposed rows so 64 bones fit the vs_3_0 register budget.
// C# uploads matrix columns 1–3 into these rows.
float4 BonePalette[192];

// Row dot-products reconstruct (position · boneMatrix).xyz including translation.
float3 SkinPosition(float4 position, int bone)
{
    int b = bone * 3;
    return float3(dot(BonePalette[b], position),
                  dot(BonePalette[b + 1], position),
                  dot(BonePalette[b + 2], position));
}

// World normal: the upper 3x3 (rotation) part only. Uniform-scale bones don't need the inverse-transpose.
float3 SkinNormal(float3 normal, int bone)
{
    int b = bone * 3;
    return float3(dot(BonePalette[b].xyz, normal),
                  dot(BonePalette[b + 1].xyz, normal),
                  dot(BonePalette[b + 2].xyz, normal));
}

#endif
