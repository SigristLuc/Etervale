#include "Shared/PristonStaticColor.fxh"

float4 PS(PixelInput input) : COLOR0
{
    float4 albedo = tex2D(TextureSampler, input.TexCoord);
    // MULTIMIX: combine the slot1 layer over the base texture BEFORE vertex-colour/lighting (matches the
    // original's texture-stage combine). No-op for single-slot materials. UV0 is recovered by removing
    // slot0's scroll so slot1 scrolls independently.
    albedo.rgb = ApplyMultiMix(albedo.rgb, input.TexCoord - UvScrollOffset);
    albedo *= input.Color;
    return float4(albedo.rgb * ShadeStatic(input), albedo.a);
}

technique PristonOpaque
{
    pass P0
    {
        VertexShader = compile vs_3_0 VS();
        PixelShader = compile ps_3_0 PS();
    }
}
