using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.Effects;
using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Gameplay.World;

// What everyone near sees when a player gains a level (the level-up burst and chime) or trains a skill rank (the
// skill-learn light and sound).
public sealed partial class GameScene
{
    public const string LevelUpEffect = "vfx.player.levelup";

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
