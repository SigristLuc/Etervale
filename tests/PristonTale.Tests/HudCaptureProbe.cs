using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Client.Net.Presence;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Tests;

internal sealed class HudCaptureProbe : Game
{
    private enum Hover
    {
        None,
        Life,
        Mana,
        Experience,
        InventoryButton,
        MapToggle,
        Item,
        SkillNode,
        SkillBasic,
        SettingsRow,
        AllocateArrow
    }

    private sealed record Scenario(string Name, int Width, int Height, Keys Panel = Keys.None, Hover Hover = Hover.None, int HealthPercent = 100,
        PlayerClass Class = PlayerClass.Mechanician, bool Npc = false, bool Settings = false, bool SystemMenu = false, bool Learned = false,
        bool Training = false, bool LowHealth = false, float Hour = 13, bool ClassicTarget = false, uint Target = 2);

    private static readonly Scenario[] Scenarios =
    [
        new("bar-800", 800, 600),
        new("bar-1024", 1024, 768),
        new("bar-1280", 1280, 720),
        new("bar-1600x1200-scale2", 1600, 1200),
        new("bar-3440-scale2", 3440, 1440),
        new("bar-3840-scale3", 3840, 2160),
        new("bar-night-800", 800, 600, Hour: 1),
        new("target-classic-800", 800, 600, ClassicTarget: true),
        new("target-npc-800", 800, 600, Target: 3),
        new("target-1920", 1920, 1080),
        new("life-tooltip-800", 800, 600, Hover: Hover.Life, HealthPercent: 60),
        new("mana-tooltip-800", 800, 600, Hover: Hover.Mana),
        new("experience-tooltip-800", 800, 600, Hover: Hover.Experience),
        new("menu-button-hover-800", 800, 600, Hover: Hover.InventoryButton),
        new("map-toggle-hover-800", 800, 600, Hover: Hover.MapToggle),
        new("low-resources-800", 800, 600, LowHealth: true),
        new("inventory-800", 800, 600, Keys.I),
        new("inventory-item-hover-800", 800, 600, Keys.I, Hover.Item),
        new("inventory-1600x1200-scale2", 1600, 1200, Keys.I),
        new("inventory-1920", 1920, 1080, Keys.I),
        new("status-800", 800, 600, Keys.C),
        new("status-arrow-hover-800", 800, 600, Keys.C, Hover.AllocateArrow),
        new("status-1280", 1280, 720, Keys.C),
        new("skills-800", 800, 600, Keys.S),
        new("skills-learned-800", 800, 600, Keys.S, Learned: true),
        new("skill-details-800", 800, 600, Keys.S, Hover.SkillNode),
        new("skill-basic-800", 800, 600, Keys.S, Hover.SkillBasic),
        new("skills-fighter-800", 800, 600, Keys.S, Class: PlayerClass.Fighter),
        new("skills-knight-800", 800, 600, Keys.S, Class: PlayerClass.Knight),
        new("skills-priestess-800", 800, 600, Keys.S, Class: PlayerClass.Priestess),
        new("skills-shaman-800", 800, 600, Keys.S, Class: PlayerClass.Shaman),
        new("npc-unicode-800", 800, 600, Npc: true),
        new("npc-unicode-1280", 1280, 720, Npc: true),
        new("skill-training-800", 800, 600, Npc: true, Learned: true, Training: true),
        new("system-menu-800", 800, 600, SystemMenu: true),
        new("settings-800", 800, 600, Hover: Hover.SettingsRow, Settings: true),
        new("settings-1600x1200-scale2", 1600, 1200, Settings: true)
    ];

    private readonly string _assetRoot;
    private readonly string _output;
    private readonly string? _backgroundPath;
    private SpriteBatch _batch = null!;
    private GameplayHud _hud = null!;
    private Texture2D? _background;
    private RenderTarget2D? _target;
    private OwnerState _owner = null!;
    private HudWorldView _world;
    private int _scenario;
    private int _frames;
    private bool _prepared;
    private readonly PresentationSettings _presentation = new();

    public HudCaptureProbe(string assetRoot, string contentRoot, string output, string? background)
    {
        _assetRoot = assetRoot;
        _output = output;
        _backgroundPath = background;
        _ = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
            SynchronizeWithVerticalRetrace = false
        };
        Content = new AbsoluteContentManager(Services, contentRoot);
        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1d / 60);
    }

    protected override void LoadContent()
    {
        Directory.CreateDirectory(_output);
        _batch = new SpriteBatch(GraphicsDevice);
        var font = Content.Load<SpriteFont>("Fonts/UI");
        Console.WriteLine($"HUD font: {font.Texture.Width}x{font.Texture.Height}; format={font.Texture.Format}; glyphs={font.Characters.Count}; lineSpacing={font.LineSpacing}; M={font.MeasureString("M")}");
        var context = new ScreenContext(GraphicsDevice, Content, _batch, font,
            _assetRoot, null!, ConsoleClientLog.Instance, _ => { }, _presentation);
        _hud = new GameplayHud(context,
            _ => Task.FromResult(new InventoryOutcome(false, 0, "Capture fixture: commands disabled")),
            interactNpc: id => Task.FromResult(new NpcInteraction(true, id, "스킬마스터 베르칸", NpcServices.SkillMaster,
                new[] { "Bem-vindo à escola de habilidades. Escolha uma habilidade para aprender ou melhorar.", "안녕하세요. 스킬을 선택하세요." }, "")));
        _hud.CharacterName = "Panzer";
        _hud.CharacterClass = PlayerClass.Mechanician;
        var player = new CharacterClassRegistry().CreatePlayer(PlayerClass.Mechanician, "Panzer");
        _owner = OwnerState.Capture(player) with
        {
            Money = 12750,
            StatPoints = 5,
            Items = new OwnedItemView[]
            {
                new(Guid.NewGuid(), 1, "Axe", ItemKind.Axe, 1, 0, -1, 2, 3, "WA101"),
                new(Guid.NewGuid(), 2, "Battle armor with a deliberately long localized display name", ItemKind.Armor, 1, 3, -1, 2, 3, "DA102"),
                new(Guid.NewGuid(), 3, "Small mana potion", ItemKind.Potion, 20, 8, -1, 1, 1, "PM101"),
                new(Guid.NewGuid(), 4, "Small healing potion", ItemKind.Potion, 7, 9, -1, 1, 1, "PL101"),
                new(Guid.NewGuid(), 5, "Equipped axe", ItemKind.Axe, 1, -1, 0, 2, 3, "WA101")
            }
        };

        // Diagnostic bootstrap only: the Ricarten stage supplies the authored minimap rectangle.
        var stage = new PristonSmdReader().ReadStage(PristonFileResolver.Resolve(Path.Combine(_assetRoot, "Field", "Ricarten", "village-2.smd")));
        var map = stage.AuthoredMapRectangle;
        var center = new Vector3((map.Left + map.Right) / 512f, 0, -(map.Top + map.Bottom) / 512f);
        _world = new HudWorldView("village-2", false, map, center, new Vector3(0.6f, 0, -0.8f), 13, false);
        Console.WriteLine($"HUD minimap rectangle: {map}");

        _hud.Combat.SetCatalog(new WorldRenderCatalog(new Dictionary<uint, MonsterRenderInfo>
        {
            [1] = new(1, "", 1, "Hopy", 1)
        }));
        _hud.Combat.Observe(new PresenceUpdate(1,
        [
            new(1, RemoteEntityKind.Player, true, default, 0, RemoteMoveState.Idle, 64, 64,
                new PlayerAppearanceInfo("Panzer", 1, 0, 0, 0, "", "", 1), 0),
            new(2, RemoteEntityKind.Monster, false, default, 0, RemoteMoveState.Idle, 31, 50, null, 1, Level: 7),
            new(3, RemoteEntityKind.Npc, false, new System.Numerics.Vector3(center.X + 300, 0, center.Z - 200), 0, RemoteMoveState.Idle, 0, 0, null, 1,
                Name: "Lena")
        ], [], [],
        [
            new(1, 2, RemoteCombatEventKind.Hit, 0, 0, 8, false, false),
            new(2, 1, RemoteCombatEventKind.Hit, 0, 0, 3, false, false),
            new(1, 2, RemoteCombatEventKind.Hit, 0, 0, 11, true, false)
        ]));
        _hud.TargetId = 2;
        if (_backgroundPath is not null)
        {
            var image = PristonImageDecoder.Decode(File.ReadAllBytes(_backgroundPath), ".png");
            _background = new Texture2D(GraphicsDevice, image.Width, image.Height);
            _background.SetData(image.Pixels);
        }
    }

    protected override void Update(GameTime gameTime)
    {
        _frames++;
        if (_frames > 3600 || _hud.AssetsFailed)
        {
            throw new InvalidOperationException("HUD assets did not become ready within the capture timeout.");
        }

        if (_scenario >= Scenarios.Length)
        {
            Exit();
            return;
        }

        var scenario = Scenarios[_scenario];
        if (!_prepared)
        {
            _target?.Dispose();
            _target = new RenderTarget2D(GraphicsDevice, scenario.Width, scenario.Height);
            GraphicsDevice.SetRenderTarget(_target);
            _hud.ClosePanel();
            _hud.CharacterClass = scenario.Class;
            _hud.World = _world with { TimeOfDay = scenario.Hour };
            _hud.TargetId = scenario.Target;
            _presentation.Requested = _presentation.Requested with { ClassicTargetHealthBar = scenario.ClassicTarget };
            var state = ScenarioState(scenario);
            var layout = HudLayout.Create(scenario.Width, scenario.Height);
            if (scenario.Npc)
            {
                _hud.OpenNpc(7);
            }

            _hud.Update(state, default, default);
            _hud.Update(state, scenario.Panel == Keys.None ? default : new KeyboardState(scenario.Panel), default);
            if (scenario.Settings || scenario.SystemMenu)
            {
                Click(state, layout, layout.Bottom(HudBar.MenuButton(HudMenuButton.System)).Center);
            }

            if (scenario.Settings)
            {
                Click(state, layout, layout.Bottom(HudSystemMenu.Entry(HudSystemEntry.Settings)).Center);
            }

            if (scenario.Training)
            {
                Click(state, layout, HudNpcPanel.TrainingButton(layout).Center);
                Click(state, layout, HudSkillsPanel.ClassRow(HudInventoryLayout.Origin(layout), 0).Center);
            }

            _prepared = true;
        }
        else
        {
            GraphicsDevice.SetRenderTarget(_target);
            _hud.Update(ScenarioState(scenario), default, Pointer(scenario));
        }
    }

    protected override void Draw(GameTime gameTime)
    {
        if (!_prepared || !_hud.IsReady || !_hud.IconsIdle || _scenario >= Scenarios.Length)
        {
            GraphicsDevice.SetRenderTarget(null);
            return;
        }

        var scenario = Scenarios[_scenario];
        GraphicsDevice.SetRenderTarget(_target);
        GraphicsDevice.Clear(new Color(45, 53, 47));
        if (_background is not null)
        {
            _batch.Begin();
            _batch.Draw(_background, new Rectangle(0, 0, scenario.Width, scenario.Height), Color.White);
            _batch.End();
        }

        _hud.Draw();
        GraphicsDevice.SetRenderTarget(null);
        using var stream = File.Create(Path.Combine(_output, scenario.Name + ".png"));
        _target!.SaveAsPng(stream, scenario.Width, scenario.Height);
        Console.WriteLine($"HUD {scenario.Name}: captured; textureBytes={_hud.TextureBytes}; GPU={GraphicsDevice.Adapter.DeviceName}");
        _scenario++;
        _prepared = false;
    }

    protected override void UnloadContent()
    {
        _hud.Dispose();
        _target?.Dispose();
        _background?.Dispose();
        _batch.Dispose();
    }

    private void Click(OwnerState state, HudLayout layout, Point logical)
    {
        var x = logical.X * layout.Scale;
        var y = logical.Y * layout.Scale;
        _hud.Update(state, default, new MouseState(x, y, 0,
            ButtonState.Pressed, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released));
        _hud.Update(state, default, new MouseState(x, y, 0,
            ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released));
    }

    private static MouseState Pointer(Scenario scenario)
    {
        var layout = HudLayout.Create(scenario.Width, scenario.Height);
        var origin = HudInventoryLayout.Origin(layout);
        var point = scenario.Hover switch
        {
            Hover.Life => layout.Bottom(HudBar.LifeHover).Center,
            Hover.Mana => layout.Bottom(HudBar.ManaHover).Center,
            Hover.Experience => layout.Bottom(HudBar.ExperienceHover).Center,
            Hover.InventoryButton => layout.Bottom(HudBar.MenuButton(HudMenuButton.Inventory)).Center,
            Hover.MapToggle => layout.Bottom(HudBar.Toggle(HudToggle.Map)).Center,
            Hover.Item => HudInventoryLayout.Cell(origin, 3, 2, 3).Center,
            Hover.SkillNode => HudSkillsPanel.ClassRow(origin, 0).Center,
            Hover.SkillBasic => HudSkillTreeLayout.BasicAttack(origin).Center,
            Hover.SettingsRow => HudSettingsPanel.Row(4).Center,
            Hover.AllocateArrow => HudStatusPanel.AllocateButton(origin, 0).Center,
            _ => new Point(layout.Width - 200, layout.Height / 2)
        };
        return new MouseState(point.X * layout.Scale, point.Y * layout.Scale, 0,
            ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
    }

    private OwnerState ScenarioState(Scenario scenario)
    {
        if (scenario.Learned)
        {
            return _owner with
            {
                Level = 24,
                SkillPoints = 4,
                Skills = new OwnedSkillView[] { new(0x01010001, 3), new(0x01010002, 1), new(0x01010004, 1) }
            };
        }

        return scenario.LowHealth
            ? _owner with { Hp = 1, Mp = 0, Sp = 2 }
            : _owner with { Hp = _owner.MaxHp * scenario.HealthPercent / 100, Experience = (_owner.LevelStart + _owner.NextLevel) / 2 };
    }
}
