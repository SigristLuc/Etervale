using System.Numerics;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Skills;

namespace PristonTale.Tests;

// Revive skills: a dead player near the caster rises where it fell with half its life and a short guard.
internal static class SkillResurrectionTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("resurrection revives the nearest dead ally with half life and a guard, and needs one", ReviveAsync);
    }

    private static Task ReviveAsync()
    {
        var field = SkillFixtures.Field();
        var priest = SkillFixtures.Player(field, 1, PlayerClass.Priestess, 60, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Staff));
        var fallen = SkillFixtures.Player(field, 2, PlayerClass.Knight, 40, new Vector3(200, 0, 0));
        priest.Character.Skills.Restore(0x07040002, 10);
        SkillFixtures.Refuse(SkillCastVerdict.InvalidTarget, "resurrection cast without a dead ally", field, priest, 0x07040002);
        var revived = false;
        var fellAt = Vector3.Zero;
        for (var attempt = 0; attempt < 6 && !revived; attempt++)
        {
            fallen.ApplyDamage(fallen.Hp + 1000);
            fellAt = fallen.Position;
            Assert.True(fallen.IsDead, "fixture did not kill the ally");
            SkillFixtures.Accept("resurrection", field, priest, 0x07040002);
            var events = SkillFixtures.FinishCollect(field, priest);
            revived = !fallen.IsDead && events.Any(value => value.Kind == Application.World.CombatEventKindView.Revive && value.TargetId == fallen.EntityId);
            if (!revived)
            {
                SkillFixtures.Wait(field, 18f);
            }
        }

        Assert.True(revived, "a 94% resurrection never succeeded");
        Assert.True(fallen.Position == fellAt && Math.Abs(fallen.Hp - fallen.MaxHp / 2) <= fallen.MaxHp / 50 + 5, $"revived with {fallen.Hp}/{fallen.MaxHp}");
        var life = fallen.Hp;
        fallen.ApplyDamage(50);
        Assert.True(fallen.Hp == life, "the revive guard let damage through");
        SkillFixtures.Wait(field, 6f);
        var guarded = fallen.Hp;
        fallen.ApplyDamage(50);
        Assert.True(fallen.Hp == guarded - 50, "damage after the guard was ignored");
        return Task.CompletedTask;
    }
}
