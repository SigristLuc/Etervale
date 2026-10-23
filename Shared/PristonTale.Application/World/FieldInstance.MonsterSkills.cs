using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Spawning;

namespace PristonTale.Application.World;

// The field side of monster skills: what FieldMonsterSkills reads and changes on this field.
public sealed partial class FieldInstance : IFieldMonsterSkillHost
{
    bool IFieldMonsterSkillHost.TryGetPlayer(uint entityId, [MaybeNullWhen(false)] out WorldPlayer player) => _players.TryGetValue(entityId, out player);

    bool IFieldMonsterSkillHost.TryGetMonster(uint entityId, [MaybeNullWhen(false)] out MonsterInstance monster) =>
        _monsters.TryGetValue(entityId, out monster);

    IEnumerable<MonsterInstance> IFieldMonsterSkillHost.Monsters => _monsters.Values;

    IEnumerable<WorldPlayer> IFieldMonsterSkillHost.Players => _players.Values;

    IReadOnlyList<PreyCandidate> IFieldMonsterSkillHost.NearbyPlayers(Vector3 origin, float radius) => _prey.Nearby(origin, radius);

    IReadOnlyList<MonsterDefinition> IFieldMonsterSkillHost.MonsterDefinitions => _catalog.All;

    MonsterInstance IFieldMonsterSkillHost.SpawnMonster(MonsterDefinition definition, Vector3 position, float facing, int spawnPointIndex) =>
        ((ISpawnSink)this).Spawn(definition, position, spawnPointIndex, facing);

    void IFieldMonsterSkillHost.RemoveMonster(uint entityId)
    {
        _monsters.Remove(entityId);
        _combat.ForgetMonster(entityId);
    }

    bool IFieldMonsterSkillHost.ApplyStatus(uint entity, EffectSpec spec, float durationMs, uint source) =>
        _skills.ApplyMonsterStatus(entity, spec, durationMs, source);

    void IFieldMonsterSkillHost.EndStuns(MonsterInstance monster) => _skills.EndStuns(monster);
}
