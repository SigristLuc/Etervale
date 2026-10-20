using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Client.Net.Presence;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Simulation.World.Loot;

namespace PristonTale.Gameplay.World.Remote;

public delegate bool FloorHeightQuery(float x, float z, out float y);

public sealed class GroundLootPresentation(IActorScene scene, PristonActorLoader loader, string root, IClientLog log) : IDisposable
{
    private readonly Dictionary<Guid, Entry> _entries = new();
    private readonly Dictionary<string, Task<PristonActor>> _prepared = new(StringComparer.OrdinalIgnoreCase);
    private int _floorRevision;
    private Guid _hovered;
    private readonly GroundLootLabelLayout _labels = new();

    public void InvalidateFloor() => _floorRevision++;

    public void Apply(PresenceUpdate update)
    {
        foreach (var id in update.GroundRemoved)
        {
            if (_entries.Remove(id, out var entry) && entry.Handle.IsValid)
            {
                scene.RemoveActor(entry.Handle);
            }
        }

        foreach (var drop in update.GroundAdded)
        {
            if (_entries.ContainsKey(drop.Id))
            {
                continue;
            }

            if (!_prepared.TryGetValue(drop.CatalogKey, out var task))
            {
                task = PrepareAsync(drop.CatalogKey);
                _prepared.Add(drop.CatalogKey, task);
                _ = ObserveAsync(task);
            }

            _entries.Add(drop.Id, new Entry(drop, task));
        }
    }

    private Task<PristonActor> PrepareAsync(string key) => Task.Run(async () =>
    {
        var actor = loader.LoadRigid(Path.Combine(root, EquipmentVisuals.MeshPath(key)));
        await scene.PrepareActorResourcesAsync(actor).ConfigureAwait(false);
        return actor;
    });

    private async Task ObserveAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            log.Error($"[Loot] Resource preparation failed: {exception.Message}");
        }
    }

    public void Update(FloorHeightQuery floor)
    {
        int uploads = 0;
        foreach (var entry in _entries.Values)
        {
            if (entry.FloorRevision != _floorRevision)
            {
                var source = entry.Drop.Position;
                entry.FloorFound = floor(source.X, -source.Z, out float height);
                entry.Position = new Vector3(source.X, height, -source.Z);
                entry.FloorRevision = _floorRevision;
            }

            if (!entry.FloorFound)
            {
                if (entry.Handle.IsValid)
                {
                    scene.RemoveActor(entry.Handle);
                    entry.Handle = ActorHandle.None;
                }

                entry.LabelBounds = Rectangle.Empty;
                continue; // Never publish a mesh at placeholder Y=0 before its stage is resident.
            }

            if (!entry.Handle.IsValid && !entry.PublishFailed && entry.Preparation.IsCompletedSuccessfully && uploads < 1)
            {
                var actor = entry.Preparation.Result;
                entry.Offset = -actor.Mesh.Bounds.Min.Y;
                try
                {
                    entry.Handle = scene.AddActor(new PristonActorInstance(actor, World(entry), PristonActorMotion.Stand));
                }
                catch (Exception exception)
                {
                    entry.PublishFailed = true;
                    log.Error($"[Loot] Model publication failed: {exception.Message}");
                }

                uploads++;
            }

            if (entry.Handle.IsValid)
            {
                scene.SetActorWorld(entry.Handle, World(entry));
            }
        }
    }

    private static Matrix World(Entry entry) => Matrix.CreateTranslation(entry.Position + Vector3.Up * (entry.Offset + 0.5f));

    public bool TryGet(Guid id, out Vector3 position)
    {
        if (_entries.TryGetValue(id, out var entry) && entry.FloorFound)
        {
            position = entry.Position;
            return true;
        }

        position = default;
        return false;
    }

    public bool Pick(Point cursor, Ray ray, out Guid id, out Vector3 position, bool showAllLabels = false)
    {
        id = Guid.Empty;
        position = default;
        float nearest = float.MaxValue;
        foreach (var entry in _entries.Values)
        {
            if (!entry.FloorFound)
            {
                continue;
            }

            if ((showAllLabels || entry.Drop.Id == _hovered) && entry.LabelBounds.Contains(cursor))
            {
                id = entry.Drop.Id;
                position = entry.Position;
                return true;
            }

            var hit = ray.Intersects(new BoundingSphere(entry.Position + Vector3.Up * 7, 12));
            if (hit.HasValue && hit.Value < nearest)
            {
                nearest = hit.Value;
                id = entry.Drop.Id;
                position = entry.Position;
            }
        }

        return id != Guid.Empty;
    }

    public void DrawLabels(SpriteBatch batch, SpriteFont font, Viewport viewport, PristonCamera camera, Guid hovered = default, bool showAll = false)
    {
        _hovered = hovered;
        _labels.Begin(viewport.Bounds, font.LineSpacing + 4);
        foreach (var entry in _entries.Values)
        {
            entry.LabelBounds = Rectangle.Empty;
            if (!entry.FloorFound || (!showAll && entry.Drop.Id != hovered))
            {
                continue;
            }

            float depth = -Vector3.Transform(entry.Position, camera.View).Z;
            if (entry.Drop.Id != hovered && (depth < 32 || depth >= 768))
            {
                continue;
            }

            var point = viewport.Project(entry.Position + Vector3.Up * 16, camera.Projection, camera.View, Matrix.Identity);
            if (point.Z < 0 || point.Z > 1 || point.X < 0 || point.Y < 0 || point.X > viewport.Width || point.Y > viewport.Height)
            {
                continue;
            }

            var size = font.MeasureString(entry.Label);
            if (!_labels.TryPlace(new Vector2(point.X, point.Y), (int)MathF.Ceiling(size.X) + 8, out var bounds))
            {
                continue;
            }

            entry.LabelBounds = bounds;
            var position = new Vector2(bounds.X + 4, bounds.Y + 2);
            batch.DrawString(font, entry.Label, position + Vector2.One, Color.Black);
            var color = entry.Drop.Id == hovered ? new Color(180, 180, 180) : new Color(110, 110, 110);
            batch.DrawString(font, entry.Label, position, color);
        }
    }

    public void Dispose()
    {
        foreach (var entry in _entries.Values)
        {
            if (entry.Handle.IsValid)
            {
                scene.RemoveActor(entry.Handle);
            }
        }

        _entries.Clear();
        _prepared.Clear();
    }

    private sealed class Entry(GroundDropView drop, Task<PristonActor> preparation)
    {
        public GroundDropView Drop { get; } = drop;
        public Task<PristonActor> Preparation { get; } = preparation;
        public ActorHandle Handle { get; set; } = ActorHandle.None;
        public Vector3 Position { get; set; }
        public float Offset { get; set; }
        public Rectangle LabelBounds { get; set; }
        public int FloorRevision { get; set; } = -1;
        public bool FloorFound { get; set; }
        public bool PublishFailed { get; set; }
        public string Label { get; } = drop.Gold ? $"{drop.Amount} Gold" : drop.Name;
    }
}
