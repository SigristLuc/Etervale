using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.Screens.CharacterSelect;
using PristonTale.Gameplay.Screens.Framework;

namespace PristonTale.Gameplay.Screens.Opening;

public sealed class OpeningScreen : GameScreen
{
    private OpeningCinematic _cinematic;
    private OpeningLoginController _loginController;

    private SoundEffectInstance _bgmInstance;
    private KeyboardState _lastKeyboard;
    private string _status = "Opening";

    private bool _loginRevealed;

    public OpeningScreen(ScreenContext context) : base(context)
    {
    }

    public override string StatusText => _status;

    public override void Enter()
    {
        _cinematic = new OpeningCinematic(
            Context.GraphicsDevice,
            Context.Content,
            Context.Log,
            AssetPath,
            Context.AssetRoot,
            Context.Sky,
            Context.Presentation,
            Context.Effects);
        _cinematic.Load();
        _loginController = new OpeningLoginController(Context);

        StartBgm();
        _status = $"Opening: {_cinematic.StatusSuffix}";
    }

    public override void Update(GameTime gameTime)
    {
        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_bgmInstance is not null)
        {
            _bgmInstance.Volume = Context.Audio?.MusicEnabled == false ? 0f : 0.6f;
        }

        _cinematic.Update(gameTime);

        var keyboard = Keyboard.GetState();
        if (Context.Presentation.AwaitingConfirmation)
        {
            _lastKeyboard = keyboard;
            return;
        }

        if (!_loginRevealed)
        {
            if (AnyKeyPressed(keyboard, _lastKeyboard) || _cinematic.IsOver)
                _loginRevealed = true;
        }
        else if (_loginController.Update(keyboard, _lastKeyboard, dt) == OpeningLoginController.LoginResult.Succeeded)
        {
            Context.RequestScreen(new CharacterSelectScreen(Context));
        }

        _lastKeyboard = keyboard;
    }

    private static bool AnyKeyPressed(in KeyboardState current, in KeyboardState previous)
    {
        foreach (var key in current.GetPressedKeys())
            if (!previous.IsKeyDown(key))
                return true;
        return false;
    }

    public override void Draw()
    {
        _cinematic.Draw();

        var viewport = Context.GraphicsDevice.Viewport;
        if (!_loginRevealed)
        {
            Context.SpriteBatch.Begin();
            Context.SpriteBatch.End();
        }
        else
        {
            _loginController.Draw(viewport);
        }
    }

    private void StartBgm()
    {
        if (Context.Audio?.OpeningMusic is not { } music)
        {
            return;
        }

        _bgmInstance = music.CreateInstance();
        _bgmInstance.IsLooped = true;
        _bgmInstance.Volume = Context.Audio.MusicEnabled ? 0.6f : 0f;
        _bgmInstance.Play();
    }

    public override void Dispose()
    {
        _loginController?.Dispose();
        _bgmInstance?.Stop();
        _bgmInstance?.Dispose();
        _cinematic?.Dispose();
    }
}
