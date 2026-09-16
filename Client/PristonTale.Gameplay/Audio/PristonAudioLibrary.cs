using Microsoft.Xna.Framework.Audio;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Gameplay.Audio;

public sealed class PristonAudioLibrary : IDisposable
{
    private static readonly (int Code, string Directory)[] NpcSoundBankDirectories =
    [
        (LegacyNpcSoundCode.Wolverine, "wav/Effects/monster/Wolverin"),
        (LegacyNpcSoundCode.Fury, "wav/Effects/monster/Fury"),
        (LegacyNpcSoundCode.Morif, "wav/Effects/NPC/morif"),
        (LegacyNpcSoundCode.MollyWolf, "wav/Effects/NPC/mollywolf"),
        (LegacyNpcSoundCode.SkillMaster, "wav/Effects/NPC/SkillMaster"),
        (LegacyNpcSoundCode.MagicMaster, "wav/Effects/NPC/MagicMaster"),
        (LegacyNpcSoundCode.CastleDoor, "wav/Effects/NPC/castledoor"),
        (LegacyNpcSoundCode.CastleCrystalRed, "wav/Effects/NPC/crystal_r"),
        (LegacyNpcSoundCode.CastleCrystalGreen, "wav/Effects/NPC/crystal_g"),
        (LegacyNpcSoundCode.CastleCrystalBlue, "wav/Effects/NPC/crystal_b"),
        (LegacyNpcSoundCode.CastleTower, "wav/Effects/NPC/tower-b"),
        (LegacyNpcSoundCode.CastleSoldierA, "wav/Effects/NPC/Soldier_A"),
        (LegacyNpcSoundCode.CastleSoldierB, "wav/Effects/NPC/Soldier_B"),
        (LegacyNpcSoundCode.CastleSoldierC, "wav/Effects/NPC/Soldier_C")
    ];

    private static readonly (string Code, string Path)[] FieldMusicFiles =
    [
        ("BGM_CODE_TOWN1", "wav/BGM/Town - Tempskron_Stronghold.bgm"),
        ("BGM_CODE_TOWN2", "wav/BGM/Town 1 - Tempskron_Ricarten - When wind comes-o.bgm"),
        ("BGM_CODE_FOREST", "wav/BGM/Field - Forest - DarkWood.bgm"),
        ("BGM_CODE_DUNGEON", "wav/BGM/Dungeon - Gloomy Heart.bgm"),
        ("BGM_CODE_FILAI", "wav/BGM/Town 2 - Morion_Philliy - Voyage above the Clouds.bgm"),
        ("BGM_CODE_SOD1", "wav/Sod/BGM/Stage_Play1.bgm"),
        ("BGM_CODE_SOD2", "wav/Sod/BGM/Stage_Play1.bgm"),
        ("BGM_CODE_SOD3", "wav/Sod/BGM/Stage_Play3.bgm"),
        ("BGM_CODE_DESERT", "wav/BGM/Field - Desert - Pilgrim.bgm"),
        ("BGM_CODE_ICE", "wav/BGM/Ice 1.bgm")
    ];

    private static readonly string[] AmbientFiles =
    [
        "wav/Ambient/stone-mill.wav",
        "wav/Ambient/dungeon_pumping_b.wav",
        "wav/Ambient/dungeon_pumping_l.wav",
        "wav/Ambient/dungeon_spin_b.wav",
        "wav/Ambient/lake 1.wav",
        "wav/Ambient/mystic 1.wav",
        "wav/Ambient/mystic 2.wav",
        "wav/Ambient/ship_swaing_l.wav",
        "wav/Ambient/stream.wav",
        "wav/Ambient/town_bell 1.wav",
        "wav/Ambient/town_bell 2.wav",
        "wav/Ambient/town_spin_b.wav",
        "wav/Ambient/town_spin_l.wav",
        "wav/Ambient/waterfall.wav",
        "wav/Ambient/stone-mill2.wav",
        "wav/Ambient/watermill 1.wav",
        "wav/Ambient/windmill 1.wav",
        "wav/Ambient/windmill 2.wav",
        "wav/Ambient/windmill 3.wav",
        "wav/Ambient/windmill 4.wav",
        "wav/Ambient/owl 1.wav",
        "wav/Ambient/dungeon_big-bolt 1.wav",
        "wav/Ambient/dungeon_stonepole.wav",
        "wav/Ambient/zombiemill 1.wav",
        "wav/Ambient/dungeon_weight.wav",
        "wav/Ambient/dungeon_device_side.wav",
        "wav/Ambient/dungeon_device_axe.wav",
        "wav/Ambient/dungeon_cage 1.wav"
    ];

    private static readonly string[] FieldAmbientFiles =
    [
        "wav/Ambient/tempskron-town.wav",
        "wav/Ambient/forest-day.wav",
        "wav/Ambient/forest-night.wav",
        "wav/Ambient/ruin wind.wav",
        "wav/Ambient/desert wind.wav",
        "wav/Ambient/dungeon.wav",
        "wav/Ambient/temple_amb.wav",
        "wav/Ambient/cave_amb.wav",
        "wav/Ambient/darksanc_amb.wav",
        "wav/Ambient/iron_amb.wav",
        "wav/Ambient/iron_amb2.wav",
        "wav/Ambient/Babel Approach.wav",
        "wav/Ambient/icewind.wav"
    ];

    private readonly Dictionary<string, SoundEffect> _fieldMusic = new(StringComparer.Ordinal);
    private readonly SoundEffect[] _ambients = new SoundEffect[AmbientFiles.Length];
    private readonly SoundEffect[] _fieldAmbients = new SoundEffect[FieldAmbientFiles.Length];
    private readonly Dictionary<(int SoundCode, PristonActorMotion Motion), SoundEffect[]> _npcSounds = [];
    private readonly Dictionary<uint, SoundEffect> _talkSounds = [];
    private readonly List<SoundEffect> _owned = [];
    private int _npcSoundCount;

    private PristonAudioLibrary()
    {
    }

    public SoundEffect OpeningMusic { get; private set; }
    public SoundEffect CharacterSelectMusic { get; private set; }
    public SoundEffect PortalWarpSound { get; private set; }
    public bool MusicEnabled { get; private set; } = true;
    public bool SoundEnabled { get; private set; } = true;
    public int AmbientCount => _ambients.Length;
    public int FieldAmbientCount => _fieldAmbients.Length;
    public int NpcSoundCount => _npcSoundCount;
    public int TalkSoundCount => _talkSounds.Count;

    public static PristonAudioLibrary LoadBootstrap(string assetRoot, IClientLog log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetRoot);
        ArgumentNullException.ThrowIfNull(log);

        var library = new PristonAudioLibrary();
        try
        {
            var byPath = new Dictionary<string, SoundEffect>(StringComparer.OrdinalIgnoreCase);
            library.OpeningMusic = library.Load(assetRoot, "StartImage/Opening/Intro.wav", byPath);
            library.CharacterSelectMusic = library.Load(assetRoot, "StartImage/login/CharacterSelect.wav", byPath);
            library.PortalWarpSound = library.Load(assetRoot, "wav/Effects/Skill/Learn_Skill.wav", byPath);
            foreach (var (code, path) in FieldMusicFiles)
            {
                library._fieldMusic.Add(code, library.Load(assetRoot, path, byPath));
            }

            for (var index = 0; index < AmbientFiles.Length; index++)
            {
                library._ambients[index] = library.Load(assetRoot, AmbientFiles[index], byPath);
            }

            for (var index = 0; index < FieldAmbientFiles.Length; index++)
            {
                library._fieldAmbients[index] = library.Load(assetRoot, FieldAmbientFiles[index], byPath);
            }

            library.LoadNpcSoundBanks(assetRoot, byPath);
            library.LoadTalkSounds(assetRoot, byPath, log);

            log.Info(
                $"[Audio] prepared {library._fieldMusic.Count} field music codes, {library._fieldAmbients.Length} field ambience loops, "
                + $"{library._ambients.Length} positional ambience sounds, {library._npcSoundCount} NPC motion sounds and "
                + $"{library._talkSounds.Count} NPC talk sounds.");
            return library;
        }
        catch
        {
            library.Dispose();
            throw;
        }
    }

    public bool TryGetFieldMusic(string code, out SoundEffect music)
    {
        if (string.IsNullOrEmpty(code) || code == "BGM_CODE_CUSTOM")
        {
            music = null;
            return false;
        }

        return _fieldMusic.TryGetValue(code, out music);
    }

    public SoundEffect Ambient(int code)
    {
        if ((uint)code >= (uint)_ambients.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(code), code, $"Ambient sound code must be between 0 and {_ambients.Length - 1}.");
        }

        return _ambients[code];
    }

    public SoundEffect FieldAmbient(int code)
    {
        if ((uint)code >= (uint)_fieldAmbients.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(code), code, $"Field ambience code must be between 0 and {_fieldAmbients.Length - 1}.");
        }

        return _fieldAmbients[code];
    }

    public bool TryGetNpcSound(int soundCode, PristonActorMotion motion, out SoundEffect[] variants)
    {
        return _npcSounds.TryGetValue((soundCode, motion), out variants);
    }

    public bool TryGetTalkSound(string message, out SoundEffect sound)
    {
        if (string.IsNullOrEmpty(message))
        {
            sound = null;
            return false;
        }

        return _talkSounds.TryGetValue(LegacyTalkSound.GetSoundCode(message), out sound);
    }

    public void ApplySettings(ClientSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        MusicEnabled = settings.MusicEnabled;
        SoundEnabled = settings.SoundEnabled;
    }

    private SoundEffect Load(string assetRoot, string relativePath, Dictionary<string, SoundEffect> byPath)
    {
        if (byPath.TryGetValue(relativePath, out var existing))
        {
            return existing;
        }

        var path = Path.Combine(assetRoot, relativePath);
        using var stream = PristonWaveDecoder.OpenRead(path);
        var sound = SoundEffect.FromStream(stream);
        byPath.Add(relativePath, sound);
        _owned.Add(sound);
        return sound;
    }

    private void LoadNpcSoundBanks(string assetRoot, Dictionary<string, SoundEffect> byPath)
    {
        var grouped = new Dictionary<(int SoundCode, PristonActorMotion Motion), List<SoundEffect>>();
        foreach (var (soundCode, relativeDirectory) in NpcSoundBankDirectories)
        {
            var directory = PristonFileResolver.Resolve(Path.Combine(assetRoot, relativeDirectory));
            var paths = Directory.EnumerateFiles(directory, "*.wav", SearchOption.TopDirectoryOnly)
                .Order(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
            {
                if (!TryGetMotion(Path.GetFileName(path), out var motion))
                {
                    continue;
                }

                var key = (soundCode, motion);
                if (!grouped.TryGetValue(key, out var variants))
                {
                    variants = [];
                    grouped.Add(key, variants);
                }

                var relativePath = Path.GetRelativePath(assetRoot, path);
                variants.Add(Load(assetRoot, relativePath, byPath));
                _npcSoundCount++;
            }
        }

        foreach (var (key, variants) in grouped)
        {
            _npcSounds.Add(key, variants.ToArray());
        }
    }

    private void LoadTalkSounds(string assetRoot, Dictionary<string, SoundEffect> byPath, IClientLog log)
    {
        var relativeDirectory = Path.Combine("wav", "Talk");
        var directory = PristonFileResolver.Resolve(Path.Combine(assetRoot, relativeDirectory));
        if (!Directory.Exists(directory))
        {
            log.Info("[Audio] wav/Talk is absent; hashed NPC voice playback is disabled until the original voice archive is supplied.");
            return;
        }

        var paths = Directory.EnumerateFiles(directory, "*.wav", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (!uint.TryParse(Path.GetFileNameWithoutExtension(path), out var soundCode))
            {
                log.Warn($"[Audio] ignored NPC talk asset with a non-numeric name: {path}");
                continue;
            }

            var relativePath = Path.GetRelativePath(assetRoot, path);
            _talkSounds[soundCode] = Load(assetRoot, relativePath, byPath);
        }
    }

    private static bool TryGetMotion(string fileName, out PristonActorMotion motion)
    {
        if (fileName.StartsWith("attack", StringComparison.OrdinalIgnoreCase))
        {
            motion = PristonActorMotion.Attack;
            return true;
        }

        if (fileName.StartsWith("damage", StringComparison.OrdinalIgnoreCase))
        {
            motion = PristonActorMotion.Damage;
            return true;
        }

        if (fileName.StartsWith("dead", StringComparison.OrdinalIgnoreCase))
        {
            motion = PristonActorMotion.Dead;
            return true;
        }

        if (fileName.StartsWith("neutral", StringComparison.OrdinalIgnoreCase))
        {
            motion = PristonActorMotion.Stand;
            return true;
        }

        if (fileName.StartsWith("move", StringComparison.OrdinalIgnoreCase))
        {
            motion = PristonActorMotion.Walk;
            return true;
        }

        if (fileName.StartsWith("disappear", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("change", StringComparison.OrdinalIgnoreCase))
        {
            motion = PristonActorMotion.Warp;
            return true;
        }

        if (fileName.StartsWith("skill", StringComparison.OrdinalIgnoreCase))
        {
            motion = PristonActorMotion.Skill;
            return true;
        }

        motion = default;
        return false;
    }

    public void Dispose()
    {
        foreach (var sound in _owned)
        {
            sound.Dispose();
        }

        _owned.Clear();
        _fieldMusic.Clear();
        _npcSounds.Clear();
        _talkSounds.Clear();
        _npcSoundCount = 0;
        Array.Clear(_ambients);
        Array.Clear(_fieldAmbients);
    }
}
