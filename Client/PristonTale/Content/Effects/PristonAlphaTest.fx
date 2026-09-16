#include "Shared/PristonStaticColor.fxh"

float AlphaCutoff;

float4 PS(PixelInput input) : COLOR0
{
    // Sample with a -1.0 LOD bias (toward the sharp mip) so the hard alpha cutoff reads the
    // high-coverage alpha — matches the reference's texture2D(uTexture, uv, -1.0). Without it,
    // box-averaged alpha mips erode the leaves below the cutoff at distance (sparse foliage).
    float4 albedo = tex2Dbias(TextureSampler, float4(input.TexCoord, 0.0f, -1.0f));
    // MULTIMIX slot1 over the base (no-op for single-slot). The alpha cutout stays slot0-driven (the
    // original's slot1 ADD only affects colour), so combine after the clip's alpha is taken from slot0.
    clip(albedo.a * input.Color.a - AlphaCutoff);
    albedo.rgb = ApplyMultiMix(albedo.rgb, input.TexCoord - UvScrollOffset);
    albedo *= input.Color;
    return float4(albedo.rgb * ShadeStatic(input), albedo.a);
}

technique PristonAlphaTest
{
    pass P0
    {
        VertexShader = compile vs_3_0 VS();
        PixelShader = compile ps_3_0 PS();
    }
}
