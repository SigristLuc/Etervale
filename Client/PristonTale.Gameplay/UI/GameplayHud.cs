using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Gameplay.UI;

public sealed class GameplayHud : IDisposable
{
    private readonly ScreenContext _context;
    private readonly HudAtlas _atlas;
    private readonly bool _ownsAtlas;
    private readonly HudCanvas _canvas;
    private readonly HudItemIcons _icons;
    private readonly HudIconCache _skillIcons;
    private readonly HudSkillsPanel _skills;
    private readonly HudResources _resources = new();
    private readonly HudActions _actions = new();
    public HudCombat Combat { get; } = new();
    public uint TargetId { get; set; }
    private readonly HudCharacterPanel _character = new();
    private readonly HudInventoryPanel _inventoryPanel = new();
    private readonly HudInventoryController _inventory;
    private readonly HudProgressionController _progression;
    private readonly HudNpcPanel _npc;
    private readonly HudSettingsPanel _settings;
    private uint _skillMaster;
    private KeyboardState _previousKeyboard;
    private MouseState _previousMouse;
    private HudLayout _layout;
    private float _layoutRequestScale;
    private bool _hasInput;
    private bool _layoutDocked;
    private OwnerState _state;
    private string _attributeHint = "Character";
    private Point _pointer;
    private PanelKind _panel;

    private enum PanelKind
    {
        None,
        Inventory,
        Character,
        Settings,
        Actions,
        Npc
    }

    public GameplayHud(ScreenContext context, Func<InventoryCommand, Task<InventoryOutcome>> applyInventory,
        Func<ProgressionCommand, Task<ProgressionOutcome>> applyProgression = null,
        Func<uint, Task<NpcInteraction>> interactNpc = null,
        Action<NpcInteraction> npcOpened = null,
        Action npcClosed = null)
    {
        _context = context;
        _settings = new HudSettingsPanel(context.Presentation);
        _ownsAtlas = context.UiAtlas is null;
        _atlas = context.UiAtlas ?? new HudAtlas(context.GraphicsDevice, context.AssetRoot, context.Log);
        _canvas = new HudCanvas(context.SpriteBatch, context.Font, _atlas);
        _icons = new HudItemIcons(context.GraphicsDevice, context.AssetRoot, context.Log);
        _skillIcons = new HudIconCache(context.GraphicsDevice, context.AssetRoot, context.Log, HudIconCatalog.Skills);
        _skills = new HudSkillsPanel(context.Font, _skillIcons);
        _inventory = new HudInventoryController(context, applyInventory);
        _progression = new HudProgressionController(applyProgression, context.Log);
        _npc = new HudNpcPanel(interactNpc, context.Log, npcOpened, npcClosed);
    }

    public void OpenNpc(uint entityId) => _npc.Request(entityId);

    public float Scale
    {
        get => _context.Presentation.Requested.UiScale;
        set => _context.Presentation.Requested = _context.Presentation.Requested with { UiScale = value };
    }
    public bool PointerCaptured { get; private set; }
    public bool IsReady => _atlas.IsReady;
    public bool AssetsFailed => _atlas.Failed || _icons.Failed || _skillIcons.Failed;
    public bool IconsIdle => _icons.IsIdle && _skillIcons.IsIdle;
    public long AtlasTextureBytes => _atlas.TextureBytes;
    public bool RightAttackEnabled => _actions.RightAttackEnabled;
    public PlayerClass CharacterClass
    {
        set
        {
            _skills.SetClass(value);
            _character.ClassName = value.ToString();
        }
    }
    public string CharacterName
    {
        get => _resources.CharacterName;
        set
        {
            _resources.CharacterName = value;
            _character.Name = value;
        }
    }

    public bool ClosePanel()
    {
        _npc.Dismiss();
        if (_panel == PanelKind.None)
        {
            return false;
        }

        _panel = PanelKind.None;
        _skillMaster = 0;
        _skills.CancelTraining();
        _inventory.CancelDrag();
        return true;
    }

    public void Update(OwnerState state) => Update(state, Keyboard.GetState(), Mouse.GetState());

    internal void Update(OwnerState state, KeyboardState keyboard, MouseState mouse)
    {
        _atlas.Update();
        _icons.Update();
        _skillIcons.Update();
        _progression.Update();
        _npc.Update(_context.Font);
        if (_npc.ReadyToOpen)
        {
            _panel = PanelKind.Npc;
            _skillMaster = 0;
            _skills.CancelTraining();
            _inventory.CancelDrag();
        }
        EnsureLayout();
        _pointer = _layout.ToLogical(mouse.Position);
        _hasInput = true;
        if (_panel == PanelKind.Npc && _layout.Panel.Contains(_pointer))
        {
            _npc.Scroll(_layout.Panel, mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue);
        }
        if (state is not null && (_state is null || state.InventoryRevision != _state.InventoryRevision))
        {
            _icons.Request(state.Items);
        }

        if (state?.StatPoints != _state?.StatPoints)
        {
            _attributeHint = state?.StatPoints > 0 ? $"Distribute {state.StatPoints} points (C)" : "Character";
        }

        _state = state;
        if (_context.Presentation.AwaitingConfirmation)
        {
            PointerCaptured = true;
            _previousKeyboard = keyboard;
            _previousMouse = mouse;
            return;
        }
        var clicked = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
        var menuClicked = clicked && _panel != PanelKind.Settings;
        if (Pressed(keyboard, Keys.I) || menuClicked && MenuButton(0).Contains(_pointer))
        {
            Toggle(PanelKind.Inventory);
        }

        if (Pressed(keyboard, Keys.C) || menuClicked && MenuButton(1).Contains(_pointer))
        {
            Toggle(PanelKind.Character);
        }

        if (menuClicked && MenuButton(3).Contains(_pointer))
        {
            Toggle(PanelKind.Settings);
        }

        if (Pressed(keyboard, Keys.S) || menuClicked && MenuButton(2).Contains(_pointer))
        {
            Toggle(PanelKind.Actions);
        }

        EnsureLayout();
        var hoveredSkill = _panel == PanelKind.Actions && _skillMaster == 0 && HudActions.SkillRow(_layout.Panel).Contains(_pointer)
            ? HudSkillBindings.BasicAttackId : 0;
        _actions.Bindings.Update(keyboard, mouse, hoveredSkill);

        if (_panel == PanelKind.Actions && clicked)
        {
            _skills.HandleTrainingClick(_layout.Panel, _pointer, state, _skillMaster, _progression);
        }

        if (_panel == PanelKind.Npc && clicked && _npc.CanTrain && HudNpcPanel.TrainingButton(_layout.Panel).Contains(_pointer))
        {
            _panel = PanelKind.Actions;
            _skillMaster = _npc.Current.EntityId;
            EnsureLayout();
        }

        if (_panel == PanelKind.Character && clicked && state is not null && state.StatPoints > 0 && !_progression.Pending)
        {
            for (var index = 0; index < 5; index++)
            {
                if (HudCharacterPanel.AllocateButton(_layout.Panel, index).Contains(_pointer))
                {
                    _progression.Submit(new ProgressionCommand(Guid.NewGuid(), state.ProgressionRevision,
                        ProgressionAction.AllocateAttribute, (CharacterAttribute)index, 1));
                }
            }
        }

        if (_panel != PanelKind.None && clicked && CloseButton().Contains(_pointer))
        {
            ClosePanel();
        }

        if (_panel == PanelKind.Settings && clicked)
        {
            _settings.Click(_layout.Panel, _pointer, keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift));
        }

        EnsureLayout();
        PointerCaptured = _layout.Resources.Contains(_pointer) || _layout.Menu.Contains(_pointer)
            || _panel != PanelKind.None && _layout.Panel.Contains(_pointer)
            || _inventory.Dragging != Guid.Empty;
        _inventory.Update(state, _layout, _panel == PanelKind.Inventory, keyboard, mouse);
        _previousKeyboard = keyboard;
        _previousMouse = mouse;
    }

    public void Draw()
    {
        if (!_atlas.IsReady)
        {
            return;
        }

        EnsureLayout();
        _pointer = _layout.ToLogical(_previousMouse.Position);
        _canvas.Begin(_layout.Scale);
        Combat.Draw(_canvas, _layout, TargetId, _panel == PanelKind.None ? Rectangle.Empty : _layout.Panel);
        if (_panel != PanelKind.None)
        {
            _canvas.FlushText();
            _canvas.Frame(_layout.Panel);
            var title = _panel switch
            {
                PanelKind.Inventory => "INVENTORY",
                PanelKind.Character => "CHARACTER",
                PanelKind.Actions => "SKILLS",
                PanelKind.Npc => "CONVERSATION",
                _ => "INTERFACE"
            };
            _canvas.Text(title, new Rectangle(_layout.Panel.X + 22, _layout.Panel.Y + 14, 315, 26), scale: 0.95f);
            _canvas.Frame(CloseButton(), "Button", 7);
            _canvas.Text("X", CloseButton(), centered: true);
            switch (_panel)
            {
                case PanelKind.Inventory:
                    _inventoryPanel.Draw(_canvas, _layout, _state, _inventory, _icons, _pointer);
                    break;
                case PanelKind.Character:
                    _character.Draw(_canvas, _layout.Panel, _state);
                    break;
                case PanelKind.Settings:
                    _settings.Draw(_canvas, _layout.Panel);
                    break;
                case PanelKind.Actions:
                    _skills.Draw(_canvas, _layout, _pointer, _state, _actions.Bindings, _skillMaster, _progression.Message);
                    break;
                case PanelKind.Npc:
                    _npc.Draw(_canvas, _layout.Panel);
                    break;
            }

            if (_panel == PanelKind.Character)
            {
                _canvas.Text(_progression.Message, new Rectangle(_layout.Panel.X + 24, _layout.Panel.Y + 205, _layout.Panel.Width - 48, 22),
                    HudCanvas.Muted, 0.6f);
            }
        }

        _canvas.FlushText();
        _resources.Draw(_canvas, _layout.Resources, _state);
        _actions.Draw(_canvas, _layout.Resources);
        if (_panel != PanelKind.Settings)
        {
            DrawMenu();
        }
        DrawResourceTooltip();
        _canvas.End();
    }

    private void DrawMenu()
    {
        _canvas.Frame(_layout.Menu, "Panel", 14);
        DrawMenuButton(0, "Bag", "I", "Inventory", _panel == PanelKind.Inventory);
        DrawMenuButton(1, "Character", "C", _attributeHint, _panel == PanelKind.Character);
        DrawMenuButton(2, "Skills", "S", "Skills", _panel == PanelKind.Actions);
        DrawMenuButton(3, "Settings", "", "Display and graphics", _panel == PanelKind.Settings);
    }

    private void DrawResourceTooltip()
    {
        if (!_hasInput || _panel != PanelKind.None && _layout.Panel.Contains(_pointer) && !_layout.Resources.Contains(_pointer))
        {
            return;
        }

        var text = _actions.HoverText(_layout.Resources, _pointer);
        if (text.Length == 0)
        {
            text = _resources.HoverText(_layout.Resources, _pointer);
        }

        if (text.Length == 0)
        {
            return;
        }

        _canvas.FlushText();
        var bounds = _layout.PlaceOverlay(new Point(_pointer.X - 170, _layout.Resources.Y - 47), 340, 37);
        _canvas.Frame(bounds, "Inset", 8);
        _canvas.Text(text, new Rectangle(bounds.X + 12, bounds.Y + 5, bounds.Width - 24, bounds.Height - 10), Color.White, 0.72f, centered: true);
    }

    private void DrawMenuButton(int index, string icon, string binding, string label, bool selected)
    {
        var bounds = MenuButton(index);
        var hover = bounds.Contains(_pointer);
        _canvas.Frame(bounds, selected || hover ? "Selected" : "Slot", 5);
        _canvas.Image(icon, new Rectangle(bounds.X + 4, bounds.Y + 4, bounds.Width - 8, bounds.Width - 8));
        _canvas.Text(binding, new Rectangle(bounds.X, bounds.Bottom - 13, bounds.Width, 13), Color.White, 0.5f, centered: true);
        if (index == 1 && _state?.StatPoints > 0)
        {
            var badge = new Rectangle(bounds.Right - 16, bounds.Y, 16, 16);
            _canvas.Frame(badge, "Selected", 4);
            _canvas.Text("+", badge, Color.Gold, 0.65f, centered: true);
        }

        if (hover)
        {
            var tooltip = _layout.PlaceOverlay(new Point(_layout.Menu.X - 28, _layout.Menu.Y - 34), 232, 29);
            _canvas.Frame(tooltip, "Inset", 7);
            _canvas.Text(label, tooltip, scale: 0.65f, centered: true);
        }
    }

    private void EnsureLayout()
    {
        var viewport = _context.GraphicsDevice.Viewport;
        var docked = _panel is PanelKind.Inventory or PanelKind.Character or PanelKind.Actions;
        if (_layout.Scale > 0 && _layout.ViewportWidth == viewport.Width && _layout.ViewportHeight == viewport.Height && _layoutRequestScale.Equals(Scale)
            && _layoutDocked == docked)
        {
            return;
        }

        _layout = HudLayout.Create(viewport.Width, viewport.Height, Scale, docked);
        _layoutRequestScale = Scale;
        _layoutDocked = docked;
    }

    private Rectangle CloseButton() => new(_layout.Panel.Right - 44, _layout.Panel.Y + 13, 28, 27);
    private Rectangle MenuButton(int index) => new(_layout.Menu.X + 9 + index * 39, _layout.Menu.Y + 10, 36, 46);
    private bool Pressed(KeyboardState state, Keys key) => state.IsKeyDown(key) && !_previousKeyboard.IsKeyDown(key);

    private void Toggle(PanelKind panel)
    {
        _npc.Dismiss();
        _panel = _panel == panel ? PanelKind.None : panel;
        _skillMaster = 0;
        _skills.CancelTraining();
        _inventory.CancelDrag();
    }

    public void Dispose()
    {
        _icons.Dispose();
        _skillIcons.Dispose();
        if (_ownsAtlas)
        {
            _atlas.Dispose();
        }
    }
}
