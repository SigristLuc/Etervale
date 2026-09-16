namespace PristonTale.Simulation.World.Ai;

public readonly record struct MonsterAttackEvent(uint AttackerEntityId, uint TargetEntityId, bool IsSkill);
