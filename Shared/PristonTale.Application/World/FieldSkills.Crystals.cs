using System.Numerics;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Application.World;

// Crystal pets: monsters a crystal calls to fight for its user. Unlike a skill summon a player may keep several, they
// do not follow the user: each stands where it was called and runs at the nearest wild monster in its sight, never at
// another pet. Their kills credit the user like his own; they die when they expire or the user leaves the field.
internal sealed partial class FieldSkills
{
    private readonly Dictionary<uint, CrystalPet> _crystalPets = new();

    private sealed record CrystalPet(uint OwnerId, int ClanId);

    public IEnumerable<(uint PetId, uint OwnerId, int ClanId)> CrystalPets =>
        _crystalPets.Select(static pair => (pair.Key, pair.Value.OwnerId, pair.Value.ClanId));

    // Calls the pet beside its owner with the owner-level lifetime and life bonus; returns where it stands.
    public Vector3 CallCrystalPet(WorldPlayer owner, MonsterDefinition definition, int clanId = 0)
    {
        var level = owner.Character.Level;
        var combat = definition.Combat;
        var owned = definition with
        {
            Faction = MonsterFaction.Npc,
            Brood = MonsterBrood.User,
            Combat = combat with { MaxLife = combat.MaxLife + MonsterCrystals.PetBonusLife(level) }
        };
        var monster = _host.AddSummon(owned, SpawnPoint(owner), owner.Facing);
        monster.OwnerId = owner.EntityId;
        monster.ExpiresAtMs = _host.NowMs + (long)MonsterCrystals.PetLifetimeMs(level);
        monster.Aggressive = true;
        monster.AiState = MonsterAiState.Idle;
        monster.Motion = MonsterMotionState.Stand;
        _crystalPets[monster.EntityId] = new CrystalPet(owner.EntityId, clanId);
        return monster.Position;
    }

    private MonsterAttackEvent? StepCrystalPet(MonsterInstance pet, MonsterAiTuning tuning, float dtMs)
    {
        if (!pet.IsAlive)
        {
            _crystalPets.Remove(pet.EntityId);
            return null;
        }

        if (!_host.TryGetPlayer(pet.OwnerId, out var owner) || _host.NowMs >= pet.ExpiresAtMs)
        {
            _crystalPets.Remove(pet.EntityId);
            pet.ApplyDamage(pet.Life);
            return null;
        }

        pet.SinceRetargetMs += dtMs;
        var target = SummonTarget(pet, owner, tuning);
        var orders = target is null
            ? new SummonOrders(pet.Position, default, false)
            : new SummonOrders(target.Position, new PreyCandidate(target.EntityId, target.Position, target.IsAlive), true);
        return _summonBrain.Tick(pet, tuning, orders, dtMs);
    }

    // A web's blow slows the struck monster for a moment; Kelvezu is immune.
    private void SlowFromWeb(MonsterInstance summon, MonsterInstance target)
    {
        if (summon.Def.SoundCode != MonsterCrystals.WebSoundCode || target.Def.SoundCode == MonsterCrystals.KelvezuSoundCode || !target.IsAlive)
        {
            return;
        }

        if (target.Effects.Apply(MonsterCrystals.WebSlow(), summon.EntityId, MonsterCrystals.WebSlowMs, target.EntityId) is { } change)
        {
            _statusEvents.Add(StatusView(change, 1));
        }
    }
}
