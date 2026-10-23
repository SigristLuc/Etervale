using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Application.World;

// Premium timers on the field tick: they expire on the real clock, and the vampiric and mana-recharging timers answer
// each attack of their owner. The hits of one attack land in the same tick, so the tick's hits of a player form one
// attack: the number of monsters hit and their average damage feed the life and mana it returns.
internal sealed class FieldPremium
{
    // Bless Castle: the per-attack premium effects do not work there.
    public const int CastleFieldId = 33;

    private readonly Dictionary<uint, (int Hits, long Damage)> _attacks = new();
    private readonly int _fieldId;

    public FieldPremium(int fieldId)
    {
        _fieldId = fieldId;
    }

    // Unix seconds of the world clock, set by the world before each step.
    public long NowSeconds { get; set; }

    public void RecordHit(WorldPlayer player, int damage)
    {
        if (_fieldId == CastleFieldId || damage <= 0)
        {
            return;
        }

        var premium = player.Character.Premium;
        if (!premium.IsActive(PremiumEffectKind.VampiricCuspid) && !premium.IsActive(PremiumEffectKind.VampiricCuspidEx)
            && !premium.IsActive(PremiumEffectKind.ManaRecharging))
        {
            return;
        }

        var current = _attacks.GetValueOrDefault(player.EntityId);
        _attacks[player.EntityId] = (current.Hits + 1, current.Damage + damage);
    }

    public void Step(IEnumerable<WorldPlayer> players, Func<uint, WorldPlayer?> find)
    {
        if (NowSeconds > 0)
        {
            foreach (var player in players)
            {
                player.Character.Premium.Expire(NowSeconds);
            }
        }

        if (_attacks.Count == 0)
        {
            return;
        }

        foreach (var (entityId, attack) in _attacks)
        {
            if (find(entityId) is not { IsDead: false } player)
            {
                continue;
            }

            var character = player.Character;
            var power = (int)(attack.Damage / attack.Hits);
            player.Heal(PremiumRules.LifeFor(character.Premium, character.Attributes.Strength, power, attack.Hits));
            var mana = PremiumRules.ManaFor(character.Premium, character.Attributes.Spirit, power, attack.Hits);
            if (mana > 0)
            {
                character.Mana = Math.Min(character.MaxMana, character.Mana + mana);
            }
        }

        _attacks.Clear();
    }
}
