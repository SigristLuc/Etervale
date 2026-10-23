using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Npc;
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
    private readonly HudSkillHud _skillHud;
    private static readonly CharacterClassRegistry SkillClasses = new();
    private PlayerClass _class;
    private IReadOnlyList<OwnedSkillView> _syncedSkills;
    private readonly HudInventoryPanel _inventoryPanel = new();
    private readonly HudInventoryController _inventory;
    private readonly HudProgressionController _progression;
    private readonly HudNpcPanel _npc;
    private readonly HudNpcServiceController _services;
    private readonly HudShopWindow _shop = new();
    private readonly HudWarehouseWindow _warehouse = new();
    private readonly HudSettingsPanel _settings;
    private readonly HudMinimap _minimap = new();
    private readonly List<Vector3> _minimapNpcs = new(64);
    private readonly HudTargetPortrait _portrait;
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
    private int _serviceCompletions;
    private int _bindingRefusals;
    private int _serviceOpened;
    private double _lastUpdate;

    public GameplayHud(ScreenContext context, Func<InventoryCommand, Task<InventoryOutcome>> applyInventory,
        Func<ProgressionCommand, Task<ProgressionOutcome>> applyProgression = null,
        Func<uint, Task<NpcInteraction>> interactNpc = null,
        Action<NpcInteraction> npcOpened = null,
        Action npcClosed = null,
        Func<NpcServiceCommand, Task<NpcServiceReply>> requestNpcService = null)
    {
        _context = context;
        _settings = new HudSettingsPanel(context.Presentation);
        _sprites = new HudSprites(context.GraphicsDevice, context.AssetRoot, context.Log);
        _sprites.Request(HudArt.Static);
        _sprites.Request(HudNpcArt.All);
        _painter = new HudPainter(context.SpriteBatch, context.Font, _sprites);
        _icons = new HudItemIcons(context.GraphicsDevice, context.AssetRoot, context.Log);
        _skills = new HudSkillsPanel(context.Font, _sprites);
        _skillHud = new HudSkillHud(_sprites);
        _inventory = new HudInventoryController(context, applyInventory);
        _progression = new HudProgressionController(applyProgression, context.Log);
        _npc = new HudNpcPanel(interactNpc, context.Log, npcOpened, npcClosed);
        _portrait = new HudTargetPortrait(context.GraphicsDevice);
        _services = new HudNpcServiceController(requestNpcService, context.Log);
        _inventory.InterceptClick = InterceptInventoryClick;
        _inventory.ExternalDrop = DropFromInventory;
        _inventoryPanel.Note = InventoryNote;
    }

    public HudCombat Combat { get; } = new();
    public uint TargetId { get; set; }
    public HudWorldView World { get; set; }
    public IHudPortraitScene PortraitScene { get; set; }
    public bool PortraitReady => _portrait.Texture(TargetId) is not null;
    public float MinimapViewSize => _minimap.CurrentViewSize;
    public bool PointerCaptured { get; private set; }
    public bool AssetsFailed => _sprites.Failed || _icons.Failed;
    public bool IconsIdle => _icons.IsIdle && _sprites.IsIdle;
    public long TextureBytes => _sprites.TextureBytes;
    public bool RightAttackEnabled => _bindings.RightSkillId == HudSkillBindings.BasicAttackId;
    internal uint LeftSkillId => _bindings.LeftSkillId;
    internal uint RightSkillId => _bindings.RightSkillId;
    public HudSkillBindings Bindings => _bindings;

    // Cooldowns and active effects of the local player, and the clock they are kept on (GameScene.Skills).
    public PristonTale.Gameplay.Effects.SkillPresentation SkillState { get; set; }
    public double SkillClockMs { get; set; }
    public uint SelfId { get; set; }

    // A double click on a summon's buff icon asks to switch the summon's fighting mode.
    public event Action<uint> SummonModeRequested;
    private uint _lastBuffClicked;
    private double _lastBuffClickSeconds;

    public void ShowNotice(string message) => _notice.Show(message);

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
            _class = value;
            _syncedSkills = null;
            _skillHud.SetClass(value);
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
            || _inventorySlide.Open || _statusSlide.Open || _skillSlide.Open || _shop.IsOpen || _warehouse.IsOpen;
        CloseServices();
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
        _services.Update();
        _npc.Update(_context.Font);
        _minimap.Update(_sprites, World, elapsedSeconds);
        _notice.Update(elapsedSeconds);
        _inventorySlide.Advance(elapsedSeconds);
        _statusSlide.Advance(elapsedSeconds);
        _skillSlide.Advance(elapsedSeconds);
        _shop.Advance(elapsedSeconds);
        _warehouse.Advance(elapsedSeconds);
        ForwardMessages();
        ApplyServices(state);
        if (_npc.ReadyToOpen)
        {
            _skillMaster = 0;
            _skills.CancelTraining();
            _settingsOpen = false;
            CloseServices();
            // While the previous window's close is still in flight the dialog shows, and its button opens the service.
            var direct = _npc.DirectService;
            _npcOpen = direct is null || !_services.Request(_npc.Current.EntityId, _npc.Current.Name, direct.Kind, NpcServiceAction.Open);
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
        if (state is not null && !ReferenceEquals(state.Skills, _syncedSkills))
        {
            _syncedSkills = state.Skills;
            _bindings.SyncLearned(SkillClasses.Get(_class).Skills, state.Skills);
        }

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
        var warehouseAction = _warehouse.Update(mouse, _previousMouse, pointer, InventoryCellAt, _services.Pending || _settingsOpen);
        if (warehouseAction.Kind != HudWarehouseActionKind.None)
        {
            ApplyWarehouseAction(state, warehouseAction);
        }
        else if (clicked)
        {
            Click(state, layout, pointer, design, keyboard);
        }

        var skillOrigin = PanelOrigin(layout, _skillSlide);
        var hoveredSkill = _skillSlide.FullyOpen && _skillMaster == 0 ? _skills.HoveredSkill(skillOrigin, pointer) : 0;
        _bindings.Update(keyboard, mouse, hoveredSkill);
        PointerCaptured = Captures(layout, pointer, design) || _inventory.Dragging != Guid.Empty || _warehouse.Dragging != Guid.Empty;
        _inventory.Update(state, layout, _inventorySlide.FullyOpen && !_settingsOpen, keyboard, mouse);
        _previousKeyboard = keyboard;
        _previousMouse = mouse;
    }

    private bool ClickBuff(Point pointer)
    {
        if (SkillState is null || SelfId == 0)
        {
            return false;
        }

        var code = HudSkillHud.BuffAt(SkillState.Statuses.For(SelfId), pointer);
        if (code == 0)
        {
            return false;
        }

        var now = _clock.Elapsed.TotalSeconds;
        if (code == _lastBuffClicked && now - _lastBuffClickSeconds < 0.4 && PristonTale.Simulation.Skills.SkillRuleCatalog.TryGet(code, out var rule) && rule.Summon is not null)
        {
            _lastBuffClicked = 0;
            SummonModeRequested?.Invoke(code);
            return true;
        }

        _lastBuffClicked = code;
        _lastBuffClickSeconds = now;
        return true;
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

        if (_services.Completions != _serviceCompletions)
        {
            _serviceCompletions = _services.Completions;
            _notice.Show(_services.Message);
        }

        if (_bindings.Refusals != _bindingRefusals)
        {
            _bindingRefusals = _bindings.Refusals;
            _notice.Show(_bindings.Refusal);
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

        if (_npcOpen && ClickNpc(state, layout, pointer))
        {
            return;
        }

        if (_shop.Contains(pointer))
        {
            ClickShop(state, pointer);
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

        if (ClickBuff(pointer))
        {
            return;
        }

        if (_hoveredToggle == HudToggle.Map)
        {
            _mapVisible = !_mapVisible;
            return;
        }

        ClickPanels(state, layout, pointer);
    }

    private bool ClickNpc(OwnerState state, HudLayout layout, Point pointer)
    {
        var current = _npc.Current;
        if (_npc.CanTrain && _npc.Destinations.Count == 0 && HudNpcPanel.TrainingButton(layout).Contains(pointer))
        {
            _services.Request(current.EntityId, current.Name, NpcServiceKind.SkillMaster, NpcServiceAction.Open);
            return true;
        }

        if (_npc.DestinationAt(pointer) is { } destination && state is not null)
        {
            _services.Request(current.EntityId, current.Name, NpcServiceKind.Teleporter, NpcServiceAction.Teleport, state.InventoryRevision,
                anchor: destination.Index);
            return true;
        }

        if (_npc.ServiceAt(pointer) is { } service)
        {
            _services.Request(current.EntityId, current.Name, service.Kind, NpcServiceAction.Open);
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
        if (HudBar.Captures(design) || MinimapShown && HudMinimap.Box(layout).Contains(pointer)
            || WindowContains(layout, _inventorySlide, pointer) || WindowContains(layout, _statusSlide, pointer) || WindowContains(layout, _skillSlide, pointer))
        {
            return true;
        }

        if (_shop.Contains(pointer) || _warehouse.Contains(pointer))
        {
            return true;
        }

        return _systemMenu && HudSystemMenu.Bounds.Contains(design)
            || _settingsOpen && HudSettingsPanel.Box.Contains(pointer)
            || _npcOpen && (HudNpcPanel.Box.Contains(pointer) || _npc.CanTrain && HudNpcPanel.SkillMasterBox(layout).Contains(pointer))
            || _skills.TrainingPending && HudMessageBox.Bounds.Contains(pointer);
    }

    private bool MinimapShown => _mapVisible && !World.MapHidden;

    // Renders the target frame's 3D portrait into its own target. Call before the world and HUD draw of the frame: binding
    // another target discards the back buffer.
    public void RenderPortrait()
    {
        var subject = TargetId != 0 && Combat.TryGetPortraitSubject(TargetId, out var target) ? target : default;
        _portrait.Render(PortraitScene, subject, Layout());
    }

    public void Draw()
    {
        var layout = Layout();
        var pointer = layout.ToLogical(_previousMouse.Position);
        var pressed = _previousMouse.LeftButton == ButtonState.Pressed;
        _painter.Begin(layout);
        if (MinimapShown)
        {
            Combat.CollectNpcPositions(_minimapNpcs);
            _minimap.Draw(_painter, layout, World, _minimapNpcs, HudMinimap.Box(layout).Contains(pointer));
        }

        Combat.DrawTarget(_painter, layout, TargetId, _context.Presentation.Requested.ClassicTargetHealthBar, _portrait.Texture(TargetId));
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

        if (_shop.Visible)
        {
            _shop.Draw(_painter, _icons, _state, pointer, _shop.FullyOpen && !_settingsOpen);
        }

        if (_warehouse.Visible)
        {
            var incoming = _inventory.Dragging == Guid.Empty ? null : _state?.Items.FirstOrDefault(item => item.InstanceId == _inventory.Dragging);
            _warehouse.Draw(_painter, _icons, pointer, incoming, _warehouse.FullyOpen && !_settingsOpen);
        }

        _painter.Flush();
        var left = _bindings.LeftSkillId == HudSkillBindings.BasicAttackId;
        _bar.Draw(_painter, layout, _state, new HudBarView(World.TimeOfDay, World.Running, _mapVisible, left, RightAttackEnabled,
            _hoveredButton, pressed && _hoveredButton is not null, _hoveredToggle));
        _skillHud.DrawSockets(_painter, layout.BottomOffset, _bindings.LeftSkillId, _bindings.RightSkillId, SkillState?.Cooldowns, SkillClockMs);
        if (SkillState is not null && SelfId != 0)
        {
            _skillHud.DrawBuffs(_painter, SkillState.Statuses.For(SelfId), SkillClockMs);
        }

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

        if (_shop.FullyOpen)
        {
            _shop.DrawOverlay(_painter, layout, pointer);
        }

        if (_warehouse.FullyOpen)
        {
            _warehouse.DrawOverlay(_painter, layout, _icons, pointer);
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

    // A service the server opened shows its window; the NPC dialog gives way to it, as the original opens services directly.
    private void ApplyServices(OwnerState state)
    {
        if (_services.Opened != _serviceOpened)
        {
            _serviceOpened = _services.Opened;
            OpenService();
        }

        if (_services.Open != NpcServiceKind.Shop && _shop.IsOpen)
        {
            _shop.Hide();
        }

        if (_services.Open != NpcServiceKind.Warehouse && _warehouse.IsOpen)
        {
            _warehouse.Hide();
        }

        if (_warehouse.IsOpen && _services.Warehouse is { } view && !ReferenceEquals(view, _warehouse.View))
        {
            _warehouse.Refresh(view);
            _icons.Request(view.Items);
        }

        if (!_services.Pending)
        {
            _services.RefreshQuotes(state);
        }
    }

    private void OpenService()
    {
        switch (_services.Open)
        {
            case NpcServiceKind.Shop:
                _npcOpen = false;
                _shop.Show(_services.Catalog);
                _icons.Request(_shop.IconKeys);
                OpenOnly(_inventorySlide);
                break;
            case NpcServiceKind.Warehouse when _services.Warehouse is { } view:
                _npcOpen = false;
                _warehouse.Show(view);
                _icons.Request(view.Items);
                OpenOnly(_inventorySlide);
                break;
            case NpcServiceKind.Teleporter:
                _npc.ShowDestinations(_services.Destinations);
                break;
            case NpcServiceKind.SkillMaster:
                _npcOpen = false;
                _skillMaster = _services.NpcId;
                OpenOnly(_skillSlide);
                break;
        }
    }

    private void CloseServices()
    {
        _services.Close();
        _shop.Hide();
        _warehouse.Hide();
    }

    private void ClickShop(OwnerState state, Point pointer)
    {
        switch (_shop.Click(pointer))
        {
            case HudShopAction.Exit:
                CloseServices();
                break;
            case HudShopAction.Buy when _shop.Selected is { } offer && state is not null:
                _services.Request(_services.NpcId, _services.NpcName, NpcServiceKind.Shop, NpcServiceAction.Buy, state.InventoryRevision, offer.Code);
                break;
            case HudShopAction.RepairAll when state is not null:
                _services.Request(_services.NpcId, _services.NpcName, NpcServiceKind.Shop, NpcServiceAction.RepairAll, state.InventoryRevision);
                break;
        }
    }

    private bool InterceptInventoryClick(OwnedItemView item)
    {
        if (!_shop.FullyOpen || _shop.Mode == HudShopMode.Buy || _state is null)
        {
            return false;
        }

        if (_shop.Mode == HudShopMode.Sell && item.Slot >= 0)
        {
            return true;
        }

        var action = _shop.Mode == HudShopMode.Sell ? NpcServiceAction.Sell : NpcServiceAction.Repair;
        _services.Request(_services.NpcId, _services.NpcName, NpcServiceKind.Shop, action, _state.InventoryRevision, itemId: item.InstanceId);
        return true;
    }

    // An inventory item dropped on the shop is sold; on the warehouse grid it is stored in the cell under the pointer.
    private bool DropFromInventory(Guid itemId, Point pointer)
    {
        if (_state is null)
        {
            return false;
        }

        if (_shop.FullyOpen && _shop.Contains(pointer))
        {
            _services.Request(_services.NpcId, _services.NpcName, NpcServiceKind.Shop, NpcServiceAction.Sell, _state.InventoryRevision, itemId: itemId);
            return true;
        }

        var cell = HudStoreLayout.CellAt(_warehouse.Origin, pointer);
        if (_warehouse.FullyOpen && cell >= 0)
        {
            _services.Request(_services.NpcId, _services.NpcName, NpcServiceKind.Warehouse, NpcServiceAction.Deposit, _state.InventoryRevision,
                itemId: itemId, anchor: cell);
            return true;
        }

        return false;
    }

    private void ApplyWarehouseAction(OwnerState state, HudWarehouseAction action)
    {
        var revision = state?.InventoryRevision ?? 0;
        switch (action.Kind)
        {
            case HudWarehouseActionKind.Exit:
                CloseServices();
                break;
            case HudWarehouseActionKind.Move:
                _services.Request(_services.NpcId, _services.NpcName, NpcServiceKind.Warehouse, NpcServiceAction.Move, revision,
                    itemId: action.ItemId, anchor: action.Anchor);
                break;
            case HudWarehouseActionKind.Withdraw:
                _services.Request(_services.NpcId, _services.NpcName, NpcServiceKind.Warehouse, NpcServiceAction.Withdraw, revision,
                    itemId: action.ItemId, anchor: action.Anchor);
                break;
        }
    }

    private int InventoryCellAt(Point pointer)
    {
        if (!_inventorySlide.FullyOpen)
        {
            return -1;
        }

        return HudInventoryLayout.CellAt(PanelOrigin(Layout(), _inventorySlide), pointer);
    }

    private string InventoryNote(OwnedItemView item)
    {
        if (!_shop.FullyOpen || _services.QuoteFor(item.InstanceId) is not { } quote)
        {
            return "";
        }

        if (_shop.Mode == HudShopMode.Repair)
        {
            return quote.RepairCost > 0 ? $"Repair: {quote.RepairCost:N0} gold" : "Not damaged";
        }

        return quote.SellPrice >= 0 ? $"Sells for {quote.SellPrice:N0} gold" : "The shop does not buy this";
    }

    private bool Pressed(KeyboardState state, Keys key) => state.IsKeyDown(key) && !_previousKeyboard.IsKeyDown(key);

    public void Dispose()
    {
        _portrait.Dispose();
        _icons.Dispose();
        _painter.Dispose();
        _sprites.Dispose();
    }
}
