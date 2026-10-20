using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Engine3D.Formats;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.World;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.UI.Widgets;

namespace PristonTale.Gameplay.Screens.Gameplay;

public sealed class GameplayScreen : GameScreen
{
    private readonly EnteredCharacter _entered;
    private GameScene _scene;
    private GameplayHud _hud;
    private KeyboardState _lastKeyboard;
    private bool _showRenderStats = true;
    private volatile PristonTale.Client.Net.WorldCatalog.WorldRenderCatalog _pendingHudCatalog;

    public GameplayScreen(ScreenContext context, EnteredCharacter entered) : base(context)
    {
        _entered = entered;
    }

    public override void Enter()
    {
        _scene = new GameScene(Context.GraphicsDevice, Context.Content, Context.Log, Context.Sky, Context.Effects, Context.Presentation, Context.Audio);
        _scene.LoadEntered(Context.AssetRoot, _entered);
        _hud = new GameplayHud(
            Context,
            _scene.ApplyInventoryAsync,
            _scene.ApplyProgressionAsync,
            _scene.InteractNpcAsync,
            _scene.PlayNpcInteractionAudio,
            _scene.StopNpcTalk);
        _hud.CharacterName = _scene.Player.Name;
        _hud.CharacterClass = _scene.Player.Class.Class;
        _scene.PresenceObserved += _hud.Combat.Observe;
        _scene.NpcSelected += _hud.OpenNpc;

        _scene.BeginRemotePresence(Context.Backend.OpenPlayChannel(), _entered);

        _ = FetchWorldCatalogAsync();
    }

    private async Task FetchWorldCatalogAsync()
    {
        try
        {
            var catalog = await Context.Backend.GetWorldCatalogAsync();
            _scene?.SetWorldRenderCatalog(catalog);
            _pendingHudCatalog = catalog;
        }
        catch (Exception ex)
        {
            Context.Log.Error("failed to fetch world render catalog (monsters will not render)", ex);
        }
    }

    public override string StatusText
    {
        get
        {
            if (_scene == null)
                return "Loading…";
            var shadows = _scene.ShadowsEnabled ? "on" : "OFF";
            if (!_showRenderStats)
                return $"Shadows {shadows} | {_scene.Status}";

            var stats = _scene.LastRenderStats;
            var hour = _scene.TimeOfDay;
            var clock = $"{(int)hour:00}:{(int)((hour - (int)hour) * 60):00}";
            return $"Shadows {shadows} | {clock} | Pos {_scene.PlayerPosition.X:F0} {-_scene.PlayerPosition.Z:F0} | Lights {_scene.StageLightCount} | " +
                   $"Draw {stats.DrawCalls} | " +
                   $"Shadow S{(stats.SunShadowUpdated ? 1 : 0)}/L{stats.LampShadowMapsUpdated}/D{stats.ShadowDrawCalls} | " +
                   $"Vis {stats.VisibleBatches}/{stats.TotalBatches} | Tri {stats.Triangles} | State {stats.StateChanges} | " +
                   $"View {_scene.RenderDebugView}";
        }
    }

    public override void Update(GameTime gameTime)
    {
        var catalog = _pendingHudCatalog;
        if (catalog is not null)
        {
            _pendingHudCatalog = null;
            _hud.Combat.SetCatalog(catalog);
        }

        var field = _scene.ActiveFieldDefinition;
        _hud.World = new HudWorldView(field?.Id, field?.Underground == true, _scene.ActiveMapRectangle, _scene.PlayerPosition,
            _scene.PlayerForward, _scene.TimeOfDay, _scene.RunMode);
        _hud.Update(_scene.Owner);
        _scene.PointerCaptured = _hud.PointerCaptured;
        _scene.CameraInputEnabled = Context.IsWindowActive && !Context.Presentation.AwaitingConfirmation;
        _scene.RightAttackEnabled = _hud.RightAttackEnabled;
        if (!Context.Presentation.AwaitingConfirmation)
        {
            UpdateMotionKeys();
            UpdateDebugKeys();
        }
        _scene.Update(gameTime);
        _hud.TargetId = _scene.SelectedTargetId;
        _lastKeyboard = Keyboard.GetState();
    }

    public override void Draw()
    {
        Context.GraphicsDevice.Clear(_scene.SkyColor);
        _scene.Draw();
        Context.SpriteBatch.Begin();
        _scene.DrawGroundLabels(Context.SpriteBatch, Context.Font);
        Context.SpriteBatch.End();
        _hud.Draw();
    }

    private void UpdateMotionKeys()
    {
        var keyboard = Keyboard.GetState();
        if (!keyboard.IsKeyDown(Keys.LeftControl) && !keyboard.IsKeyDown(Keys.RightControl))
        {
            return;
        }

        if (Pressed(keyboard, Keys.D1))
        {
            _scene.DebugForceMotion(PristonActorMotion.Stand);
        }
        if (Pressed(keyboard, Keys.D2))
        {
            _scene.DebugForceMotion(PristonActorMotion.Walk);
        }
        if (Pressed(keyboard, Keys.D3))
        {
            _scene.DebugForceMotion(PristonActorMotion.Run);
        }
        if (Pressed(keyboard, Keys.D4))
        {
            _scene.DebugForceMotion(PristonActorMotion.Attack);
        }
        if (Pressed(keyboard, Keys.D5))
        {
            _scene.DebugForceMotion(PristonActorMotion.Damage);
        }
        if (Pressed(keyboard, Keys.D6))
        {
            _scene.DebugForceMotion(PristonActorMotion.Dead);
        }
        if (Pressed(keyboard, Keys.D7))
        {
            _scene.DebugForceMotion(PristonActorMotion.Skill);
        }
    }

    private void UpdateDebugKeys()
    {
        var keyboard = Keyboard.GetState();
        var control = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
        if (control && Pressed(keyboard, Keys.F3))
        {
            _showRenderStats = !_showRenderStats;
        }
        if (control && Pressed(keyboard, Keys.F5))
        {
            _scene.ToggleShadows();
        }
        if (control && Pressed(keyboard, Keys.F10))
        {
            _scene.CycleRenderDebugView();
        }
        if (Pressed(keyboard, Keys.OemComma))
        {
            _scene.AdvanceTimeOfDay(-1f);
        }
        if (Pressed(keyboard, Keys.OemPeriod))
        {
            _scene.AdvanceTimeOfDay(1f);
        }
        if (Pressed(keyboard, Keys.P))
        {
            _scene.DayLengthSeconds = _scene.DayLengthSeconds > 0f ? 0f : 150f;
        }
    }

    private bool Pressed(KeyboardState keyboard, Keys key) =>
        keyboard.IsKeyDown(key) && !_lastKeyboard.IsKeyDown(key);

    public override bool HandleEscape() => _hud?.ClosePanel() == true;

    public override void Dispose()
    {
        if (_scene is not null && _hud is not null)
        {
            _scene.PresenceObserved -= _hud.Combat.Observe;
            _scene.NpcSelected -= _hud.OpenNpc;
        }

        _hud?.Dispose();
        _scene?.Dispose();
    }
}
