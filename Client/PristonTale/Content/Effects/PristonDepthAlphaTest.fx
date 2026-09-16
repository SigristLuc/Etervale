#include "Shared/PristonStaticDepth.fxh"

float4 PS(PixelInput input) : COLOR0
{
    clip(tex2D(TextureSampler, input.TexCoord).a - AlphaCutoff);
    return PackDepth(input.Depth);
}

technique PristonDepthAlphaTest
{
    pass P0
    {
        VertexShader = compile vs_3_0 VS();
        PixelShader = compile ps_3_0 PS();
    }
}
