#define PRISTON_OPAQUE_DEPTH
#include "Shared/PristonSkinnedDepth.fxh"

float4 PS(PixelInput input) : COLOR0
{
    return PackDepth(input.Depth);
}

technique PristonSkinnedDepthOnly
{
    pass P0
    {
        VertexShader = compile vs_3_0 VS();
        PixelShader  = compile ps_3_0 PS();
    }
}
