using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World.Ai;

namespace PristonTale.Simulation.Items;

// How item copies are made from their authored ranges. A shop sells the lowest form of an item; a monster drop rolls
// every range, and may specialize the copy for a class.
public static class ItemRoller
{
    // Percent ranges a specialization moves the requirements by: strength, spirit, talent, agility. A zero lower
    // bound leaves the requirement unchanged.
    private static readonly Dictionary<JobBitCode, (ItemRange Strength, ItemRange Spirit, ItemRange Talent, ItemRange Agility)> RequirementShift = new()
    {
        [JobBitCode.Mechanician] = (new(5, 15), new(-20, -10), default, new(-25, -15)),
        [JobBitCode.Fighter] = (new(10, 15), new(-20, -15), default, new(-20, -15)),
        [JobBitCode.Pikeman] = (new(10, 15), new(-20, -15), default, new(-25, -15)),
        [JobBitCode.Archer] = (new(-25, -15), new(-20, -10), default, new(15, 25)),
        [JobBitCode.Knight] = (new(5, 15), new(-15, -10), new(5, 10), new(-25, -15)),
        [JobBitCode.Atalanta] = (new(-20, -15), new(-20, -10), default, new(15, 25)),
        [JobBitCode.Priestess] = (new(-25, -20), new(15, 20), new(-15, -10), new(-20, -15)),
        [JobBitCode.Magician] = (new(-25, -20), new(15, 25), new(-15, -10), new(-20, -15))
    };

    public static int LowerDurability(ItemDefinition definition)
    {
        var template = definition.Template;
        if (template is null)
        {
            return definition.MaxDurability;
        }

        return template.Durability.Min;
    }

    public static ItemRoll Lower(ItemDefinition definition)
    {
        var template = definition.Template;
        if (template is null)
        {
            var stats = definition.BaseStats;
            return new ItemRoll(stats.DamageMin, stats.DamageMax, stats.AttackRating, stats.Defence, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                definition.Resistances, definition.RequiredLevel, definition.Requirements, definition.Price, definition.SpecializationJobs,
                ItemSpecBonus.None);
        }

        var resistances = template.Resistances;
        var roll = new ItemRoll(
            template.DamageMin.Max != 0 || template.DamageMax.Max != 0 ? template.DamageMin.Min : 0,
            template.DamageMin.Max != 0 || template.DamageMax.Max != 0 ? template.DamageMax.Min : 0,
            template.AttackRating.Min, template.Defence.Min, template.Absorb.Min, template.Block.Min, template.MoveSpeed.Min,
            template.LifeRegen.Min, template.ManaRegen.Min, template.StaminaRegen.Min,
            template.AddLife.Min, template.AddMana.Min, template.AddStamina.Min,
            new ResistanceBlock(Low(resistances, 0), Low(resistances, 1), Low(resistances, 2), Low(resistances, 3), Low(resistances, 4),
                Low(resistances, 5), Low(resistances, 6), Low(resistances, 7)),
            definition.RequiredLevel, definition.Requirements, definition.Price, template.SpecJob, ItemSpecBonus.None);
        if (template.SpecJob == 0)
        {
            return roll;
        }

        // The lowest form keeps its authored specialization with the middle of each requirement shift.
        var spec = template.Spec;
        var requirements = Shift(definition.Requirements, template.SpecJob, static range => range.Min + (range.Max - range.Min) / 2);
        var levelRating = spec.LevelAttackRating.Max != 0
            ? spec.LevelAttackRating.Min + (spec.LevelAttackRating.Max - spec.LevelAttackRating.Min) / 2
            : spec.LevelAttackRating.Min;
        var bonus = new ItemSpecBonus(spec.Absorb.Min, spec.Defence.Min, spec.MoveSpeed.Min, spec.Block, spec.AttackSpeed, spec.Critical,
            spec.ShootingRange, spec.MagicMastery.Min, spec.LevelLife, spec.LevelMana, levelRating, spec.LevelDamage,
            spec.LifeRegenRange.Max != 0 ? spec.LifeRegenRange.Min : spec.LifeRegen, spec.ManaRegen.Min, spec.StaminaRegen);
        return roll with { Requirements = requirements, Spec = bonus };
    }

    // A dropped copy: every range is rolled; four times in ten the copy is specialized, taking one of the random
    // classes three times in ten when the item has any, and then costs a fifth more.
    public static ItemInstance Create(ItemDefinition definition, int count, ISimRng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var item = new ItemInstance(definition, count);
        var template = definition.Template;
        if (template is null)
        {
            return item;
        }

        if (template.Durability.Min != 0)
        {
            var maximum = template.Durability.Max != 0 ? Range(rng, template.Durability) : template.Durability.Min;
            item.DurabilityMax = maximum;
            item.DurabilityCurrent = Range(rng, new ItemRange(maximum / 2, maximum));
        }

        var resistances = new short[8];
        for (var element = 0; element < resistances.Length; element++)
        {
            var range = element < template.Resistances.Count ? template.Resistances[element] : default;
            resistances[element] = (short)(range.Max != 0 ? Range(rng, range) : range.Min);
        }

        int damageMin = 0;
        int damageMax = 0;
        if (template.DamageMin.Max != 0 || template.DamageMax.Max != 0)
        {
            damageMin = Range(rng, template.DamageMin);
            damageMax = Range(rng, template.DamageMax);
        }

        var attackRating = Pick(rng, template.AttackRating);
        var absorb = Tenths(rng, template.Absorb);
        var defence = Pick(rng, template.Defence);
        var block = Hundredths(rng, template.Block);
        var speed = Tenths(rng, template.MoveSpeed);
        var manaRegen = Tenths(rng, template.ManaRegen);
        var lifeRegen = Tenths(rng, template.LifeRegen);
        var staminaRegen = Tenths(rng, template.StaminaRegen);
        var addLife = Pick(rng, template.AddLife);
        var addMana = Pick(rng, template.AddMana);
        var addStamina = Pick(rng, template.AddStamina);

        var specJob = 0u;
        var price = definition.Price;
        var requirements = definition.Requirements;
        var bonus = ItemSpecBonus.None;
        if (template.CanSpecialize && rng.NextExclusive(0, 10) <= 3)
        {
            var mask = template.SpecJob;
            var chosen = mask;
            if (rng.NextExclusive(0, 10) < 3 && template.RandomSpecJobs.Count > 0)
            {
                mask = 0;
            }

            if (mask == 0 && template.RandomSpecJobs.Count > 0)
            {
                chosen = template.RandomSpecJobs[rng.NextExclusive(0, template.RandomSpecJobs.Count)];
                mask = chosen;
            }

            if (mask != 0)
            {
                price += price * 2 / 10;
            }

            requirements = Shift(requirements, chosen, range => Range(rng, range));
            var spec = template.Spec;
            var specAbsorb = Hundredths(rng, spec.Absorb);
            var specDefence = Pick(rng, spec.Defence);
            var specSpeed = Hundredths(rng, spec.MoveSpeed);
            var mastery = Hundredths(rng, spec.MagicMastery);
            var specLife = spec.LifeRegenRange.Max != 0 ? Hundredths(rng, spec.LifeRegenRange) : spec.LifeRegen;
            var specMana = Hundredths(rng, spec.ManaRegen);
            var levelRating = Pick(rng, spec.LevelAttackRating);
            specJob = mask;
            if (mask != 0)
            {
                bonus = new ItemSpecBonus(specAbsorb, specDefence, specSpeed, spec.Block, spec.AttackSpeed, spec.Critical, spec.ShootingRange,
                    mastery, spec.LevelLife, spec.LevelMana, levelRating, spec.LevelDamage, specLife, specMana, spec.StaminaRegen);
            }
        }

        item.Roll = new ItemRoll(damageMin, damageMax, attackRating, defence, absorb, block, speed, lifeRegen, manaRegen, staminaRegen,
            addLife, addMana, addStamina,
            new ResistanceBlock(resistances[0], resistances[1], resistances[2], resistances[3], resistances[4], resistances[5], resistances[6],
                resistances[7]),
            definition.RequiredLevel, requirements, price, specJob, bonus);
        return item;
    }

    private static CharacterAttributes Shift(CharacterAttributes requirements, uint job, Func<ItemRange, int> percent)
    {
        if (!RequirementShift.TryGetValue((JobBitCode)job, out var shift))
        {
            return requirements;
        }

        return new CharacterAttributes(
            Apply(requirements.Strength, shift.Strength, percent),
            Apply(requirements.Spirit, shift.Spirit, percent),
            Apply(requirements.Talent, shift.Talent, percent),
            Apply(requirements.Agility, shift.Agility, percent),
            requirements.Health);
    }

    private static int Apply(int requirement, ItemRange shift, Func<ItemRange, int> percent)
    {
        if (requirement == 0 || shift.Min == 0)
        {
            return requirement;
        }

        return requirement + requirement * percent(shift) / 100;
    }

    private static short Low(IReadOnlyList<ItemRange> ranges, int element) => (short)(element < ranges.Count ? ranges[element].Min : 0);

    private static int Pick(ISimRng rng, ItemRange range) => range.Max != 0 ? Range(rng, range) : range.Min;

    private static int Range(ISimRng rng, ItemRange range) => range.Max <= range.Min ? range.Max : rng.NextInclusive(range.Min, range.Max);

    // One decimal: absorb, move speed and regeneration.
    private static float Tenths(ISimRng rng, ItemFloatRange range) => Steps(rng, range, 10f);

    // Two decimals: block and the specialization ranges.
    private static float Hundredths(ISimRng rng, ItemFloatRange range) => Steps(rng, range, 100f);

    private static float Steps(ISimRng rng, ItemFloatRange range, float steps)
    {
        if (range.Max == 0)
        {
            return range.Min;
        }

        var span = (int)((range.Max - range.Min) * steps);
        if (span <= 0)
        {
            return range.Max;
        }

        return range.Min + rng.NextInclusive(0, span) / steps;
    }
}
