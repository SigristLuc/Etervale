#define PRISTON_OPAQUE_DEPTH
#include "Shared/PristonStaticDepth.fxh"

float4 PS(PixelInput input) : COLOR0
{
    return PackDepth(input.Depth);
}

technique PristonDepthOnly
{
    pass P0
    {
        VertexShader = compile vs_3_0 VS();
        PixelShader = compile ps_3_0 PS();
    }
}
