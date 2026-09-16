#include "Shared/PristonSkinnedColor.fxh"

float AlphaCutoff;

float4 PS(PixelInput input) : COLOR0
{
    float4 albedo = tex2D(TextureSampler, input.TexCoord);
    clip(albedo.a - AlphaCutoff);
    float3 selectedAlbedo = saturate(albedo.rgb + ActorColorAdd);
    return float4(selectedAlbedo * ShadeSkinned(input), albedo.a);
}

technique PristonSkinnedAlphaTest
{
    pass P0
    {
        VertexShader = compile vs_3_0 VS();
        PixelShader  = compile ps_3_0 PS();
    }
}
