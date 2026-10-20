using System.Diagnostics;
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

// The original in-game interface in its draw order: status, skill and inventory windows behind the
// bottom bar, then item overlays, help boxes and message boxes. Party, quest, chat input and the system menu
// entries other than Settings have no server support and are drawn without behaviour.
public sealed class GameplayHud : IDisposable
{
    private readonly ScreenContext _context;
    private readonly HudSprites _sprites;
    private readonly HudPainter _painter;
    private readonly HudItemIcons _icons;
    private readonly HudSkillBindings _bindings = new();
    private readonly HudBar _bar = new();
    private readonly HudStatusPanel _status = new();
    private readonly HudSkillsPanel _skills;
    private readonly HudInventoryPanel _inventoryPanel = new();
    private readonly HudInventoryController _inventory;
    private readonly HudProgressionController _progression;
    private readonly HudNpcPanel _npc;
    private readonly HudSettingsPanel _settings;
    private readonly HudMinimap _minimap = new();
    private readonly HudNotice _notice = new();
    private readonly HudSlide _inventorySlide = new();
    private readonly HudSlide _statusSlide = new();
    private readonly HudSlide _skillSlide = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private KeyboardState _previousKeyboard;
    private MouseState _previousMouse;
    private OwnerState _state;
    private HudMenuButton? _hoveredButton;
    private HudToggle? _hoveredToggle;
    private uint _skillMaster;
    private bool _systemMenu;
    private bool _settingsOpen;
    private bool _npcOpen;
    private bool _mapVisible = true;
    private bool _ready;
    private int _inventoryCompletions;
    private int _progressionCompletions;
    private double _lastUpdate;

    public GameplayHud(ScreenContext context, Func<InventoryCommand, Task<InventoryOutcome>> applyInventory,
        Func<ProgressionCommand, Task<ProgressionOutcome>> applyProgression = null,
        Func<uint, Task<NpcInteraction>> interactNpc = null,
        Action<NpcInteraction> npcOpened = null,
        Action npcClosed = null)
    {
        _context = context;
        _settings = new HudSettingsPanel(context.Presentation);
        _sprites = new HudSprites(context.GraphicsDevice, context.AssetRoot, context.Log);
        _sprites.Request(HudArt.Static);
        _painter = new HudPainter(context.SpriteBatch, context.Font, _sprites);
        _icons = new HudItemIcons(context.GraphicsDevice, context.AssetRoot, context.Log);
        _skills = new HudSkillsPanel(context.Font, _sprites);
        _inventory = new HudInventoryController(context, applyInventory);
        _progression = new HudProgressionController(applyProgression, context.Log);
        _npc = new HudNpcPanel(interactNpc, context.Log, npcOpened, npcClosed);
    }

    public HudCombat Combat { get; } = new();
    public uint TargetId { get; set; }
    public HudWorldView World { get; set; }
    public bool PointerCaptured { get; private set; }
    public bool AssetsFailed => _sprites.Failed || _icons.Failed;
    public bool IconsIdle => _icons.IsIdle && _sprites.IsIdle;
    public long TextureBytes => _sprites.TextureBytes;
    public bool RightAttackEnabled => _bindings.RightSkillId == HudSkillBindings.BasicAttackId;
    public HudSkillBindings Bindings => _bindings;

    public bool IsReady
    {
        get
        {
            _ready = _ready || HudArt.Static.All(_sprites.IsResolved);
            return _ready;
        }
    }

    public PlayerClass CharacterClass
    {
        set
        {
            _skills.SetClass(value);
            _status.ClassName = HudJobTitles.For(value)[0];
            _status.Invalidate();
        }
    }

    public string CharacterName
    {
        get => _status.Name;
        set
        {
            _status.Name = value;
            _status.Invalidate();
        }
    }

    public void OpenNpc(uint entityId) => _npc.Request(entityId);

    public bool ClosePanel()
    {
        _npc.Dismiss();
        var closed = _npcOpen || _settingsOpen || _systemMenu || _skills.TrainingPending
            || _inventorySlide.Open || _statusSlide.Open || _skillSlide.Open;
        _npcOpen = false;
        _settingsOpen = false;
        _systemMenu = false;
        _inventorySlide.Open = false;
        _statusSlide.Open = false;
        _skillSlide.Open = false;
        _skillMaster = 0;
        _skills.CancelTraining();
        _inventory.CancelDrag();
        return closed;
    }

    public void Update(OwnerState state)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var elapsed = now - _lastUpdate;
        _lastUpdate = now;
        Update(state, Keyboard.GetState(), Mouse.GetState(), elapsed);
    }

    internal void Update(OwnerState state, KeyboardState keyboard, MouseState mouse, double elapsedSeconds = 1)
    {
        _sprites.Update();
        _icons.Update();
        _progression.Update();
        _npc.Update(_context.Font);
        _minimap.Update(_sprites, World.FieldId);
        _notice.Update(elapsedSeconds);
        _inventorySlide.Advance(elapsedSeconds);
        _statusSlide.Advance(elapsedSeconds);
        _skillSlide.Advance(elapsedSeconds);
        ForwardMessages();
        if (_npc.ReadyToOpen)
        {
            _npcOpen = true;
            _skillMaster = 0;
            _skills.CancelTraining();
            _settingsOpen = false;
        }

        var layout = Layout();
        var pointer = layout.ToLogical(mouse.Position);
        var design = new Point(pointer.X, pointer.Y - layout.BottomOffset);
        if (_npcOpen && HudNpcPanel.Box.Contains(pointer))
        {
            _npc.Scroll(mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue);
        }

        if (state is not null && (_state is null || state.InventoryRevision != _state.InventoryRevision))
        {
            _icons.Request(state.Items);
        }

        _state = state;
        if (_context.Presentation.AwaitingConfirmation)
        {
            PointerCaptured = true;
            _previousKeyboard = keyboard;
            _previousMouse = mouse;
            return;
        }

        _hoveredButton = ButtonAt(design);
        _hoveredToggle = ToggleAt(design);
        var clicked = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
        UpdateKeys(keyboard);
        if (clicked)
        {
            Click(state, layout, pointer, design, keyboard);
        }

        var skillOrigin = PanelOrigin(layout, _skillSlide);
        var hoveredSkill = _skillSlide.FullyOpen && _skillMaster == 0 ? _skills.HoveredSkill(skillOrigin, pointer) : 0;
        _bindings.Update(keyboard, mouse, hoveredSkill);
        PointerCaptured = Captures(layout, pointer, design) || _inventory.Dragging != Guid.Empty;
        _inventory.Update(state, layout, _inventorySlide.FullyOpen && !_settingsOpen, keyboard, mouse);
        _previousKeyboard = keyboard;
        _previousMouse = mouse;
    }

    private void ForwardMessages()
    {
        if (_inventory.Completions != _inventoryCompletions)
        {
            _inventoryCompletions = _inventory.Completions;
            _notice.Show(_inventory.Message);
        }

        if (_progression.Completions != _progressionCompletions)
        {
            _progressionCompletions = _progression.Completions;
            _notice.Show(_progression.Message);
        }
    }

    private void UpdateKeys(KeyboardState keyboard)
    {
        if (keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl))
        {
            return;
        }

        if (Pressed(keyboard, Keys.I) || Pressed(keyboard, Keys.V))
        {
            TogglePanel(_inventorySlide);
        }

        if (Pressed(keyboard, Keys.C))
        {
            TogglePanel(_statusSlide);
        }

        if (Pressed(keyboard, Keys.S))
        {
            TogglePanel(_skillSlide);
        }

        if (Pressed(keyboard, Keys.X))
        {
            _systemMenu = !_systemMenu;
        }
    }

    private void Click(OwnerState state, HudLayout layout, Point pointer, Point design, KeyboardState keyboard)
    {
        if (_settingsOpen && HudSettingsPanel.Box.Contains(pointer))
        {
            if (HudSettingsPanel.OkButton.Contains(pointer))
            {
                _settingsOpen = false;
            }
            else
            {
                _settings.Click(pointer, keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift));
            }

            return;
        }

        if (_skills.TrainingPending)
        {
            _skills.HandleTrainingClick(PanelOrigin(layout, _skillSlide), pointer, state, _skillMaster, _progression);
            return;
        }

        if (_npcOpen && ClickNpc(layout, pointer))
        {
            return;
        }

        if (_systemMenu)
        {
            var entry = HudSystemMenu.At(design);
            if (entry == HudSystemEntry.Settings)
            {
                _settingsOpen = true;
                _systemMenu = false;
                return;
            }

            if (entry is not null)
            {
                return;
            }

            if (_hoveredButton != HudMenuButton.System)
            {
                _systemMenu = false;
            }
        }

        if (_hoveredButton is { } button)
        {
            PressButton(button);
            return;
        }

        if (_hoveredToggle == HudToggle.Map)
        {
            _mapVisible = !_mapVisible;
            return;
        }

        ClickPanels(state, layout, pointer);
    }

    private bool ClickNpc(HudLayout layout, Point pointer)
    {
        if (_npc.CanTrain && HudNpcPanel.TrainingButton(layout).Contains(pointer))
        {
            _npcOpen = false;
            _skillMaster = _npc.Current.EntityId;
            OpenOnly(_skillSlide);
            return true;
        }

        if (HudNpcPanel.OkButton.Contains(pointer) || _npc.CanTrain && HudNpcPanel.ExitButton(layout).Contains(pointer))
        {
            _npcOpen = false;
            _npc.Dismiss();
            return true;
        }

        return HudNpcPanel.Box.Contains(pointer) || _npc.CanTrain && HudNpcPanel.SkillMasterBox(layout).Contains(pointer);
    }

    private void PressButton(HudMenuButton button)
    {
        switch (button)
        {
            case HudMenuButton.Status:
                TogglePanel(_statusSlide);
                break;
            case HudMenuButton.Inventory:
                TogglePanel(_inventorySlide);
                break;
            case HudMenuButton.Skills:
                TogglePanel(_skillSlide);
                break;
            case HudMenuButton.System:
                _systemMenu = !_systemMenu;
                break;
        }
    }

    private void ClickPanels(OwnerState state, HudLayout layout, Point pointer)
    {
        if (_inventorySlide.FullyOpen && HudInventoryLayout.Close(PanelOrigin(layout, _inventorySlide)).Contains(pointer))
        {
            _inventorySlide.Open = false;
            _inventory.CancelDrag();
            return;
        }

        if (_statusSlide.FullyOpen)
        {
            var origin = PanelOrigin(layout, _statusSlide);
            if (HudStatusPanel.Close(origin).Contains(pointer))
            {
                _statusSlide.Open = false;
                return;
            }

            if (state is not null && state.StatPoints > 0 && !_progression.Pending)
            {
                for (var index = 0; index < 5; index++)
                {
                    if (HudStatusPanel.AllocateButton(origin, index).Contains(pointer))
                    {
                        _progression.Submit(new ProgressionCommand(Guid.NewGuid(), state.ProgressionRevision,
                            ProgressionAction.AllocateAttribute, (CharacterAttribute)index, 1));
                    }
                }
            }
        }

        if (_skillSlide.FullyOpen)
        {
            var origin = PanelOrigin(layout, _skillSlide);
            if (HudSkillTreeLayout.Close(origin).Contains(pointer))
            {
                _skillSlide.Open = false;
                _skillMaster = 0;
                return;
            }

            _skills.HandleTrainingClick(origin, pointer, state, _skillMaster, _progression);
        }
    }

    private bool Captures(HudLayout layout, Point pointer, Point design)
    {
        if (HudBar.Captures(design) || _mapVisible && HudMinimap.Box(layout).Contains(pointer)
            || WindowContains(layout, _inventorySlide, pointer) || WindowContains(layout, _statusSlide, pointer) || WindowContains(layout, _skillSlide, pointer))
        {
            return true;
        }

        return _systemMenu && HudSystemMenu.Bounds.Contains(design)
            || _settingsOpen && HudSettingsPanel.Box.Contains(pointer)
            || _npcOpen && (HudNpcPanel.Box.Contains(pointer) || _npc.CanTrain && HudNpcPanel.SkillMasterBox(layout).Contains(pointer))
            || _skills.TrainingPending && HudMessageBox.Bounds.Contains(pointer);
    }

    public void Draw()
    {
        var layout = Layout();
        var pointer = layout.ToLogical(_previousMouse.Position);
        var pressed = _previousMouse.LeftButton == ButtonState.Pressed;
        _painter.Begin(layout);
        if (_mapVisible)
        {
            _minimap.Draw(_painter, layout, World, Combat.NpcPositions);
        }

        Combat.DrawTarget(_painter, layout, TargetId, _context.Presentation.Requested.ClassicTargetHealthBar);
        if (!_inventorySlide.Visible && !_statusSlide.Visible && !_skillSlide.Visible)
        {
            Combat.DrawLog(_painter, layout);
        }

        if (_statusSlide.Visible)
        {
            _status.Draw(_painter, PanelOrigin(layout, _statusSlide), _state, pointer, pressed, _statusSlide.FullyOpen);
        }

        if (_skillSlide.Visible)
        {
            _skills.Draw(_painter, PanelOrigin(layout, _skillSlide), pointer, _state, _bindings, _skillMaster, pressed, _skillSlide.FullyOpen);
        }

        if (_inventorySlide.Visible)
        {
            _inventoryPanel.Draw(_painter, PanelOrigin(layout, _inventorySlide), _state, _inventory, _icons, pointer, _inventorySlide.FullyOpen);
        }

        _painter.Flush();
        var left = _bindings.LeftSkillId == HudSkillBindings.BasicAttackId;
        _bar.Draw(_painter, layout, _state, new HudBarView(World.TimeOfDay, World.Running, _mapVisible, left, RightAttackEnabled,
            _hoveredButton, pressed && _hoveredButton is not null, _hoveredToggle));
        _bar.DrawHoverText(_painter, layout, _state, pointer);
        if (_systemMenu)
        {
            HudSystemMenu.Draw(_painter, layout, pointer);
        }

        if (_npcOpen)
        {
            _painter.Flush();
            _npc.Draw(_painter, layout, pointer, true);
        }

        if (_settingsOpen)
        {
            _painter.Flush();
            _settings.Draw(_painter, pointer);
        }

        if (_inventorySlide.FullyOpen)
        {
            _inventoryPanel.DrawOverlay(_painter, layout, _icons, pointer);
        }

        if (_skillSlide.FullyOpen)
        {
            _skills.DrawOverlay(_painter, layout, pointer, _skillMaster, _progression.Message);
        }

        _painter.Flush();
        _notice.Draw(_painter, layout);
        _painter.End();
    }

    private HudLayout Layout()
    {
        var viewport = _context.GraphicsDevice.Viewport;
        return HudLayout.Create(viewport.Width, viewport.Height);
    }

    private static Point PanelOrigin(HudLayout layout, HudSlide slide) => HudInventoryLayout.Origin(layout, slide.Offset);

    private static bool WindowContains(HudLayout layout, HudSlide slide, Point pointer) =>
        slide.Visible && HudInventoryLayout.Window(PanelOrigin(layout, slide)).Contains(pointer);

    private static HudMenuButton? ButtonAt(Point design)
    {
        foreach (var button in Enum.GetValues<HudMenuButton>())
        {
            if (HudBar.MenuButton(button).Contains(design))
            {
                return button;
            }
        }

        return null;
    }

    private static HudToggle? ToggleAt(Point design)
    {
        foreach (var toggle in Enum.GetValues<HudToggle>())
        {
            if (HudBar.Toggle(toggle).Contains(design))
            {
                return toggle;
            }
        }

        return null;
    }

    private void TogglePanel(HudSlide slide)
    {
        if (slide.Open)
        {
            slide.Open = false;
            if (slide == _skillSlide)
            {
                _skillMaster = 0;
                _skills.CancelTraining();
            }

            _inventory.CancelDrag();
            return;
        }

        _skillMaster = 0;
        OpenOnly(slide);
    }

    // The status, skill and inventory windows share the same screen area; opening one closes the others.
    private void OpenOnly(HudSlide slide)
    {
        _inventorySlide.Open = slide == _inventorySlide;
        _statusSlide.Open = slide == _statusSlide;
        _skillSlide.Open = slide == _skillSlide;
        _skills.CancelTraining();
        _inventory.CancelDrag();
    }

    private bool Pressed(KeyboardState state, Keys key) => state.IsKeyDown(key) && !_previousKeyboard.IsKeyDown(key);

    public void Dispose()
    {
        _icons.Dispose();
        _painter.Dispose();
        _sprites.Dispose();
    }
}
