using System.Numerics;
using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.World;

internal static class ProgressionTransactions
{
    public static bool CanReach(WorldPlayer player, WorldNpc npc)
    {
        var delta = player.Position - npc.Position;
        return !player.IsDead && delta.X * delta.X + delta.Z * delta.Z <= NpcInteraction.Reach * NpcInteraction.Reach;
    }

    public static PreparedCharacterOperation<ProgressionOutcome> Prepare(
        PlayerSession session, WorldPlayer player, FieldInstance field, ProgressionCommand command)
    {
        var original = player.Character;
        foreach (var operation in original.ProgressionOperations)
        {
            if (operation.Command.OperationId == command.OperationId)
            {
                var replay = operation.Command == command;
                return new PreparedCharacterOperation<ProgressionOutcome>(original, null, null,
                    new ProgressionOutcome(replay, operation.Revision, replay ? "already committed" : "operation identity conflict"));
            }
        }

        if (player.IsDead || command.Action == ProgressionAction.LearnSkill
            && (!field.TryGetNpc(command.NpcEntityId, out var npc) || !npc.Definition.Has(NpcServices.SkillMaster) || !CanReach(player, npc)))
        {
            return new PreparedCharacterOperation<ProgressionOutcome>(original, null, null,
                new ProgressionOutcome(false, original.ProgressionRevision, "approach a skill master while alive"));
        }

        var candidate = original.CloneForInventoryOperation();
        if (!candidate.ApplyProgression(command))
        {
            return new PreparedCharacterOperation<ProgressionOutcome>(original, null, null,
                new ProgressionOutcome(false, original.ProgressionRevision, "requirements not met or stale progression revision"));
        }

        var snapshot = CharacterRecordFactory.Capture(session.Loaded!, candidate, field.FieldId, player.Position);
        return new PreparedCharacterOperation<ProgressionOutcome>(original, candidate, snapshot,
            new ProgressionOutcome(true, candidate.ProgressionRevision, "committed"));
    }
}
