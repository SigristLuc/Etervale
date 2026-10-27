using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Contracts.Motion;

namespace PristonTale.Application.World;

internal sealed partial class FieldSkills
{
    // A monster a game master calls as a pet: it takes the player's side like an aggressive summon (one per owner, as every
    // summon) and leaves when its lifetime ends. It shows no keep icon on its owner.
    public MonsterInstance CallGameMasterPet(WorldPlayer owner, MonsterDefinition definition, float lifetimeMs)
    {
        DismissSummon(owner.EntityId);
        var pet = definition with { Faction = MonsterFaction.Npc, Brood = MonsterBrood.User };
        var monster = _host.AddSummon(pet, SpawnPoint(owner), owner.Facing);
        monster.OwnerId = owner.EntityId;
        monster.SummonSkill = 0;
        monster.ExpiresAtMs = _host.NowMs + (long)lifetimeMs;
        monster.Aggressive = true;
        monster.TargetId = 0;
        monster.AiState = MonsterAiState.Idle;
        monster.Motion = MotionState.Stand;
        _summons[owner.EntityId] = monster.EntityId;
        _summonSkills[owner.EntityId] = 0;
        return monster;
    }
}
