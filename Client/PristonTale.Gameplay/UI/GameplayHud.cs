using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Npc;
using PristonTale.Gameplay.UI.Quests;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Travel;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Gameplay.UI;

// The original in-game interface in its draw order: speech bubbles, the chat window, status, skill, inventory and quest
// windows behind the bottom bar, then item overlays, help boxes and message boxes. Party and the system menu entries
// other than Settings have no server support and are drawn without behaviour. Refusals show in the notice strip;
// confirmations and server lines go to the chat.
public sealed class GameplayHud : IDisposable
{
    private readonly ScreenContext _context;
    private readonly HudSprites _sprites;
    private readonly HudPainter _painter;
    private readonly HudItemIcons _icons;
    private readonly Dictionary<Guid, string> _itemKeys = new();
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
    private readonly HudAgingWindow _aging = new();
    private readonly HudQuestController _quests;
    private readonly HudQuestDocuments _questDocuments;
    private readonly HudQuestDialog _questDialog = new();
    private readonly HudQuestWindow _questWindow = new();
    private QuestView _questView;
    private double _questViewMs;
    private int _questCompletions;
    private int _questDialogs;
    private readonly HudSettingsPanel _settings;
    private readonly HudMinimap _minimap = new();
    private readonly List<Vector3> _minimapNpcs = new(64);
    private readonly HudTargetPortrait _portrait;
    private readonly HudNotice _notice = new();
    private readonly HudChat _chat = new();
    private readonly HudSlide _inventorySlide = new();
    private readonly HudSlide _statusSlide = new();
    private readonly HudSlide _skillSlide = new();
    private readonly HudItemUseController _itemUse;
    private readonly HudPremiumTimers _premium;
    private readonly HudPremiumKeeps _premiumKeeps = new();
    private readonly HudPuzzleExchange _puzzle = new();
    private readonly HudRestartPanel _restart = new();
    private readonly HudWingGate _wingGate;
    private int _itemUseCompletions;
    private int _wingGateCompletions;
    private int _shopsOpened;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private KeyboardState _previousKeyboard;
    private MouseState _previousMouse;
    private OwnerState _state;
    private HudMenuButton? _hoveredButton;
    private HudToggle? _hoveredToggle;
    private ulong _skillMaster;
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
        Func<ulong, Task<NpcInteraction>> interactNpc = null,
        Action<NpcInteraction> npcOpened = null,
        Action npcClosed = null,
        Func<NpcServiceCommand, Task<NpcServiceReply>> requestNpcService = null,
        Func<QuestCommand, Task<QuestOutcome>> requestQuest = null)
    {
        _context = context;
        _settings = new HudSettingsPanel(context.Presentation);
        _sprites = new HudSprites(context.GraphicsDevice, context.AssetRoot, context.Log);
        _sprites.Request(HudArt.Static);
        _sprites.Request(HudNpcArt.All);
        _sprites.Request(HudCraftArt.All);
        _sprites.Request(HudArt.BlockText);
        _sprites.Request(HudQuestArt.All);
        _painter = new HudPainter(context.SpriteBatch, context.Font, _sprites);
        _icons = new HudItemIcons(context.GraphicsDevice, context.AssetRoot, context.Log);
        _skills = new HudSkillsPanel(context.Font, _sprites);
        _skillHud = new HudSkillHud(_sprites, context.GraphicsDevice);
        _inventory = new HudInventoryController(context, applyInventory);
        _progression = new HudProgressionController(applyProgression, context.Log);
        _npc = new HudNpcPanel(interactNpc, context.Log, npcOpened, npcClosed);
        _portrait = new HudTargetPortrait(context.GraphicsDevice);
        _services = new HudNpcServiceController(requestNpcService, context.Log);
        _itemUse = new HudItemUseController(UseItemAsync, context.Log);
        _premium = new HudPremiumTimers(_sprites);
        _sprites.Request(HudTeleportCore.Art);
        _wingGate = new HudWingGate(context.Log);
        _sprites.Request(HudWingGate.Art);
        _inventory.UseRequested = item => _itemUse.Request(item, _state);
        _quests = new HudQuestController(requestQuest, context.Log);
        _questDocuments = new HudQuestDocuments(context.AssetRoot, context.Log);
        _inventory.InterceptClick = InterceptInventoryClick;
        _inventory.ExternalDrop = DropFromInventory;
        _inventory.ItemSound = key => ItemSound?.Invoke(key);
        _services.Succeeded = ServiceSound;
        _inventoryPanel.Note = InventoryNote;
        _inventoryPanel.Withheld = _aging.Holds;
        _sprites.Request(HudChatWindow.Art);
        _sprites.Request(HudChatBubbles.Art);
        _chat.Lookup = id => (Combat.KindOf(id, out var owner), owner);
    }

    // The chat window, its input line and the speech bubbles.
    public HudChat Chat => _chat;

    // The chat part of each presence update (server lines, whispers, notices, the close request).
    public void ObserveChat(PristonTale.Client.Net.Presence.PresenceUpdate update) => _chat.Observe(update, _clock.Elapsed.TotalMilliseconds);

    // The input line has the keyboard: game keys must not act on typed letters.
    public bool KeyboardCaptured => _chat.KeyboardCaptured;

    // Where a speaker's bubble hangs on screen, from the scene; null when the speaker is not drawn.
    public Func<ulong, HudOverheadPoint?> Overhead { get; set; } = static _ => null;

    public HudCombat Combat { get; } = new();
    public HudStringEffect StringEffect { get; } = new();

    // Uses a carried item through the server; set by the screen.
    public Func<PristonTale.Simulation.Items.Usable.ItemUseCommand, Task<PristonTale.Simulation.Items.Usable.ItemUseOutcome>> UseItem { get; set; }

    // The local player's running premium timers as the server last sent them.
    public PristonTale.Client.Net.Presence.PremiumTimerSnapshot PremiumTimers { get; set; }
    public ulong TargetId { get; set; }
    public HudWorldView World { get; set; }

    // The player stands in a castle field, where a Restoration revives.
    public bool InCastleField { get; set; }

    // The local player is down, and what standing up would cost (from the server).
    public bool PlayerDead { get; set; }
    public RestartOffer? RestartOffer { get; set; }

    // The restart window; the screen sets its request and quits on its exit choice.
    public HudRestartPanel Restart => _restart;

    // The wing warp map a warp stone opens; the screen opens it and sets its trip.
    public HudWingGate WingGate => _wingGate;
    public IHudPortraitScene PortraitScene { get; set; }
    public bool PortraitReady => _portrait.Texture(TargetId) is not null;
    public float MinimapViewSize => _minimap.CurrentViewSize;
    public bool PointerCaptured { get; private set; }
    public bool AssetsFailed => _sprites.Failed || _icons.Failed;
    public bool IconsIdle => _icons.IsIdle && _sprites.IsIdle && !_questDocuments.IsLoading;
    public long TextureBytes => _sprites.TextureBytes;
    public bool RightAttackEnabled => _bindings.RightSkillId == HudSkillBindings.BasicAttackId;
    internal uint LeftSkillId => _bindings.LeftSkillId;
    internal HudItemUseController ItemUse => _itemUse;
    internal HudPuzzleExchange Puzzle => _puzzle;
    internal uint RightSkillId => _bindings.RightSkillId;
    public HudSkillBindings Bindings => _bindings;

    // Cooldowns and active effects of the local player, and the clock they are kept on (GameScene.Skills).
    // The client's view of statuses and cooldowns from the server's skill messages.
    public PristonTale.Gameplay.Effects.StatusBook Statuses { get; set; }
    public PristonTale.Gameplay.Effects.SkillCooldownBook Cooldowns { get; set; }
    public double SkillClockMs { get; set; }
    public ulong SelfId { get; set; }

    // A double click on a summon's buff icon asks to switch the summon's fighting mode.
    public event Action<uint> SummonModeRequested;

    // R or the bar's walk/run button.
    public event Action RunToggleRequested;

    // The fighting mode the player chose for a running summon skill (true: automatic), or null for other skills.
    public Func<uint, bool?> SummonAutomatic { get; set; }
    private uint _lastBuffClicked;
    private double _lastBuffClickSeconds;

    public void ShowNotice(string message) => _notice.Show(message);

    // The quest fanfare: a quest started, moved on or ended.
    public event Action QuestFanfare;

    public event Action<bool> CraftResult;

    // An item lifted, set, bought, sold, stored or taken back, by catalog key, for its sound.
    public event Action<string> ItemSound;

    public HudQuestController Quests => _quests;
    public HudQuestWindow QuestWindow => _questWindow;

    // The quest part of each presence update (the journal, quest notices and dialogs the world opened).
    public void ObserveQuests(PristonTale.Client.Net.Presence.PresenceUpdate update) => _quests.Observe(update);

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

    public void OpenNpc(ulong entityId) => _npc.Request(entityId);

    public bool ClosePanel()
    {
        // As in the original, Escape first closes an open chat window and nothing else.
        if (_chat.Window.Open && !_settingsOpen)
        {
            _chat.Window.Close();
            return true;
        }

        _npc.Dismiss();
        var closed = _npcOpen || _settingsOpen || _systemMenu || _skills.TrainingPending
            || _inventorySlide.Open || _statusSlide.Open || _skillSlide.Open || _shop.IsOpen || _warehouse.IsOpen || _aging.IsOpen
            || _questWindow.Open || _quests.Dialog is not null || _quests.SkillResetBox;
        CloseServices();
        closed |= _itemUse.Teleport.IsOpen || _itemUse.Confirming || _puzzle.Confirming || _wingGate.IsOpen || _wingGate.Confirming;
        _itemUse.Close();
        _wingGate.Close();
        _puzzle.Close();
        _quests.CloseDialog();
        _quests.AnswerSkillReset(false);
        _quests.LeaveNpc();
        _questWindow.Close();
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
        _itemUse.Update();
        _wingGate.Update();
        _itemUse.Restore(state, InCastleField, _clock.Elapsed.TotalMilliseconds);
        _restart.Update(PlayerDead, _clock.Elapsed.TotalMilliseconds);
        _premium.Apply(PremiumTimers, _clock.Elapsed.TotalMilliseconds);
        _quests.Update();
        _npc.Update(_context.Font);
        _minimap.Update(_sprites, World, elapsedSeconds);
        _notice.Update(elapsedSeconds);
        StringEffect.Update((float)elapsedSeconds);
        _inventorySlide.Advance(elapsedSeconds);
        _statusSlide.Advance(elapsedSeconds);
        _skillSlide.Advance(elapsedSeconds);
        _shop.Advance(elapsedSeconds);
        _warehouse.Advance(elapsedSeconds);
        _aging.Advance(elapsedSeconds);
        _questWindow.Advance(elapsedSeconds);
        ForwardMessages();
        ApplyQuests();
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
            _quests.LeaveNpc();
            if (_npc.TakesPartInQuests)
            {
                _quests.Talk(_npc.Current.EntityId);
            }
        }

        var layout = Layout();
        var pointer = layout.ToLogical(mouse.Position);
        var design = new Point(pointer.X, pointer.Y - layout.BottomOffset);
        if (_quests.Dialog is not null && HudQuestDialog.Box.Contains(pointer))
        {
            _questDialog.Scroll(mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue);
        }
        else if (_npcOpen && HudNpcPanel.Box.Contains(pointer))
        {
            _npc.Scroll(mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue);
        }

        if (state is not null && (_state is null || state.InventoryRevision != _state.InventoryRevision))
        {
            RequestIcons(state);
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
        _chat.Update(keyboard, _previousKeyboard, _context.TypedText.Drain(), _clock.Elapsed.TotalMilliseconds, _chat.Window.Log.Tab);
        var typing = _chat.KeyboardCaptured;
        if (!typing)
        {
            UpdateKeys(keyboard);
        }

        var warehouseAction = _warehouse.Update(mouse, _previousMouse, pointer, InventoryCellAt, _services.Pending || _settingsOpen);
        var agingAction = _aging.Update(mouse, _previousMouse, pointer, state, _services.Pending || _settingsOpen);
        if (warehouseAction.Kind != HudWarehouseActionKind.None)
        {
            ApplyWarehouseAction(state, warehouseAction);
        }
        else if (agingAction != HudWorkActionKind.None)
        {
            ApplyAgingAction(state, agingAction);
        }
        else if (clicked)
        {
            Click(state, layout, pointer, design, keyboard);
        }

        var skillOrigin = PanelOrigin(layout, _skillSlide);
        _skills.RefreshRanks(_state);
        var hoveredSkill = _skillSlide.FullyOpen && _skillMaster == 0 ? _skills.HoveredSkill(skillOrigin, pointer) : 0;
        _bindings.Update(typing ? default : keyboard, mouse, hoveredSkill);
        PointerCaptured = Captures(layout, pointer, design) || _chat.Window.Contains(layout, pointer, SideWindowOpen) || _inventory.Dragging != Guid.Empty || _warehouse.Dragging != Guid.Empty;
        _inventory.Update(state, layout, _inventorySlide.FullyOpen && !_settingsOpen, keyboard, mouse);
        _previousKeyboard = keyboard;
        _previousMouse = mouse;
    }

    private bool ClickBuff(Point pointer)
    {
        if (Statuses is null || SelfId == 0)
        {
            return false;
        }

        var code = _skillHud.BuffAt(pointer);
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
            Report(_inventory.LastOk, _inventory.Message);
        }

        if (_progression.Completions != _progressionCompletions)
        {
            _progressionCompletions = _progression.Completions;
            Report(_progression.LastOk, _progression.Message);
        }

        if (_services.Completions != _serviceCompletions)
        {
            _serviceCompletions = _services.Completions;
            Report(_services.LastOk, _services.Message);
            ShowAgingResult();
        }

        if (_itemUse.Completions != _itemUseCompletions)
        {
            _itemUseCompletions = _itemUse.Completions;
            Report(_itemUse.LastOutcome?.Ok == true, _itemUse.Message);
        }

        if (_wingGate.Completions != _wingGateCompletions)
        {
            _wingGateCompletions = _wingGate.Completions;
            Report(_wingGate.LastOutcome?.Ok == true, _wingGate.Message);
        }

        if (_itemUse.ShopsOpened != _shopsOpened)
        {
            _shopsOpened = _itemUse.ShopsOpened;
            CloseServices();
            _services.Request(0, "Mobile Shop", NpcServiceKind.Shop, NpcServiceAction.Open);
        }

        if (_quests.Completions != _questCompletions)
        {
            _questCompletions = _quests.Completions;
            _notice.Show(_quests.Message);
        }

        if (_bindings.Refusals != _bindingRefusals)
        {
            _bindingRefusals = _bindings.Refusals;
            _chat.System(_bindings.Refusal);
        }
    }

    // A refusal shows in the notice strip as the classic message box; a confirmation (which the classic client did not show)
    // goes to the chat as a server line. The server's "committed" carries no information and is dropped.
    private void Report(bool ok, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (!ok)
        {
            _notice.Show(message);
        }
        else if (message != "committed")
        {
            _chat.System(message);
        }
    }

    // A status, skill or inventory window shrinks the chat window above it.
    private bool SideWindowOpen => _inventorySlide.Visible || _statusSlide.Visible || _skillSlide.Visible;

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

        if (Pressed(keyboard, Keys.Q))
        {
            _questWindow.Toggle();
        }

        if (Pressed(keyboard, Keys.R))
        {
            RunToggleRequested?.Invoke();
        }
    }

    // Quest notices go to the combat log (chat lines), the notice strip (events and messages) or the fanfare; a new quest
    // dialog takes the help box from the NPC dialog until it closes.
    private void ApplyQuests()
    {
        while (_quests.TryTakeNotice(out var notice))
        {
            switch (notice.Kind)
            {
                case QuestNoticeKind.Chat:
                    _chat.Assist(notice.Text);
                    break;
                case QuestNoticeKind.Fanfare:
                    QuestFanfare?.Invoke();
                    break;
                default:
                    _notice.Show(notice.Text);
                    break;
            }
        }

        if (_quests.DialogsOpened != _questDialogs)
        {
            _questDialogs = _quests.DialogsOpened;
            _skills.CancelTraining();
            _settingsOpen = false;
            foreach (var document in _quests.Dialog?.ChoiceDocuments ?? [])
            {
                _questDocuments.Lines(HudQuestDocuments.DialogPath(document));
            }
        }

        // Quest texts load off the game thread; asking early has them ready when the dialog or window draws.
        if (_quests.Dialog is not null)
        {
            _questDocuments.Lines(HudQuestDocuments.DialogPath(_quests.DialogDocument));
        }

        if (!ReferenceEquals(_quests.View, _questView))
        {
            _questView = _quests.View;
            _questViewMs = _clock.Elapsed.TotalMilliseconds;
            _questWindow.Refresh(_questView);
            foreach (var entry in _questView is null ? [] : _questView.Progress.Concat(_questView.Finished))
            {
                _questDocuments.Lines(HudQuestDocuments.BoardPath(entry.Description));
            }
        }
    }


    private long QuestTimeLeftMs => _questView is null ? 0 : Math.Max(0, _questView.TimerMs - (long)(_clock.Elapsed.TotalMilliseconds - _questViewMs));

    // The quest dialog, its yes/no box and the skill redistribution offer are modal: they take every click.
    private bool ClickQuests(HudLayout layout, Point pointer)
    {
        if (_quests.SkillResetBox)
        {
            if (HudMessageBox.Accept.Contains(pointer) || HudMessageBox.Reject.Contains(pointer))
            {
                _quests.AnswerSkillReset(HudMessageBox.Accept.Contains(pointer));
            }

            return true;
        }

        if (_quests.Confirming)
        {
            if (HudMessageBox.Accept.Contains(pointer) || HudMessageBox.Reject.Contains(pointer))
            {
                _quests.Answer(HudMessageBox.Accept.Contains(pointer));
            }

            return true;
        }

        if (_quests.Dialog is { } dialog)
        {
            if (HudQuestDialog.OkButton.Contains(pointer))
            {
                _quests.Ok();
            }
            else if (HudQuestDialog.ChoiceAt(dialog, pointer) is var choice and >= 0)
            {
                _quests.Choose(choice);
            }
            else if (dialog.Followup == QuestFollowup.ConfirmChoice && HudQuestDialog.PreviousArrow.Contains(pointer))
            {
                _quests.BrowseBy(-1);
            }
            else if (dialog.Followup == QuestFollowup.ConfirmChoice && HudQuestDialog.NextArrow.Contains(pointer))
            {
                _quests.BrowseBy(1);
            }

            return HudQuestDialog.Box.Contains(pointer);
        }

        if (_npcOpen && _npc.CanTrain && _npc.Destinations.Count == 0 && HudNpcPanel.ClassChangeButton(layout).Contains(pointer)
            && _quests.ClassChangeNpc == _npc.Current?.EntityId)
        {
            _quests.ChangeClass(_quests.ClassChangeNpc);
            return true;
        }

        return _questWindow.Click(layout, pointer, _questView);
    }

    private void Click(OwnerState state, HudLayout layout, Point pointer, Point design, KeyboardState keyboard)
    {
        if (_restart.Click(layout, pointer, RestartOffer?.FieldOpen == true))
        {
            return;
        }

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

        if (_chat.Window.Click(layout, pointer, SideWindowOpen))
        {
            return;
        }

        if (_itemUse.Click(pointer, state) || _wingGate.Click(pointer, state) || _puzzle.Click(pointer, state, _services))
        {
            return;
        }

        if (_skills.TrainingPending)
        {
            _skills.HandleTrainingClick(PanelOrigin(layout, _skillSlide), pointer, state, _skillMaster, _progression);
            return;
        }

        if (ClickQuests(layout, pointer))
        {
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

        if (_hoveredToggle == HudToggle.Walk)
        {
            RunToggleRequested?.Invoke();
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
            _quests.LeaveNpc();
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
            case HudMenuButton.Quest:
                _questWindow.Toggle();
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
        if (_itemUse.Captures(pointer) || _wingGate.Captures(pointer) || _puzzle.Captures(pointer) || _premium.Contains(pointer))
        {
            return true;
        }

        if (HudBar.Captures(design) || MinimapShown && HudMinimap.Box(layout).Contains(pointer) || _skillHud.BuffAt(pointer) != 0
            || WindowContains(layout, _inventorySlide, pointer) || WindowContains(layout, _statusSlide, pointer) || WindowContains(layout, _skillSlide, pointer))
        {
            return true;
        }

        if (_shop.Contains(pointer) || _warehouse.Contains(pointer) || _aging.Contains(pointer))
        {
            return true;
        }

        if (_quests.SkillResetBox || _quests.Confirming || _quests.Dialog is not null && HudQuestDialog.Box.Contains(pointer)
            || _questWindow.Contains(layout, pointer) || _skillHud.QuestTimerAt(pointer))
        {
            return true;
        }

        return _restart.Open && HudRestartPanel.Box(layout).Contains(pointer)
            || _systemMenu && HudSystemMenu.Bounds.Contains(design)
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
        _chat.Bubbles.Draw(_painter, layout, _clock.Elapsed.TotalMilliseconds, Overhead, id => _chat.NameColor(id, TargetId), TargetId);
        _painter.Flush();
        if (MinimapShown)
        {
            Combat.CollectNpcPositions(_minimapNpcs);
            _minimap.Draw(_painter, layout, World, _minimapNpcs, HudMinimap.Box(layout).Contains(pointer));
        }

        Combat.DrawTarget(_painter, layout, TargetId, _context.Presentation.Requested.ClassicTargetHealthBar, _portrait.Texture(TargetId));
        _premium.Draw(_painter, _clock.Elapsed.TotalMilliseconds);
        _chat.Window.Draw(_painter, layout, SideWindowOpen);

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
            _inventoryPanel.ClockMs = _clock.Elapsed.TotalMilliseconds;
            _inventoryPanel.Draw(_painter, PanelOrigin(layout, _inventorySlide), _state, _inventory, _icons, pointer, _inventorySlide.FullyOpen);
        }

        if (_questWindow.Visible)
        {
            _questWindow.Draw(_painter, layout, _context.Font, _questView, _questDocuments, pointer);
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

        if (_aging.Visible)
        {
            var incoming = _inventory.Dragging == Guid.Empty ? null : _state?.Items.FirstOrDefault(item => item.InstanceId == _inventory.Dragging);
            _aging.Draw(_painter, _icons, pointer, _state, incoming, _aging.FullyOpen && !_settingsOpen);
        }

        _painter.Flush();
        var left = _bindings.LeftSkillId == HudSkillBindings.BasicAttackId;
        _bar.Draw(_painter, layout, _state, new HudBarView(World.TimeOfDay, World.Running, _mapVisible, left, RightAttackEnabled,
            _hoveredButton, pressed && _hoveredButton is not null, _hoveredToggle));
        _skillHud.DrawSockets(_painter, layout.BottomOffset, _bindings.LeftSkillId, _bindings.RightSkillId, Cooldowns, SkillClockMs);
        if (Statuses is not null && SelfId != 0)
        {
            _skillHud.DrawBuffs(_painter, _premiumKeeps.With(Statuses.For(SelfId), PremiumTimers, SkillClockMs), SkillClockMs);
        }
        else
        {
            _skillHud.ClearBuffs();
        }

        _skillHud.DrawQuestTimer(_painter, _questView, QuestTimeLeftMs);

        StringEffect.Draw(_painter, layout);
        _bar.DrawHoverText(_painter, layout, _state, pointer);
        if (_systemMenu)
        {
            HudSystemMenu.Draw(_painter, layout, pointer);
        }

        // The quest dialog takes the help box from the NPC dialog; its yes/no box replaces both, as the classic client closed
        // the help box before asking.
        if (_quests.Dialog is not null)
        {
            if (!_quests.Confirming)
            {
                _painter.Flush();
                _questDialog.Draw(_painter, _context.Font, _quests, _questDocuments, pointer);
            }
        }
        else if (_npcOpen)
        {
            _painter.Flush();
            _npc.Draw(_painter, layout, pointer, true, _quests.ClassChangeNpc != 0 && _quests.ClassChangeNpc == _npc.Current?.EntityId);
        }

        if (_itemUse.Teleport.IsOpen || _itemUse.Confirming)
        {
            _painter.Flush();
            _itemUse.Draw(_painter, pointer);
        }

        if (_wingGate.IsOpen || _wingGate.Confirming)
        {
            _painter.Flush();
            _wingGate.Draw(_painter, _context.Font, _questDocuments, _state, pointer, _clock.Elapsed.TotalMilliseconds);
        }

        _puzzle.Draw(_painter, pointer);

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

        if (_aging.FullyOpen)
        {
            _aging.DrawOverlay(_painter, layout, pointer, _state);
        }

        if (_inventorySlide.FullyOpen)
        {
            _inventoryPanel.DrawOverlay(_painter, layout, _icons, pointer);
        }

        if (_skillSlide.FullyOpen)
        {
            _skills.DrawOverlay(_painter, layout, pointer, _skillMaster, _progression.Message);
        }

        if (Statuses is not null && SelfId != 0)
        {
            _skillHud.DrawBuffTooltip(_painter, _premiumKeeps.With(Statuses.For(SelfId), PremiumTimers, SkillClockMs), pointer, SkillClockMs,
                SummonAutomatic);
        }

        _skillHud.DrawQuestTimerTooltip(_painter, _questView, QuestTimeLeftMs, pointer);
        if (_quests.Confirming)
        {
            _painter.Flush();
            HudQuestDialog.DrawConfirm(_painter, _quests, pointer);
        }
        else if (_quests.SkillResetBox)
        {
            _painter.Flush();
            HudQuestDialog.DrawBox(_painter, QuestTexts.SkillPointName, QuestTexts.Redistribute.Item1, QuestTexts.Redistribute.Item2, pointer);
        }

        _painter.Flush();
        _premium.DrawHover(_painter, pointer, _clock.Elapsed.TotalMilliseconds);
        _painter.Flush();
        _restart.Draw(_painter, layout, pointer, RestartOffer?.FieldOpen == true, InCastleField);
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

        if (_services.Open != NpcServiceKind.ItemAging && _aging.IsOpen)
        {
            _aging.Hide();
        }

        _aging.Sync(state);

        if (_warehouse.IsOpen && _services.Warehouse is { } view && !ReferenceEquals(view, _warehouse.View))
        {
            _warehouse.Refresh(view);
            RequestIcons(_state);
        }

        if (!_services.Pending)
        {
            _services.RefreshQuotes(state);
        }
    }

    // The icon cache keeps only the last requested set, so every request names everything on screen: the pack, the open
    // shop's stock and the open warehouse.
    private void RequestIcons(OwnerState state)
    {
        RememberKeys(state?.Items);
        RememberKeys(_warehouse.View?.Items);
        var keys = state?.Items.Select(static item => item.CatalogKey) ?? [];
        if (_shop.IsOpen)
        {
            keys = keys.Concat(_shop.IconKeys);
        }

        if (_warehouse.IsOpen && _warehouse.View is { } view)
        {
            keys = keys.Concat(view.Items.Select(static item => item.CatalogKey));
        }

        _icons.Request(keys);
    }

    // Keys of the items seen in the pack or the warehouse, so a sold or stored item still has its sound once it is gone.
    private void RememberKeys(IEnumerable<OwnedItemView> items)
    {
        if (_itemKeys.Count > 4096)
        {
            _itemKeys.Clear();
        }

        foreach (var item in items ?? [])
        {
            _itemKeys[item.InstanceId] = item.CatalogKey;
        }
    }

    private void ServiceSound(NpcServiceCommand command)
    {
        var key = command.Action == NpcServiceAction.Buy
            ? ItemCodes.TryFormat(command.ItemCode, out var bought) ? bought : null
            : command.Action is NpcServiceAction.Sell or NpcServiceAction.Deposit or NpcServiceAction.Withdraw or NpcServiceAction.Move
                ? _itemKeys.GetValueOrDefault(command.ItemId)
                : null;
        if (key is not null)
        {
            ItemSound?.Invoke(key);
        }
    }

    private void OpenService()
    {
        switch (_services.Open)
        {
            case NpcServiceKind.Shop:
                _npcOpen = false;
                _shop.Show(_services.Catalog);
                RequestIcons(_state);
                OpenOnly(_inventorySlide);
                break;
            case NpcServiceKind.Warehouse when _services.Warehouse is { } view:
                _npcOpen = false;
                _warehouse.Show(view);
                RequestIcons(_state);
                OpenOnly(_inventorySlide);
                break;
            case NpcServiceKind.Teleporter:
                _npc.ShowDestinations(_services.Destinations);
                break;
            case NpcServiceKind.ItemAging:
                _npcOpen = false;
                _aging.Show(_npc.Current?.Services.FirstOrDefault(static offer => offer.Kind == NpcServiceKind.ItemAging)?.Value ?? 100);
                RequestIcons(_state);
                OpenOnly(_inventorySlide);
                break;
            case NpcServiceKind.QuestEvent:
                if (_puzzle.Offer(_state, _services.NpcId, _services.NpcName) is { } help)
                {
                    _notice.Show(help);
                }
                else
                {
                    _npcOpen = false;
                    _npc.Dismiss();
                    _inventorySlide.Open = false;
                }

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
        _aging.Hide();
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
        if (_aging.Holds(item.InstanceId))
        {
            return true;
        }

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

        if (_aging.FullyOpen && _aging.Place(_state.Items.FirstOrDefault(item => item.InstanceId == itemId), pointer, _state) is { } refusal)
        {
            if (refusal.Length > 0)
            {
                _notice.Show(refusal);
            }

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

    private void ApplyAgingAction(OwnerState state, HudWorkActionKind action)
    {
        switch (action)
        {
            case HudWorkActionKind.Exit:
                CloseServices();
                break;
            case HudWorkActionKind.Start when state is not null:
                if (state.Money < _aging.Price(state))
                {
                    _notice.Show("Not enough gold");
                    break;
                }

                _services.Request(_services.NpcId, _services.NpcName, NpcServiceKind.ItemAging, NpcServiceAction.Age, state.InventoryRevision,
                    itemId: _aging.Item, materials: _aging.Materials);
                break;
        }
    }

    private void ShowAgingResult()
    {
        if (_services.LastCommand is not { Action: NpcServiceAction.Age })
        {
            return;
        }

        if (_services.LastWork is not { } work)
        {
            if (!_services.LastOk)
            {
                CraftResult?.Invoke(false);
            }

            return;
        }

        var result = (Simulation.Items.Aging.AgingResult)work.Result;
        _notice.Show(result switch
        {
            Simulation.Items.Aging.AgingResult.Destroyed => "Item aging has failed",
            Simulation.Items.Aging.AgingResult.DownOne => "Aging level has been decreased by -1",
            Simulation.Items.Aging.AgingResult.DownTwo => "Aging level has been decreased by -2",
            Simulation.Items.Aging.AgingResult.Double => "Aging level has been increased by +2",
            _ => "Aging level has been increased by +1"
        });
        CraftResult?.Invoke(result >= Simulation.Items.Aging.AgingResult.Success);
        CloseServices();
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

    private Task<PristonTale.Simulation.Items.Usable.ItemUseOutcome> UseItemAsync(PristonTale.Simulation.Items.Usable.ItemUseCommand command) =>
        UseItem is { } use ? use(command)
            : Task.FromResult(PristonTale.Simulation.Items.Usable.ItemUseOutcome.Refused(command.ExpectedRevision,
                PristonTale.Simulation.Items.Usable.ItemUseStatus.NotUsable));

    private bool Pressed(KeyboardState state, Keys key) => state.IsKeyDown(key) && !_previousKeyboard.IsKeyDown(key);

    public void Dispose()
    {
        _portrait.Dispose();
        _skillHud.Dispose();
        _icons.Dispose();
        _painter.Dispose();
        _sprites.Dispose();
    }
}
