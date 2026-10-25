using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.IO;
using PristonTale.Gameplay.Effects;
using PristonTale.Simulation.Items;

namespace PristonTale.Gameplay.Audio;

// What a character sound folder's file plays for, by the start of its name.
public enum CharacterSoundCue
{
    Attack,
    Damage,
    Dead,
    Neutral,
    Move,
    Warp,
    Skill,
    Hammer
}

// How a player's swing ended, for its weapon sound.
public enum WeaponSoundOutcome
{
    Miss,
    Hit,
    Critical
}

// Character, weapon and footstep sounds as the original plays them: a random file of the character's sound folder for a
// motion (monsters by sound code, players by class), a weapon file by weapon family and hand for each swing, and the
// floor's footsteps. Folders are read when a character first needs them and decoded on a worker; a sound that is not
// ready yet is skipped, as the original loads monster sounds when they first play.
public sealed class CombatSounds : IDisposable
{
    private const string WeaponDirectory = "wav/Effects/weapon";
    private const string RestartSound = "wav/Effects/event/fieldstart.wav";

    // Weapon file name starts and their sound codes; the first start that matches a file names it.
    private static readonly (string Start, int Code)[] WeaponFiles =
    [
        ("one hand swing axe", 1), ("two hand swing axe", 2), ("one hand swing sword", 3), ("two hand swing sword", 4),
        ("one hand swing spear", 5), ("two hand swing spear", 6), ("one hand scratch", 7), ("bow shoot", 8),
        ("one hand swing blunt", 9), ("two hand swing blunt", 10), ("throwing", 11), ("one hand miss", 12), ("two hand miss", 13),
        ("punch hit", 14), ("one hand small swing", 15), ("critical hit", 16), ("one hand casting", 17), ("two hand casting", 18)
    ];

    private static readonly (string Start, CharacterSoundCue Cue)[] CharacterFiles =
    [
        ("attack", CharacterSoundCue.Attack), ("damage", CharacterSoundCue.Damage), ("dead", CharacterSoundCue.Dead),
        ("neutral", CharacterSoundCue.Neutral), ("move", CharacterSoundCue.Move), ("disappear", CharacterSoundCue.Warp),
        ("change", CharacterSoundCue.Warp), ("skill", CharacterSoundCue.Skill), ("hammer", CharacterSoundCue.Hammer)
    ];

    private static readonly Dictionary<int, string> Directories = CharacterSoundBanks.Directories.ToDictionary(bank => bank.Code, bank => bank.Directory);

    private readonly string _assetRoot;
    private readonly SkillSoundBank _bank;
    private readonly Dictionary<int, Dictionary<CharacterSoundCue, string[]>> _characters = new();
    private readonly Dictionary<int, string[]> _weapons = new();

    public CombatSounds(string assetRoot, IClientLog log)
    {
        _assetRoot = assetRoot;
        _bank = new SkillSoundBank(assetRoot, log);
        var weapons = new Dictionary<int, List<string>>();
        foreach (var path in Files(WeaponDirectory))
        {
            var name = Path.GetFileName(path);
            foreach (var (start, code) in WeaponFiles)
            {
                if (name.StartsWith(start, StringComparison.OrdinalIgnoreCase))
                {
                    (weapons.TryGetValue(code, out var list) ? list : weapons[code] = []).Add(path);
                    break;
                }
            }
        }

        foreach (var (code, paths) in weapons)
        {
            _weapons[code] = [.. paths];
        }

        _bank.Preload([.. _weapons.Values.SelectMany(paths => paths), .. FootstepFiles.SelectMany(files => files), RestartSound]);
    }

    public bool Enabled
    {
        get => _bank.Enabled;
        set => _bank.Enabled = value;
    }

    public void Update() => _bank.Update();

    // Starts reading a character's sound folder before it first plays (a monster entering view).
    public void Prepare(int soundCode) => Bank(soundCode);

    // Walking sounds are heard only close by (three quarters of full volume and more), as in the original. A monster
    // drawn smaller than its model plays higher, one drawn more than twice as large plays lower.
    public void PlayCharacter(int soundCode, CharacterSoundCue cue, Vector3 source, Vector3 listener, float scale = 1f)
    {
        if (soundCode == 0 || Bank(soundCode) is not { } bank || !bank.TryGetValue(cue, out var files))
        {
            return;
        }

        if (cue == CharacterSoundCue.Move && Volume(source, listener) < 0.75f)
        {
            return;
        }

        var pitch = scale <= 0f || MathF.Abs(scale - 1f) < 0.001f ? 0f : scale < 1f ? MathF.Log2(3600f / 2205f) : scale > 2f ? MathF.Log2(1400f / 2205f) : 0f;
        _bank.Play(files[Random.Shared.Next(files.Length)], source, listener, 1f, pitch);
    }

    // A player's swing: the miss sound of its hand, or the swing sound of its weapon family (punches without a weapon),
    // and the critical sound on top of a critical hit. Magicians and priestesses cast with staves.
    public void PlayWeapon(string weaponKey, int classCode, WeaponSoundOutcome outcome, Vector3 source, Vector3 listener)
    {
        var twoHanded = weaponKey.Length > 0 && ItemEquipmentMetadata.IsTwoHanded(weaponKey);
        var code = outcome == WeaponSoundOutcome.Miss ? twoHanded ? 13 : 12 : WeaponCode(weaponKey, twoHanded, classCode);
        if (!PlayWeaponCode(code, source, listener))
        {
            return;
        }

        if (outcome == WeaponSoundOutcome.Critical && weaponKey.Length > 0)
        {
            PlayWeaponCode(16, source, listener);
        }
    }

    // The footsteps of the floor the local player walks on, at the original's fixed volume.
    public void PlayFootstep(FootstepFloor floor, int step)
    {
        var files = FootstepFiles[(int)floor];
        var file = floor == FootstepFloor.Water ? files[step % files.Length] : files[Random.Shared.Next(files.Length)];
        _bank.Play(file, Vector3.Zero, Vector3.Zero);
    }

    // The restart chime where a character rises again.
    public void PlayRestart(Vector3 source, Vector3 listener) => _bank.Play(RestartSound, source, listener);

    public void Dispose() => _bank.Dispose();

    internal static float Volume(Vector3 source, Vector3 listener) =>
        (400 - Math.Clamp((int)(Vector3.DistanceSquared(source, listener) / 1100f), 0, 400)) / 400f;

    // The original's weapon sound code: axes, swords (the first three are small blades), spears, claws, bows, staves (cast
    // by magicians and priestesses, struck by others), hammers and throwing weapons; anything else punches.
    internal static int WeaponCode(string weaponKey, bool twoHanded, int classCode)
    {
        if (!ItemCodes.TryParse(weaponKey, out var code))
        {
            return 14;
        }

        return weaponKey[..3].ToUpperInvariant() switch
        {
            "WA1" => twoHanded ? 2 : 1,
            "WS2" when ItemCodes.Ordinal(code) <= 3 => 15,
            "WS2" => twoHanded ? 4 : 3,
            "WP1" => twoHanded ? 6 : 5,
            "WC1" => 7,
            "WS1" => 8,
            "WM1" when classCode is 7 or 8 => twoHanded ? 18 : 17,
            "WM1" or "WH1" => twoHanded ? 10 : 9,
            "WT1" => 11,
            _ => 14
        };
    }

    private bool PlayWeaponCode(int code, Vector3 source, Vector3 listener)
    {
        if (!_weapons.TryGetValue(code, out var files))
        {
            return false;
        }

        _bank.Play(files[Random.Shared.Next(files.Length)], source, listener);
        return true;
    }

    private Dictionary<CharacterSoundCue, string[]> Bank(int soundCode)
    {
        if (_characters.TryGetValue(soundCode, out var bank))
        {
            return bank;
        }

        bank = [];
        _characters[soundCode] = bank;
        if (!Directories.TryGetValue(soundCode, out var directory))
        {
            return bank;
        }

        var grouped = new Dictionary<CharacterSoundCue, List<string>>();
        foreach (var path in Files(directory))
        {
            var name = Path.GetFileName(path);
            foreach (var (start, cue) in CharacterFiles)
            {
                if (name.StartsWith(start, StringComparison.OrdinalIgnoreCase))
                {
                    (grouped.TryGetValue(cue, out var list) ? list : grouped[cue] = []).Add(path);
                    break;
                }
            }
        }

        foreach (var (cue, paths) in grouped)
        {
            bank[cue] = [.. paths];
        }

        _bank.Preload(bank.Values.SelectMany(paths => paths));
        return bank;
    }

    // The .wav files of a folder as asset-relative paths.
    private IEnumerable<string> Files(string relativeDirectory)
    {
        var directory = PristonFileResolver.Resolve(Path.Combine(_assetRoot, relativeDirectory));
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "*.wav", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(path => Path.GetRelativePath(_assetRoot, path).Replace('\\', '/'))
            .ToArray();
    }

    private static readonly string[][] FootstepFiles =
    [
        ["wav/Effects/FootSteps/step01.wav", "wav/Effects/FootSteps/step02.wav"],
        ["wav/Effects/FootSteps/stonerun1.wav", "wav/Effects/FootSteps/stonerun2.wav", "wav/Effects/FootSteps/stonerun3.wav"],
        ["wav/Effects/FootSteps/stone-dun1.wav", "wav/Effects/FootSteps/stone-dun2.wav", "wav/Effects/FootSteps/stone-dun3.wav"],
        ["wav/Effects/FootSteps/inwater 1.wav", "wav/Effects/FootSteps/inwater 2.wav", "wav/Effects/FootSteps/inwater 3.wav"]
    ];
}

// The floor under the local player's step: soft ground (forest, desert), stone (towns, ruins), dungeon stone, water.
public enum FootstepFloor
{
    Soft,
    Stone,
    Dungeon,
    Water
}
