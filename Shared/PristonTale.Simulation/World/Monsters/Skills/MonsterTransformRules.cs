namespace PristonTale.Simulation.World.Monsters.Skills;

// Monsters that turn into another monster when hurt or after many blows: they play their transformation clip, then the
// server replaces them with a full-life monster of the other form at the same place.
public static class MonsterTransformRules
{
    // More than 80 blows make a shape-shifter angry enough to change whatever its life.
    public const int AngerLimit = 80;

    public static bool IsShapeShifter(MonsterDefinition definition) =>
        definition.SoundCode is MonsterSounds.Hungky or MonsterSounds.DeadHopt or MonsterSounds.BebeChick;

    public static bool Wants(MonsterInstance monster)
    {
        var definition = monster.Def;
        if (!IsShapeShifter(definition) || monster.DuelPlayerId != 0)
        {
            return false;
        }

        var max = definition.Combat.MaxLife;
        var percent = max > 0 ? monster.Life * 100L / max : 100;
        var threshold = definition.SoundCode == MonsterSounds.DeadHopt ? 20 : 30;
        return percent < threshold || monster.AttackAnger > AngerLimit;
    }

    // The form it turns into: Hungky becomes a hulk below level 80, or one of level 80 and more when angry; the undead hopy
    // a king hopy of level 108 and more; the chick its parent.
    public static bool Accepts(MonsterInstance monster, MonsterDefinition candidate)
    {
        return monster.Def.SoundCode switch
        {
            MonsterSounds.Hungky => candidate.SoundCode == MonsterSounds.Hulk
                && (monster.AttackAnger > AngerLimit ? candidate.Combat.Level >= 80 : candidate.Combat.Level < 80),
            MonsterSounds.DeadHopt => candidate.SoundCode == MonsterSounds.DeadKingHopy && candidate.Combat.Level >= 108,
            MonsterSounds.BebeChick => candidate.SoundCode == MonsterSounds.PapaChick,
            _ => false
        };
    }
}
