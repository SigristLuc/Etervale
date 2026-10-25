using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.Effects;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Gameplay.World;

// What everyone near sees when a player gains a level (the level-up burst and chime) or trains a skill rank (the
// skill-learn light and sound).
public sealed partial class GameScene
{
    public const string LevelUpEffect = "vfx.player.levelup";

    // A wolverine leaving at dawn warps out in the return sparkle with the skill-learn sound.
    private void OnMonsterWarp(MonsterActorView monster)
    {
        if (monster.SoundCode != LegacyNpcSoundCode.Wolverine)
        {
            return;
        }

        _returnEffect?.Start(monster.Position + Vector3.Up * GroundTraversal.VisualOffset);
        _itemEffects?.Play(ItemEffectKind.Return, monster.EntityId, 0, monster.Position, PlayerPosition, _skillClockMs);
    }

    private void ObserveProgression(PresenceUpdate update)
    {
        foreach (var combatEvent in update.Events)
        {
            if (combatEvent.Kind is not (RemoteCombatEventKind.LevelUp or RemoteCombatEventKind.SkillLearned)
                || ItemAnchor(combatEvent.TargetId) is not { } position)
            {
                continue;
            }

            if (combatEvent.Kind == RemoteCombatEventKind.LevelUp)
            {
                _skillFx?.Spawn(LevelUpEffect, combatEvent.TargetId, combatEvent.TargetId, position);
            }
            else
            {
                _skillFx?.Spawn(ItemVfx.Learn, combatEvent.TargetId, combatEvent.TargetId, position);
                _itemEffects?.Play(ItemEffectKind.Learn, combatEvent.TargetId, 0, position, PlayerPosition, _skillClockMs);
            }
        }
    }
}
