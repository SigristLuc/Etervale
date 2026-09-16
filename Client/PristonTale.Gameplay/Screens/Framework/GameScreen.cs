using Microsoft.Xna.Framework;

namespace PristonTale.Gameplay.Screens.Framework;

public abstract class GameScreen : IDisposable
{
    protected GameScreen(ScreenContext context) => Context = context;

    protected ScreenContext Context { get; }

    public virtual string StatusText => string.Empty;

    public virtual void Enter() { }

    public abstract void Update(GameTime gameTime);

    public abstract void Draw();

    public virtual bool HandleEscape() => false;

    public virtual void Dispose() { }

    protected static string Fallback(string value, string ifEmpty) => string.IsNullOrWhiteSpace(value) ? ifEmpty : value;

    protected string AssetPath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(Context.AssetRoot, normalized);
    }
}
