using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using PristonTale.Simulation.World;

namespace PristonTale.Gameplay.Audio;

public sealed class FieldAudioController : IDisposable
{
    internal const float MusicVolume = 0.6f;
    internal const float FieldAmbientVolume = 0.65f;
    internal const float PositionalAmbientVolume = 1f;
    internal const float MusicFadeSeconds = 3.6f;
    private const float PositionalBlendRate = 8f;

    private readonly PristonAudioLibrary _library;
    private IReadOnlyList<FieldAmbient> _ambients = [];
    private FieldDef _field;
    private SoundEffectInstance _music;
    private SoundEffectInstance _fieldAmbient;
    private SoundEffectInstance _positionalAmbient;
    private SoundEffect _selectedMusic;
    private string _selectedMusicCode = string.Empty;
    private string _playingMusicCode = string.Empty;
    private int _fieldAmbientCode = -1;
    private int _positionalAmbientCode = -1;
    private float _musicFadeRemaining;
    private float _positionalTargetVolume;

    public FieldAudioController(PristonAudioLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        _library = library;
    }

    public void SelectField(FieldDef field)
    {
        ArgumentNullException.ThrowIfNull(field);
        _field = field;
        _ambients = field.Ambients;
        SelectMusic(field.BackMusicCode);
    }

    public void Update(Vector3 listenerPosition, float timeOfDay, float seconds)
    {
        if (!float.IsFinite(timeOfDay))
        {
            throw new ArgumentOutOfRangeException(nameof(timeOfDay));
        }

        if (!float.IsFinite(seconds) || seconds < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }

        UpdateMusic(seconds);
        UpdateFieldAmbience(timeOfDay);
        UpdatePositionalAmbience(listenerPosition, seconds);
    }

    public void PlayPortalWarp()
    {
        if (_library.SoundEnabled)
        {
            _library.PortalWarpSound.Play(1f, 0f, 0f);
        }
    }

    internal static int SelectFieldAmbientCode(FieldDef field, float timeOfDay)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.State switch
        {
            FieldState.Ruin => 3,
            FieldState.Desert => 4,
            FieldState.Dungeon or FieldState.QuestArena => SelectDungeonAmbientCode(field.FieldCode),
            FieldState.Forest => timeOfDay < 4f || timeOfDay >= 23f ? 2 : 1,
            FieldState.Village => 0,
            FieldState.Room => -1,
            FieldState.Iron => 9,
            FieldState.Ice => 12,
            _ => -1
        };
    }

    internal static float ProximityVolume(Vector3 listenerPosition, FieldAmbient ambient)
    {
        var source = new Vector3(ambient.Position.X, ambient.Position.Y, -ambient.Position.Z);
        var distanceSquared = Vector3.DistanceSquared(listenerPosition, source);
        var innerRadiusSquared = (float)ambient.Radius * ambient.Radius;
        var attenuation = (int)((distanceSquared - innerRadiusSquared) / 1100f);
        attenuation = Math.Clamp(attenuation, 0, 400);

        var legacyVolume = 400 - attenuation;
        if (legacyVolume <= 100)
        {
            return 0f;
        }

        var playbackVolume = Math.Min(legacyVolume + 100, 399);
        return playbackVolume / 400f;
    }

    private static int SelectDungeonAmbientCode(int fieldCode)
    {
        if (fieldCode <= 16 || fieldCode == 32)
        {
            return 5;
        }

        if (fieldCode <= 23)
        {
            return 6;
        }

        if (fieldCode <= 25)
        {
            return 7;
        }

        return fieldCode == 26 ? 8 : -1;
    }

    private void SelectMusic(string code)
    {
        code ??= string.Empty;
        if (_selectedMusicCode == code)
        {
            return;
        }

        _selectedMusicCode = code;
        _library.TryGetFieldMusic(code, out _selectedMusic);
        if (_music is null)
        {
            if (_library.MusicEnabled)
            {
                StartMusic();
            }

            return;
        }

        if (_playingMusicCode != code)
        {
            _musicFadeRemaining = MusicFadeSeconds;
        }
    }

    private void UpdateMusic(float seconds)
    {
        if (!_library.MusicEnabled)
        {
            StopMusic();
            return;
        }

        if (_music is null)
        {
            StartMusic();
            return;
        }

        if (_playingMusicCode == _selectedMusicCode)
        {
            _musicFadeRemaining = 0f;
            _music.Volume = MusicVolume;
            return;
        }

        if (_musicFadeRemaining <= 0f)
        {
            _musicFadeRemaining = MusicFadeSeconds;
        }

        _musicFadeRemaining = MathF.Max(0f, _musicFadeRemaining - seconds);
        _music.Volume = MusicVolume * (_musicFadeRemaining / MusicFadeSeconds);
        if (_musicFadeRemaining > 0f)
        {
            return;
        }

        StopMusic();
        StartMusic();
    }

    private void StartMusic()
    {
        if (_selectedMusic is null)
        {
            _playingMusicCode = string.Empty;
            return;
        }

        _music = _selectedMusic.CreateInstance();
        _music.IsLooped = true;
        _music.Volume = MusicVolume;
        _music.Play();
        _playingMusicCode = _selectedMusicCode;
    }

    private void StopMusic()
    {
        _music?.Stop();
        _music?.Dispose();
        _music = null;
        _playingMusicCode = string.Empty;
        _musicFadeRemaining = 0f;
    }

    private void UpdateFieldAmbience(float timeOfDay)
    {
        if (!_library.SoundEnabled || _field is null)
        {
            StopFieldAmbience();
            return;
        }

        var code = SelectFieldAmbientCode(_field, timeOfDay);
        if (code == _fieldAmbientCode)
        {
            return;
        }

        StopFieldAmbience();
        if (code < 0)
        {
            return;
        }

        _fieldAmbient = _library.FieldAmbient(code).CreateInstance();
        _fieldAmbient.IsLooped = true;
        _fieldAmbient.Volume = FieldAmbientVolume;
        _fieldAmbient.Play();
        _fieldAmbientCode = code;
    }

    private void StopFieldAmbience()
    {
        _fieldAmbient?.Stop();
        _fieldAmbient?.Dispose();
        _fieldAmbient = null;
        _fieldAmbientCode = -1;
    }

    private void UpdatePositionalAmbience(Vector3 listenerPosition, float seconds)
    {
        if (!_library.SoundEnabled)
        {
            StopPositionalAmbience();
            return;
        }

        var bestCode = -1;
        var bestVolume = 0f;
        foreach (var ambient in _ambients)
        {
            var volume = ProximityVolume(listenerPosition, ambient);
            if (volume > bestVolume)
            {
                bestCode = ambient.SoundCode;
                bestVolume = volume;
            }
        }

        if (bestCode >= 0 && bestCode != _positionalAmbientCode)
        {
            StopPositionalAmbience();
            _positionalAmbient = _library.Ambient(bestCode).CreateInstance();
            _positionalAmbient.IsLooped = true;
            _positionalAmbient.Volume = 0f;
            _positionalAmbient.Play();
            _positionalAmbientCode = bestCode;
        }

        _positionalTargetVolume = bestVolume * PositionalAmbientVolume;
        if (_positionalAmbient is null)
        {
            return;
        }

        var blend = 1f - MathF.Exp(-PositionalBlendRate * seconds);
        _positionalAmbient.Volume = MathHelper.Lerp(_positionalAmbient.Volume, _positionalTargetVolume, blend);
        if (bestCode < 0 && _positionalAmbient.Volume < 0.001f)
        {
            StopPositionalAmbience();
        }
    }

    private void StopPositionalAmbience()
    {
        _positionalAmbient?.Stop();
        _positionalAmbient?.Dispose();
        _positionalAmbient = null;
        _positionalAmbientCode = -1;
        _positionalTargetVolume = 0f;
    }

    public void Dispose()
    {
        StopMusic();
        StopFieldAmbience();
        StopPositionalAmbience();
    }
}
