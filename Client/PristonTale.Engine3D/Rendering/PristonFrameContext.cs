using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Engine3D.Rendering;

internal readonly record struct PristonFrameContext(
    Matrix View,
    Matrix Projection,
    Vector3 CameraPosition,
    uint RenderTime,
    PristonLighting Lighting,
    Matrix LightViewProjection,
    Texture2D ShadowMap,
    bool ShadowsEnabled,
    float LampFactor);
