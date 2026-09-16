using PristonTale.Simulation.Skills;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Gameplay.UI.Inventory;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Tests;

internal static class HudInteractionTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("original skill shortcuts bind the held mouse side without selecting on release", ShortcutsAsync);
        yield return ("UI color keys preserve crimson, metal and opaque world pixels", ColorKeysAsync);
        yield return ("HUD menus avoid resources across scales and window sizes", LayoutAsync);
        yield return ("owner combat statistics survive protobuf decoding", OwnerStatsAsync);
        yield return ("docked HUD keeps equipment, skills and attribute hit regions clear of the footer", DockAsync);
        yield return ("inventory drag uses equipment slots and owner revision without changing local items", EquipmentDragAsync);
        yield return ("HUD gauge separation preserves the frame and masks every rectangular corner", GaugeAsync);
        yield return ("HUD overlays tolerate uninitialized and tiny resized viewports", OverlayAsync);
    }

    private static Task OwnerStatsAsync()
    {
        var packet = new PristonTale.Protocol.Session.OwnedCharacterState
        {
            AttackRating = 173,
            MoveSpeed = 3
        };
        var encoded = Google.Protobuf.MessageExtensions.ToByteArray(packet);
        var decoded = PristonTale.Protocol.Session.OwnedCharacterState.Parser.ParseFrom(encoded);
        var state = PristonTale.Client.Net.Online.OwnerStateMapping.ToNeutral(decoded);
        Assert.True(state is { AttackRating: 173, MoveSpeed: 3 }, "online HUD lost authoritative derived values");
        return Task.CompletedTask;
    }

    private static Task DockAsync()
    {
        foreach (var size in new[] { new Point(800, 600), new Point(1024, 768), new Point(1280, 720), new Point(3440, 1440) })
        {
            foreach (var scale in new[] { 0.75f, 1f, 1.5f })
            {
                var layout = HudLayout.Create(size.X, size.Y, scale, docked: true);
                var viewport = new Rectangle(0, 0, (int)(size.X / layout.Scale), (int)(size.Y / layout.Scale));
                Assert.True(viewport.Contains(layout.Panel), "docked panel escaped viewport");
                Assert.True(layout.Panel.Contains(layout.Menu), "docked menu escaped its frame");
                Assert.True(!layout.Menu.Intersects(layout.Resources), "docked menu covers the resource actions");
                var content = new Rectangle(layout.Panel.X, layout.Panel.Y, layout.Panel.Width, layout.Resources.Y - layout.Panel.Y);
                var bag = HudInventoryLayout.Bag(layout);
                Assert.True(content.Contains(bag), "bag overlaps resource footer");
                for (var slot = 0; slot < 10; slot++)
                {
                    var bounds = HudInventoryLayout.Equipment(layout, slot);
                    Assert.True(content.Contains(bounds) && !bounds.Intersects(bag), "equipment clipped or covered the bag");
                    for (var other = slot + 1; other < 10; other++)
                    {
                        Assert.True(!bounds.Intersects(HudInventoryLayout.Equipment(layout, other)), "equipment targets overlap");
                    }
                }

                for (var index = 0; index < 16; index++)
                {
                    var node = HudSkillTreeLayout.Node(layout.Panel, index);
                    Assert.True(content.Contains(node) && !node.Intersects(HudActions.SkillRow(layout.Panel)), "skill obscures footer or basic attack");
                    for (var other = index + 1; other < 16; other++)
                    {
                        Assert.True(!node.Intersects(HudSkillTreeLayout.Node(layout.Panel, other)), "skill targets overlap");
                    }
                }

                for (var index = 0; index < 5; index++)
                {
                    Assert.True(content.Contains(HudCharacterPanel.AllocateButton(layout.Panel, index)), "attribute control clipped");
                }

                Assert.True(content.Contains(HudSkillTreeLayout.Confirmation(layout.Panel)), "training dialog covers footer");
            }
        }

        return Task.CompletedTask;
    }

    private static Task EquipmentDragAsync()
    {
        var layout = HudLayout.Create(800, 600, 1, docked: true);
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Mechanician, "Inventory");
        var state = OwnerState.Capture(character);
        Assert.True(state.AttackRating > 0 && state.MoveSpeed > 0, "owner projection omitted derived combat statistics");
        Assert.True(ReferenceEquals(state, OwnerState.Capture(character, state)),
            "unchanged derived values invalidated owner cache");
        var item = new OwnedItemView(Guid.NewGuid(), 1, "Weapon", ItemKind.Axe, 1, 0, -1, 2, 3);
        state = state with { Items = new[] { item }, InventoryRevision = 7 };
        InventoryCommand? sent = null;
        var controller = new HudInventoryController(null!, command =>
        {
            sent = command;
            return Task.FromResult(new InventoryOutcome(true, 8, ""));
        });
        var bag = HudInventoryLayout.Bag(layout);
        var pointer = bag.Location + new Point(10, 10);
        controller.Update(state, layout, true, default, MouseAt(pointer, ButtonState.Pressed));
        Assert.True(controller.Dragging == item.InstanceId, "bag item was not picked up");
        var weapon = HudInventoryLayout.Equipment(layout, (int)EquipmentSlot.Weapon).Center;
        controller.Update(state, layout, true, default, MouseAt(weapon, ButtonState.Released));
        Assert.True(sent is { Action: InventoryAction.Equip, Slot: EquipmentSlot.Weapon, ExpectedRevision: 7 }
            && sent.ItemId == item.InstanceId && sent.OperationId != Guid.Empty, "drop did not preserve the authoritative equipment command");
        Assert.True(state.Items[0].Slot == -1 && controller.Dragging == Guid.Empty, "gesture changed immutable owner state or retained drag");
        return Task.CompletedTask;
    }

    private static MouseState MouseAt(Point point, ButtonState left) => new(point.X, point.Y, 0,
        left, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);

    private static Task OverlayAsync()
    {
        var empty = default(HudLayout).PlaceOverlay(new Point(-170, -47), 340, 37);
        Assert.True(empty == new Rectangle(0, 0, 1, 1), "empty viewport generated invalid clamp bounds");
        foreach (var (width, height) in new[] { (0, 0), (1, 1), (32, 24), (160, 90), (800, 600), (1280, 720) })
        {
            foreach (var scale in new[] { float.NaN, 0f, 0.75f, 1.5f })
            {
                var layout = HudLayout.Create(width, height, scale);
                Assert.True(float.IsFinite(layout.Scale) && layout.Scale > 0, "viewport generated an invalid UI transform");
                var viewport = new Rectangle(0, 0, (int)(layout.ViewportWidth / layout.Scale), (int)(layout.ViewportHeight / layout.Scale));
                foreach (var point in new[] { new Point(-1000, -1000), new Point(100000, 100000) })
                {
                    var overlay = layout.PlaceOverlay(point, 340, 1000);
                    Assert.True(viewport.Contains(overlay) && overlay.Width > 0 && overlay.Height > 0, "overlay escaped the resized viewport");
                }
            }
        }

        return Task.CompletedTask;
    }

    private static Task ShortcutsAsync()
    {
        var bindings = new HudSkillBindings();
        var basic = HudSkillBindings.BasicAttackId;
        var rightDown = Mouse(right: ButtonState.Pressed);
        bindings.Update(default, rightDown, basic);
        bindings.Update(new KeyboardState(Keys.F5), rightDown, basic);
        Assert.True(bindings.Shortcut(4) == new HudSkillShortcut(basic, SkillMouseUse.Right), "F5 did not bind the held right button");
        bindings.Update(default, default, basic);
        Assert.True(bindings.RightSkillId == 0, "release after binding unexpectedly selected a skill");
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
        Assert.True(draggedAway.RightSkillId == 0, "release outside skill row selected it");

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
        var legacy = new DecodedPristonImage(2, 1, [Color.Black, Color.White], false);
        PristonUiPixels.Prepare(legacy, PristonUiColorKey.Black);
        Assert.True(legacy.Pixels[0] == Color.Transparent && legacy.Pixels[1] == Color.White, "legacy UI black color key failed");
        var opaque = new DecodedPristonImage(1, 1, [Color.Black], false);
        PristonUiPixels.Prepare(opaque, PristonUiColorKey.None);
        Assert.True(opaque.Pixels[0] == Color.Black, "opaque texture was color-keyed without an explicit policy");
        return Task.CompletedTask;
    }

    private static Task LayoutAsync()
    {
        foreach (var size in new[] { new Point(800, 600), new Point(1024, 768), new Point(1280, 720), new Point(3440, 1440) })
        {
            foreach (var scale in new[] { 0.75f, 1f, 1.5f })
            {
                var layout = HudLayout.Create(size.X, size.Y, scale);
                Assert.True(!layout.Menu.Intersects(layout.Resources), "menu obscures resources");
                Assert.True(!layout.Panel.Intersects(layout.Resources), "open panel obscures resources");
                var left = HudActions.Socket(layout.Resources, false);
                var right = HudActions.Socket(layout.Resources, true);
                Assert.True(left.Width >= 44 && right.Width >= 44 && !left.Intersects(right), "mouse-action reference is too small or overlapping");
            }
        }

        return Task.CompletedTask;
    }

    private static Task GaugeAsync()
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
