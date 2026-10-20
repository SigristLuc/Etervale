using PristonTale.Simulation.Skills;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Gameplay.UI.Inventory;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Tests;

internal static class HudInteractionTests
{
    private static readonly Point[] Viewports =
    [
        new(800, 600), new(1024, 768), new(1280, 720), new(1600, 1200), new(1920, 1080), new(2560, 1440), new(3440, 1440), new(3840, 2160)
    ];

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("original skill shortcuts bind the held mouse side without selecting on release", ShortcutsAsync);
        yield return ("UI color keys preserve crimson, metal and opaque world pixels", ColorKeysAsync);
        yield return ("original HUD uses the largest integer scale and the 800x600 bottom/right anchors", LayoutAsync);
        yield return ("owner combat statistics survive the owner-status packet", OwnerStatsAsync);
        yield return ("original HUD hit rectangles stay inside their windows without overlapping", HitRectsAsync);
        yield return ("inventory drag uses original equipment slots and owner revision without changing local items", EquipmentDragAsync);
        yield return ("original gauges crop bitmaps from the top and follow the source formulas", GaugeAsync);
        yield return ("atlas gauge separation preserves the frame and masks every rectangular corner", AtlasGaugeAsync);
        yield return ("HUD overlays tolerate uninitialized and tiny resized viewports", OverlayAsync);
        yield return ("bottom windows slide with the original 70 Hz steps", SlideAsync);
        yield return ("minimap crops the field map around the player like the original", MinimapAsync);
    }

    private static Task OwnerStatsAsync()
    {
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Mechanician, "Stats", 1);
        var state = OwnerState.Capture(character) with { AttackRating = 173, MoveSpeed = 3 };
        var buffer = new byte[PristonTale.Protocol.Packets.OwnerStatusPacket.Size];
        PristonTale.Server.Net.WireMapping.Status(state).Write(buffer);
        var decoded = PristonTale.Protocol.Packets.OwnerStatusPacket.Read(buffer);
        Assert.True(decoded is { AttackRating: 173, MoveSpeed: 3 }, "online HUD lost authoritative derived values");
        return Task.CompletedTask;
    }

    private static Task LayoutAsync()
    {
        var expected = new Dictionary<Point, int>
        {
            [new Point(800, 600)] = 1, [new Point(1024, 768)] = 1, [new Point(1280, 720)] = 1, [new Point(1600, 1200)] = 2,
            [new Point(1920, 1080)] = 1, [new Point(2560, 1440)] = 2, [new Point(3440, 1440)] = 2, [new Point(3840, 2160)] = 3
        };
        foreach (var size in Viewports)
        {
            var layout = HudLayout.Create(size.X, size.Y);
            Assert.True(layout.Scale == expected[size], $"wrong integer scale for {size}");
            Assert.True(layout.Width == size.X / layout.Scale && layout.Height == size.Y / layout.Scale, "logical size is not the integer division");
            Assert.True(layout.Width >= HudLayout.DesignWidth && layout.Height >= HudLayout.DesignHeight, "scale shrank the 800x600 design space");
            var viewport = new Rectangle(0, 0, layout.Width, layout.Height);
            foreach (var button in Enum.GetValues<HudMenuButton>())
            {
                var bounds = layout.Bottom(HudBar.MenuButton(button));
                Assert.True(viewport.Contains(bounds) && bounds.Bottom <= layout.Height && bounds.X < HudLayout.DesignWidth,
                    "menu button is not bottom-left anchored");
            }

            var minimap = HudMinimap.Box(layout);
            Assert.True(viewport.Contains(minimap) && minimap.Right == layout.Width - 16 && minimap.Bottom == layout.Height - 46,
                "minimap is not bottom-right anchored");
            var window = HudInventoryLayout.Window(HudInventoryLayout.Origin(layout));
            Assert.True(window.Bottom == layout.Height && window.X == 0, "bottom windows are not bottom-left anchored");
            Assert.True(viewport.Contains(HudCombat.TargetBox(layout)), "target box escaped the viewport");
            var scaled = new Point(minimap.Center.X * layout.Scale + layout.Scale - 1, minimap.Center.Y * layout.Scale);
            Assert.True(layout.ToLogical(scaled) == minimap.Center, "scaled pointer does not map back to logical pixels");
        }

        return Task.CompletedTask;
    }

    private static Task HitRectsAsync()
    {
        var menu2 = new Rectangle(544, 536, 256, 64);
        var buttons = Enum.GetValues<HudMenuButton>().Select(HudBar.MenuButton).ToArray();
        var toggles = Enum.GetValues<HudToggle>().Select(HudBar.Toggle).ToArray();
        AssertDisjointInside(buttons, menu2, "menu buttons");
        AssertDisjointInside(toggles, menu2, "walk/camera/map toggles");
        Assert.True(!buttons.Any(button => toggles.Any(button.Intersects)), "toggles overlap menu buttons");
        Assert.True(!HudBar.Socket(false).Intersects(HudBar.Socket(true)), "skill sockets overlap");
        Assert.True(HudBar.Body.Any(body => body.Contains(HudBar.Socket(false))) && HudBar.Body.Any(body => body.Contains(HudBar.Socket(true))),
            "skill sockets are not pointer-captured");

        var origin = Point.Zero;
        var window = HudInventoryLayout.Window(origin);
        var bag = HudInventoryLayout.Bag(origin);
        Assert.True(window.Contains(bag) && bag.Width == 12 * 22 && bag.Height == 6 * 22, "bag grid differs from the original inventory origin");
        AssertDisjointInside(Enumerable.Range(0, 10).Select(slot => HudInventoryLayout.Equipment(origin, slot)).Append(bag).ToArray(), window,
            "equipment slots and bag");
        Assert.True(HudInventoryLayout.Equipment(origin, (int)EquipmentSlot.Weapon) == new Rectangle(575, 434, 65, 110), "weapon slot moved");
        Assert.True(HudInventoryLayout.CellAt(origin, new Point(23 + 22 * 11 + 3, 436 + 22 * 5 + 3)) == 71, "last bag cell not addressed");
        Assert.True(HudInventoryLayout.CellAt(origin, new Point(22, 436)) == -1, "pointer outside bag picked a cell");
        Assert.True(window.Contains(HudInventoryLayout.Close(origin)) && !HudInventoryLayout.Close(origin).Intersects(bag), "inventory close misplaced");

        // sSkillBox rectangles overlap at their transparent hexagon corners; every centre must hit only its own node.
        var nodes = Enumerable.Range(0, HudSkillTreeLayout.NodeCount).Select(index => HudSkillTreeLayout.Node(origin, index))
            .Append(HudSkillTreeLayout.BasicAttack(origin)).ToArray();
        foreach (var node in nodes)
        {
            Assert.True(window.Contains(node) && nodes.Count(other => other.Contains(node.Center)) == 1, "skill node escaped or is covered");
        }

        nodes = nodes[..HudSkillTreeLayout.NodeCount];
        for (var index = 0; index < nodes.Length; index++)
        {
            var gauge = HudSkillTreeLayout.RankGauge(origin, index);
            Assert.True(window.Contains(gauge) && gauge.Left > nodes[index].Left, "skill rank gauge escaped the window");
        }

        var allocate = Enumerable.Range(0, 5).Select(index => HudStatusPanel.AllocateButton(origin, index)).ToArray();
        AssertDisjointInside(allocate.Append(HudStatusPanel.Close(origin)).ToArray(), window, "status buttons");
        Assert.True(allocate[0] == new Rectangle(566, 432, 18, 17), "strength arrow differs from PointButton");

        var entries = Enum.GetValues<HudSystemEntry>().Select(HudSystemMenu.Entry).ToArray();
        AssertDisjointInside(entries, HudSystemMenu.Bounds, "system menu entries");
        Assert.True(!buttons.Any(HudSystemMenu.Bounds.Intersects), "system menu covers the menu buttons");

        var design = new Rectangle(0, 0, HudLayout.DesignWidth, HudLayout.DesignHeight);
        Assert.True(design.Contains(HudNpcPanel.Box) && HudNpcPanel.Box.Contains(HudNpcPanel.OkButton) && HudNpcPanel.Box.Contains(HudNpcPanel.Title),
            "NPC help box escaped 800x600");
        Assert.True(HudNpcPanel.Box == new Rectangle(50, 45, 352, 352), "NPC help box differs from QuestMessageBoxPosi2/Size2");
        Assert.True(design.Contains(HudSettingsPanel.Box) && HudSettingsPanel.Box.Contains(HudSettingsPanel.OkButton)
            && HudSettingsPanel.Box.Contains(HudSettingsPanel.Row(15)), "settings rows escaped the help box");
        AssertDisjointInside([HudMessageBox.Accept, HudMessageBox.Reject], HudMessageBox.Bounds, "message box buttons");
        var layout = HudLayout.Create(800, 600);
        Assert.True(HudNpcPanel.SkillMasterBox(layout).Contains(HudNpcPanel.TrainingButton(layout))
            && HudNpcPanel.SkillMasterBox(layout).Contains(HudNpcPanel.ExitButton(layout)), "skill master buttons escaped ReStartMain");
        return Task.CompletedTask;
    }

    private static void AssertDisjointInside(Rectangle[] rectangles, Rectangle container, string name)
    {
        for (var index = 0; index < rectangles.Length; index++)
        {
            Assert.True(container.Contains(rectangles[index]) && rectangles[index].Width > 0 && rectangles[index].Height > 0, $"{name} escaped");
            for (var other = index + 1; other < rectangles.Length; other++)
            {
                Assert.True(!rectangles[index].Intersects(rectangles[other]), $"{name} overlap");
            }
        }
    }

    private static Task EquipmentDragAsync()
    {
        foreach (var size in new[] { new Point(800, 600), new Point(1600, 1200), new Point(1920, 1080) })
        {
            var layout = HudLayout.Create(size.X, size.Y);
            var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Mechanician, "Inventory");
            var state = OwnerState.Capture(character);
            Assert.True(state.AttackRating > 0 && state.MoveSpeed > 0, "owner projection omitted derived combat statistics");
            Assert.True(ReferenceEquals(state, OwnerState.Capture(character, state)), "unchanged derived values invalidated owner cache");
            var item = new OwnedItemView(Guid.NewGuid(), 1, "Weapon", ItemKind.Axe, 1, 0, -1, 2, 3);
            state = state with { Items = new[] { item }, InventoryRevision = 7 };
            InventoryCommand? sent = null;
            var controller = new HudInventoryController(null!, command =>
            {
                sent = command;
                return Task.FromResult(new InventoryOutcome(true, 8, ""));
            });
            var origin = HudInventoryLayout.Origin(layout);
            var pointer = HudInventoryLayout.Bag(origin).Location + new Point(10, 10);
            controller.Update(state, layout, true, default, MouseAt(pointer, layout.Scale, ButtonState.Pressed));
            Assert.True(controller.Dragging == item.InstanceId, "bag item was not picked up");
            var weapon = HudInventoryLayout.Equipment(origin, (int)EquipmentSlot.Weapon).Center;
            controller.Update(state, layout, true, default, MouseAt(weapon, layout.Scale, ButtonState.Released));
            Assert.True(sent is { Action: InventoryAction.Equip, Slot: EquipmentSlot.Weapon, ExpectedRevision: 7 }
                && sent.ItemId == item.InstanceId && sent.OperationId != Guid.Empty, "drop did not preserve the authoritative equipment command");
            Assert.True(state.Items[0].Slot == -1 && controller.Dragging == Guid.Empty, "gesture changed immutable owner state or retained drag");
        }

        return Task.CompletedTask;
    }

    private static MouseState MouseAt(Point logical, int scale, ButtonState left) => new(logical.X * scale, logical.Y * scale, 0,
        left, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);

    private static Task GaugeAsync()
    {
        Assert.True(HudBar.Length(HudBar.LifeLength, 50, 100) == 47 && HudBar.Length(HudBar.LifeLength, 100, 100) == 94, "life length differs");
        Assert.True(HudBar.Length(HudBar.StaminaLength, 1, 0) == 0 && HudBar.Length(HudBar.ManaLength, -5, 10) == 0, "empty maximum filled a gauge");
        Assert.True(HudBar.Length(HudBar.ManaLength, 500, 100) == 94, "overfilled gauge escaped its bitmap");
        var half = HudBar.GaugeSource(16, HudBar.LifeLength, 47);
        Assert.True(half == new Rectangle(0, 47, 16, 47) && half.Bottom == HudBar.LifeLength, "gauge is not cropped from the top");
        Assert.True(HudBar.GaugeSource(8, HudBar.StaminaLength, 0).Height == 0, "empty gauge drew rows");
        Assert.True(HudBar.TimeGauge(4) == (true, 0) && HudBar.TimeGauge(13.5f) == (true, 23) && HudBar.TimeGauge(22.9f) == (true, 47),
            "day gauge differs from GetWordTimeDisplay");
        Assert.True(HudBar.TimeGauge(23) == (false, 0) && HudBar.TimeGauge(1) == (false, 20) && HudBar.TimeGauge(3) == (false, 40),
            "night gauge differs");
        Assert.True(HudBar.TimeGauge(float.NaN).Day == false && HudBar.TimeGauge(-1).Day == false, "invalid hour was not normalized");
        var owner = OwnerState.Capture(new CharacterClassRegistry().CreatePlayer(PlayerClass.Mechanician, "Exp"));
        Assert.True(HudBar.ExperienceFraction(owner with { Experience = owner.LevelStart }) == 0, "level start shows experience");
        Assert.True(HudBar.ExperienceFraction(owner with { Experience = owner.NextLevel + 10 }) == 1, "experience escaped its bar");
        Assert.True(HudCombat.StateColor(0, 10) == new Color(255, 0, 0) && HudCombat.StateColor(10, 10) == new Color(1, 255, 0),
            "target bar color differs from DrawStateBar");
        return Task.CompletedTask;
    }

    private static Task SlideAsync()
    {
        var slide = new HudSlide { Open = true };
        slide.Advance(1.0 / 70);
        Assert.True(slide.Move == 184 && !slide.FullyOpen && slide.Visible, "opening did not jump to 184");
        slide.Advance(1.0 / 70);
        Assert.True(slide.Move == 191, "opening step differs");
        slide.Advance(1);
        Assert.True(slide.FullyOpen && slide.Offset == 0, "window did not reach its open position");
        slide.Open = false;
        slide.Advance(1.0 / 70);
        Assert.True(slide.Move == 240 && !slide.FullyOpen, "closing step differs");
        slide.Advance(1);
        Assert.True(!slide.Visible && slide.Offset == HudSlide.Full, "window did not close");
        return Task.CompletedTask;
    }

    private static Task MinimapAsync()
    {
        // A 10240x10240 world-unit stage in fixed point; the player stands in the middle.
        var rectangle = new PristonIntRectangle(-5120 * 256, -5120 * 256, 5120 * 256, 5120 * 256);
        Assert.True(HudMinimap.MapRegion(rectangle, Vector3.Zero, false, out var source, out var destination), "centered map was rejected");
        var span = 2 * HudMinimap.ViewSize(false) / 10240f;
        Assert.True(MathF.Abs(source.Z - span) < 1e-4f && MathF.Abs(source.X - (0.5f - span / 2)) < 1e-4f, "map source span differs");
        Assert.True(destination == new Vector4(0, 0, 126, 126), "unclipped destination is not the 126 pixel interior");
        Assert.True(HudMinimap.MapRegion(rectangle, new Vector3(-5120, 0, 0), false, out source, out destination)
            && source.X == 0 && MathF.Abs(destination.X - 64) < 0.01f && MathF.Abs(destination.Z - 62) < 0.01f, "left edge was not clipped");
        Assert.True(!HudMinimap.MapRegion(rectangle, new Vector3(20000, 0, 0), false, out _, out _), "map outside the stage was drawn");
        Assert.True(!HudMinimap.MapRegion(default, Vector3.Zero, false, out _, out _), "empty stage rectangle was drawn");
        // Engine Z is the mirrored stage Z: walking towards -Z (north) moves up the map.
        var north = HudMinimap.MapPoint(rectangle, new Vector3(0, 0, -100)).Y;
        Assert.True(north < HudMinimap.MapPoint(rectangle, Vector3.Zero).Y, "map vertical axis is mirrored");
        Assert.True(MathF.Abs(HudMinimap.ArrowRotation(-Vector3.UnitZ) - MathF.PI) < 1e-5f, "north heading does not flip the arrow bitmap");
        return Task.CompletedTask;
    }

    private static Task OverlayAsync()
    {
        var empty = default(HudLayout).PlaceOverlay(new Point(-170, -47), 340, 37);
        Assert.True(empty == new Rectangle(0, 0, 1, 1), "empty viewport generated invalid clamp bounds");
        foreach (var (width, height) in new[] { (0, 0), (1, 1), (32, 24), (160, 90), (800, 600), (1280, 720), (3840, 2160) })
        {
            var layout = HudLayout.Create(width, height);
            Assert.True(layout.Scale >= 1 && layout.Width >= 1 && layout.Height >= 1, "viewport generated an invalid UI transform");
            var viewport = new Rectangle(0, 0, layout.Width, layout.Height);
            foreach (var point in new[] { new Point(-1000, -1000), new Point(100000, 100000) })
            {
                var overlay = layout.PlaceOverlay(point, 340, 1000);
                Assert.True(viewport.Contains(overlay) && overlay.Width > 0 && overlay.Height > 0, "overlay escaped the resized viewport");
            }

            Assert.True(layout.ToLogical(new Point(-1, -1)) == new Point(-1, -1), "negative pointer rounded into the viewport");
        }

        return Task.CompletedTask;
    }

    private static Task ShortcutsAsync()
    {
        var bindings = new HudSkillBindings();
        var basic = HudSkillBindings.BasicAttackId;
        Assert.True(bindings.LeftSkillId == basic && bindings.RightSkillId == basic, "sockets did not start with the normal attack");
        var rightDown = Mouse(right: ButtonState.Pressed);
        bindings.Update(default, rightDown, basic);
        bindings.Update(new KeyboardState(Keys.F5), rightDown, basic);
        Assert.True(bindings.Shortcut(4) == new HudSkillShortcut(basic, SkillMouseUse.Right), "F5 did not bind the held right button");
        bindings.Update(default, default, basic);
        Assert.True(bindings.RightSkillId == basic, "release after binding changed the right socket");
        bindings.Update(new KeyboardState(Keys.F5), default, 0);
        Assert.True(bindings.RightSkillId == basic && bindings.LeftSkillId == basic, "hotkey did not select the recorded side");

        Assert.True(bindings.Assign(0, basic, SkillMouseUse.Left), "valid reassignment rejected");
        Assert.True(bindings.Shortcut(4).SkillId == 0, "old shortcut remained after rebinding the same skill");
        Assert.True(!bindings.Assign(8, basic, SkillMouseUse.Left), "F9 outside original shortcut range accepted");
        Assert.True(!bindings.Select(42, SkillMouseUse.Right), "unknown skill selected");
        Assert.True(!bindings.Select(basic, SkillMouseUse.Both), "ambiguous mouse side accepted");

        var direct = new HudSkillBindings();
        direct.Update(default, rightDown, basic);
        direct.Update(default, default, basic);
        Assert.True(direct.RightSkillId == basic, "right release on a skill did not select it");
        var draggedAway = new HudSkillBindings();
        draggedAway.Update(default, rightDown, basic);
        draggedAway.Update(default, default, 0);
        Assert.True(draggedAway.RightSkillId == basic, "release outside the skill row changed the right socket");

        var debug = new HudSkillBindings();
        debug.Update(default, rightDown, basic);
        debug.Update(new KeyboardState(Keys.LeftControl, Keys.F5), rightDown, basic);
        Assert.True(debug.Shortcut(4).SkillId == 0, "debug chord overwrote a skill shortcut");
        return Task.CompletedTask;
    }

    private static Task ColorKeysAsync()
    {
        var colors = new[] { Color.Magenta, Color.Black, new Color(240, 20, 25), new Color(180, 175, 170), new Color(240, 5, 235) };
        var image = new DecodedPristonImage(colors.Length, 1, colors, false);
        PristonUiPixels.Prepare(image, PristonUiColorKey.Magenta);
        Assert.True(colors[0] == Color.Transparent && colors[1] == Color.Black, "matte removed the dark interior or remained opaque");
        Assert.True(colors[2] == new Color(240, 20, 25) && colors[3] == new Color(180, 175, 170), "crimson or silver changed");
        Assert.True(colors[4].R <= colors[4].A && colors[4].B <= colors[4].A, "edge pixels are not premultiplied");

        Assert.True(HudSprites.ColorKeyFor(".BMP") == PristonUiColorKey.Black && HudSprites.ColorKeyFor(".tga") == PristonUiColorKey.None,
            "Sinimage bitmaps are not black keyed or TGA files lost their alpha");
        var bitmap = new DecodedPristonImage(3, 1, [Color.Black, new Color(1, 0, 0), Color.White], false);
        PristonUiPixels.Prepare(bitmap, HudSprites.ColorKeyFor(".bmp"));
        Assert.True(bitmap.Pixels[0] == Color.Transparent && bitmap.Pixels[1] == new Color(1, 0, 0) && bitmap.Pixels[2] == Color.White,
            "black key is not exact RGB 0,0,0");
        var targa = new DecodedPristonImage(2, 1, [new Color(200, 100, 50, 128), new Color(0, 0, 0, 255)], true);
        PristonUiPixels.Prepare(targa, HudSprites.ColorKeyFor(".tga"));
        Assert.True(targa.Pixels[0] == new Color(100, 50, 25, 128) && targa.Pixels[1] == Color.Black, "TGA alpha is not premultiplied or black was keyed");
        return Task.CompletedTask;
    }

    private static Task AtlasGaugeAsync()
    {
        var original = new Color(220, 40, 30);
        var frame = new DecodedPristonImage(24, 24, Enumerable.Repeat(original, 24 * 24).ToArray(), false);
        var bounds = new Rectangle(4, 4, 16, 16);
        var fill = HudGaugePixels.Separate(frame, bounds);
        Assert.True(fill.Pixels[0] == Color.Transparent && fill.Pixels[15] == Color.Transparent
            && fill.Pixels[240] == Color.Transparent && fill.Pixels[255] == Color.Transparent, "rectangular corners remained visible");
        Assert.True(fill.Pixels[8 * 16 + 8] == original, "filled gauge changed the original liquid");
        Assert.True(frame.Pixels[12 * 24 + 12].R < 100, "empty frame still contains the bright liquid");
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                if (!bounds.Contains(x, y) || fill.Pixels[(y - bounds.Y) * 16 + x - bounds.X].A == 0)
                {
                    Assert.True(frame.Pixels[y * 24 + x] == original, "mask darkened the surrounding frame");
                }
            }
        }

        var cropped = HudGaugePixels.Crop(frame, bounds);
        Assert.True(cropped.Width == 16 && cropped.Height == 16, "crop retained unused margins");
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                Assert.True(cropped.Pixels[y * 16 + x] == frame.Pixels[(y + 4) * 24 + x + 4], "crop changed atlas pixels");
            }
        }

        var transparent = new DecodedPristonImage(4, 4, new Color[16], true);
        var transparentFill = HudGaugePixels.Separate(transparent, new Rectangle(0, 0, 4, 4));
        Assert.True(transparent.Pixels.All(pixel => pixel == Color.Transparent)
            && transparentFill.Pixels.All(pixel => pixel == Color.Transparent), "empty pixels lost premultiplied alpha");

        return Task.CompletedTask;
    }

    private static MouseState Mouse(ButtonState right)
    {
        return new MouseState(0, 0, 0, ButtonState.Released, ButtonState.Released, right, ButtonState.Released, ButtonState.Released);
    }
}
