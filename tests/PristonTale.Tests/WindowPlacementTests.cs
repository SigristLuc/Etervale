using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Tests;

internal static class WindowPlacementTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a window without a saved position opens centred on the work area with its title bar on screen", CenteredAsync);
        yield return ("the window's last position and maximized state survive close and reopen", PersistenceAsync);
    }

    private static Task CenteredAsync()
    {
        var work = new Rectangle(0, 0, 1920, 1032);
        Assert.True(WindowPlacement.Centered(new Rectangle(552, 269, 816, 519), work) == new Point(552, 256),
            "a window smaller than the work area is not centred on it");
        // The saved 1920x1009 client of a maximized window, restored with its frame: taller than the work area.
        Assert.True(WindowPlacement.Centered(new Rectangle(552, 269, 1922, 1041), work) == new Point(-1, 0),
            "a window taller than the work area left its title bar off the top or is not centred across");
        var second = new Rectangle(1920, 48, 2560, 1392);
        Assert.True(WindowPlacement.Centered(new Rectangle(0, 0, 1280, 720), second) == new Point(2560, 384),
            "the work area's origin (a second monitor, a taskbar on top) is ignored");
        return Task.CompletedTask;
    }

    private static async Task PersistenceAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "priston-placement-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "client-settings.json");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, """{ "version": 1, "windowWidth": 1920, "windowHeight": 1009 }""");
            var store = new ClientSettingsStore(path, NullClientLog.Instance);
            var older = store.Load();
            Assert.True(older.WindowX is null && older.WindowY is null && !older.WindowMaximized,
                "a settings document without a window position does not open centred");
            var placed = older with { WindowX = -1200, WindowY = 140, WindowMaximized = true };
            Assert.True(placed.HasSameDisplay(older), "moving or maximizing the window counts as a display change");
            Assert.True(store.QueueSave(placed), "the window placement was rejected by the persistence queue");
            await store.CompleteAsync();
            var reopened = new ClientSettingsStore(path, NullClientLog.Instance);
            Assert.True(reopened.Load() == placed, "the window position or maximized state was lost on close/reopen");
            await reopened.CompleteAsync();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
