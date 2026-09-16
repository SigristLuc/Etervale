using Microsoft.Xna.Framework;
using PristonTale.Gameplay.World.Remote;

namespace PristonTale.Tests;

internal static class GroundLootLabelTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("distinct ground drops share an anchor without overlapping their label hit targets", LayoutAsync);
    }

    private static Task LayoutAsync()
    {
        var layout = new GroundLootLabelLayout();
        var viewport = new Rectangle(10, 20, 320, 160);
        layout.Begin(viewport, 20);
        var occupied = new List<Rectangle>();
        for (int index = 0; index < 8; index++)
        {
            Assert.True(layout.TryPlace(new Vector2(170, 170), 80, out var bounds), "overlapping drop lost its label despite available rows");
            Assert.True(viewport.Contains(bounds), "drop label escaped viewport");
            Assert.True(occupied.All(previous => !previous.Intersects(bounds)), "two drops share a clickable label rectangle");
            occupied.Add(bounds);
        }

        Assert.True(!layout.TryPlace(new Vector2(170, 170), 80, out _), "full column placed an overlapping label");
        layout.Begin(new Rectangle(0, 0, 1, 1), 20);
        Assert.True(!layout.TryPlace(Vector2.Zero, 80, out _), "minimized viewport produced invalid label bounds");
        layout.Begin(viewport, 20);
        Assert.True(layout.TryPlace(new Vector2(-50, -50), 80, out var resized) && viewport.Contains(resized), "resize retained stale occupied cells");
        return Task.CompletedTask;
    }
}
