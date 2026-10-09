using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.UI.Inventory;

public sealed class HudItemIcons : IDisposable
{
    private readonly HudIconCache _cache;

    public HudItemIcons(GraphicsDevice device, string assetRoot, IClientLog log)
    {
        _cache = new HudIconCache(device, assetRoot, log);
    }

    public Texture2D Find(string key) => _cache.Find(key);
    public bool IsIdle => _cache.IsIdle;
    public bool Failed => _cache.Failed;
    public void Request(IReadOnlyList<OwnedItemView> items) => _cache.Request(items.Select(item => item.CatalogKey));
    public void Update() => _cache.Update();
    public void Dispose() => _cache.Dispose();
}
