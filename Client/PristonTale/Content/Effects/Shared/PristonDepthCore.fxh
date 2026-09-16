#ifndef PRISTON_DEPTHCORE_FXH
#define PRISTON_DEPTHCORE_FXH

// Shared core for the shadow-depth shaders. They render normalised light-space depth (z/w) into the
// red channel of an R32F target (only .r is meaningful; receivers sample .r for the comparison).
// The alpha-test variants additionally clip on the diffuse texture's alpha.
float4x4 View;
float4x4 Projection;

#ifndef PRISTON_OPAQUE_DEPTH
float AlphaCutoff;
float2 UvScrollOffset;
texture Texture;

sampler TextureSampler = sampler_state
{
    Texture = <Texture>;
    AddressU = Wrap; AddressV = Wrap;
    MinFilter = Linear; MagFilter = Linear; MipFilter = Linear;
};
#endif

float4 PackDepth(float2 depthZW)
{
    return float4(saturate(depthZW.x / depthZW.y), 0.0f, 0.0f, 1.0f);
}

#endif
