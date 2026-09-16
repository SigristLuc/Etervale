using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Client.Net.Presence;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.UI;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Tests;

internal sealed class HudCaptureProbe : Game
{
    private sealed record Scenario(string Name, int Width, int Height, float Scale, Keys Panel = Keys.None, bool LowHealth = false,
        int Hover = 0, int HealthPercent = 100, PlayerClass Class = PlayerClass.Mechanician,
        bool Npc = false, bool Settings = false, bool VerticalSync = false, bool Learned = false, bool Training = false);

    private static readonly Scenario[] Scenarios =
    [
        new("resources-1280", 1280, 720, 1),
        new("npc-unicode-1280", 1280, 720, 1, Npc: true),
        new("npc-unicode-800", 800, 600, 1, Npc: true),
        new("settings-vsync-off", 1280, 720, 1, Hover: 6, Settings: true),
        new("settings-vsync-on-800", 800, 600, 1, Hover: 6, Settings: true, VerticalSync: true),
        new("health-tooltip-1280", 1280, 720, 1, Hover: 1),
        new("mana-tooltip-1280", 1280, 720, 1, Hover: 2),
        new("right-action-tooltip-1280", 1280, 720, 1, Hover: 3),
        new("inventory-1280", 1280, 720, 1, Keys.I),
        new("character-1280", 1280, 720, 1, Keys.C),
        new("character-800", 800, 600, 1, Keys.C),
        new("character-ultrawide", 3440, 1440, 1.5f, Keys.C),
        new("skills-ultrawide", 3440, 1440, 1.5f, Keys.S),
        new("skills-unavailable", 1280, 720, 1, Keys.S, Hover: 7),
        new("skill-training-800", 800, 600, 1, Npc: true, Learned: true, Training: true),
        new("skills-learned", 1280, 720, 1, Keys.S, Learned: true),
        new("actions-1280", 1280, 720, 1, Keys.S),
        new("skill-details-1280", 1280, 720, 1, Keys.S, Hover: 4),
        new("skill-details-800", 800, 600, 1, Keys.S, Hover: 4),
        new("skills-fighter", 1280, 720, 1, Keys.S, Hover: 4, Class: PlayerClass.Fighter),
        new("skills-pikeman", 1280, 720, 1, Keys.S, Class: PlayerClass.Pikeman),
        new("skills-archer", 1280, 720, 1, Keys.S, Class: PlayerClass.Archer),
        new("skills-knight", 1280, 720, 1, Keys.S, Hover: 5, Class: PlayerClass.Knight),
        new("skills-atalanta", 1280, 720, 1, Keys.S, Class: PlayerClass.Atalanta),
        new("skills-priestess", 1280, 720, 1, Keys.S, Class: PlayerClass.Priestess),
        new("skills-magician", 1280, 720, 1, Keys.S, Class: PlayerClass.Magician),
        new("skills-assassin", 1280, 720, 1, Keys.S, Class: PlayerClass.Assassin),
        new("skills-shaman", 1280, 720, 1, Keys.S, Class: PlayerClass.Shaman),
        new("health-empty-1280", 1280, 720, 1, HealthPercent: 0),
        new("health-quarter-1280", 1280, 720, 1, HealthPercent: 25),
        new("health-half-1280", 1280, 720, 1, HealthPercent: 50),
        new("health-three-quarters-1280", 1280, 720, 1, HealthPercent: 75),
        new("low-resources-800", 800, 600, 1, LowHealth: true),
        new("inventory-800", 800, 600, 1, Keys.I),
        new("inventory-ultrawide", 3440, 1440, 1.5f, Keys.I),
        new("inventory-small-ui", 1024, 768, 0.75f, Keys.I)
    ];

    private readonly string _assetRoot;
    private readonly string _output;
    private readonly string? _backgroundPath;
    private SpriteBatch _batch = null!;
    private GameplayHud _hud = null!;
    private Texture2D? _background;
    private RenderTarget2D? _target;
    private OwnerState _owner = null!;
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
        Content.RootDirectory = Path.GetFullPath(contentRoot);
        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1d / 60);
    }

    protected override void LoadContent()
    {
        Directory.CreateDirectory(_output);
        _batch = new SpriteBatch(GraphicsDevice);
        var font = Content.Load<SpriteFont>("Fonts/UI");
        Console.WriteLine($"HUD font: {font.Texture.Width}x{font.Texture.Height}; format={font.Texture.Format}; glyphs={font.Characters.Count}");
        var context = new ScreenContext(GraphicsDevice, Content, _batch, font,
            _assetRoot, null!, ConsoleClientLog.Instance, _ => { }, _ => { }, _presentation);
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

        _hud.Combat.SetCatalog(new WorldRenderCatalog(new Dictionary<uint, MonsterRenderInfo>
        {
            [1] = new(1, "", 1, "Hopy", 1)
        }));
        _hud.Combat.Observe(new PresenceUpdate(1,
        [
            new(1, RemoteEntityKind.Player, true, default, 0, RemoteMoveState.Idle, 64, 64,
                new PlayerAppearanceInfo("Panzer", 1, 0, 0, 0, "", "", 1), 0),
            new(2, RemoteEntityKind.Monster, false, default, 0, RemoteMoveState.Idle, 31, 50, null, 1)
        ], [], [],
        [
            new(1, 2, RemoteCombatEventKind.Hit, 0, 0, 8, false, false),
            new(2, 1, RemoteCombatEventKind.Hit, 0, 0, 3, false, false),
            new(1, 2, RemoteCombatEventKind.Hit, 0, 0, 11, true, false)
        ]));
        _hud.TargetId = 2;
        if (_backgroundPath is not null)
        {
            // Diagnostic bootstrap only; no gameplay loop is running while the capture backdrop is loaded.
            var image = PristonImageDecoder.Decode(File.ReadAllBytes(_backgroundPath), ".png");
            _background = new Texture2D(GraphicsDevice, image.Width, image.Height);
            _background.SetData(image.Pixels);
        }
    }

    protected override void Update(GameTime gameTime)
    {
        _frames++;
        if (_frames > 1800 || _hud.AssetsFailed)
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
            _hud.Scale = scenario.Scale;
            _hud.CharacterClass = scenario.Class;
            _presentation.VerticalSync = scenario.VerticalSync;
            if (scenario.Npc)
            {
                _hud.OpenNpc(7);
            }
            _hud.Update(_owner, default, default);
            var state = ScenarioState(scenario);
            _hud.Update(state, scenario.Panel == Keys.None ? default : new KeyboardState(scenario.Panel), Pointer(scenario));
            if (scenario.Settings)
            {
                var pointer = Pointer(scenario).Position;
                _hud.Update(state, default, new MouseState(pointer.X, pointer.Y, 0,
                    ButtonState.Pressed, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released));
            }
            if (scenario.Training)
            {
                var dialog = HudLayout.Create(scenario.Width, scenario.Height, scenario.Scale);
                Click(state, PristonTale.Gameplay.UI.Widgets.HudNpcPanel.TrainingButton(dialog.Panel).Center, dialog.Scale);
                var dock = HudLayout.Create(scenario.Width, scenario.Height, scenario.Scale, docked: true);
                Click(state, PristonTale.Gameplay.UI.Widgets.HudSkillsPanel.ClassRow(dock.Panel, 0).Center, dock.Scale);
            }

            _prepared = true;
        }
        else
        {
            GraphicsDevice.SetRenderTarget(_target);
            var state = ScenarioState(scenario);
            _hud.Update(state, default, Pointer(scenario));
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
        Console.WriteLine($"HUD {scenario.Name}: captured; atlasBytes={_hud.AtlasTextureBytes}; GPU={GraphicsDevice.Adapter.Description}");
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

    private void Click(OwnerState state, Point logical, float scale)
    {
        var x = (int)(logical.X * scale);
        var y = (int)(logical.Y * scale);
        _hud.Update(state, default, new MouseState(x, y, 0,
            ButtonState.Pressed, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released));
        _hud.Update(state, default, new MouseState(x, y, 0,
            ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released));
    }

    private static MouseState Pointer(Scenario scenario)
    {
        var layout = HudLayout.Create(scenario.Width, scenario.Height, scenario.Scale, scenario.Panel is Keys.I or Keys.C or Keys.S);
        var resources = layout.Resources;
        var point = scenario.Hover switch
        {
            1 => new Point(resources.Center.X, resources.Y + 52),
            2 => new Point(resources.X + 120, resources.Y + 54),
            3 => PristonTale.Gameplay.UI.Widgets.HudActions.Socket(resources, true).Center,
            4 => PristonTale.Gameplay.UI.Widgets.HudSkillsPanel.ClassRow(layout.Panel, 0).Center,
            5 => PristonTale.Gameplay.UI.Widgets.HudSkillsPanel.ClassRow(layout.Panel, 3).Center,
            7 => PristonTale.Gameplay.UI.Widgets.HudSkillsPanel.ClassRow(layout.Panel, 15).Center,
            6 => new Point(layout.Menu.X + 9 + 3 * 39 + 18, layout.Menu.Y + 33),
            _ => Point.Zero
        };
        return new MouseState((int)(point.X * layout.Scale), (int)(point.Y * layout.Scale), 0,
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

        return scenario.LowHealth ? _owner with { Hp = 1, Mp = 0, Sp = 2 } : _owner with { Hp = _owner.MaxHp * scenario.HealthPercent / 100 };
    }
}
