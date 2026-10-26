using System.Globalization;
using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Aging;

namespace PristonTale.Gameplay.UI.Inventory;

// An aged copy's level and its count toward the next level (goal 0 once its aging finished).
public readonly record struct AgingGauge(int Level, int Count, int Goal);

// The original item box: the name, every stat the copy carries with the original labels, the requirements (red when the
// owner does not meet them), then the class specialization. Each line is label and value as one centred string, one
// text line apart. An aged copy shows its aging gauge under the name and the stats its aging raises in light blue.
public static class HudItemDetailTooltip
{
    public const int LineHeight = 16;
    public static readonly Color NameColor = new(222, 231, 255);
    public static readonly Color UniqueColor = new(128, 255, 128);
    public static readonly Color AgedColor = new(255, 250, 0);
    public static readonly Color ValueColor = Color.White;
    public static readonly Color RequirementColor = new(255, 180, 100);
    public static readonly Color UnmetColor = new(255, 0, 0);
    public static readonly Color SpecHeaderColor = new(255, 220, 0);
    public static readonly Color SpecColor = new(164, 199, 41);
    public static readonly Color PriceColor = new(247, 243, 193);
    public static readonly Color HintColor = new(200, 200, 200);
    public static readonly Color AgingColor = new(119, 200, 254);
    private const int GaugeWidth = 130;
    private const int GaugeFill = 125;

    private static readonly string[] ResistanceLabels =
        ["Organic Type:  ", "Nature Type:  ", "Flame Type:  ", "Frost Type:  ", "Lightning Type:  ", "Poison Type: ", "Water Type:  ", "Wind Type:  "];

    public static List<(string Text, Color Color)> Lines(OwnedItemView item, OwnerState owner, string price, string hint)
    {
        var lines = new List<(string, Color)>();
        var details = item.Details;
        if (details is null)
        {
            lines.Add((item.Name, NameColor));
            AddTail(lines, price, hint);
            return lines;
        }

        var roll = details.Roll;
        var nameColor = details.UniqueItem != 0 ? UniqueColor : details.UpgradeLevel > 0 ? AgedColor : NameColor;
        lines.Add((item.Name, nameColor));
        var aged = Aged(details);
        if (aged)
        {
            // Room for the gauge.
            lines.Add(("", ValueColor));
            lines.Add(("", ValueColor));
        }

        var family = aged && details.UpgradeLevel > 0 ? AgingRules.Family(item.CatalogKey) : "";
        Add(lines, roll.DamageMin != 0 || roll.DamageMax != 0, "Attack Power:  ", $"{roll.DamageMin}-{roll.DamageMax}",
            Raised(family, "WA1", "WC1", "WH1", "WM1", "WP1", "WS1", "WS2", "WT1"));
        Add(lines, details.AttackSpeed != 0, "Weapon Speed:  ", Whole(details.AttackSpeed));
        Add(lines, details.ShootingRange != 0, "Range:  ", Whole(details.ShootingRange));
        Add(lines, details.Critical != 0, "Critical:  ", details.Critical + "%", Raised(family, "WC1", "WP1", "WS1", "WT1", "WS2"));
        Add(lines, roll.Defence != 0, "Defense Rating:  ", Whole(roll.Defence), Raised(family, "OM1", "DA1", "DA2", "DB1", "DG1", "OA2"));
        Add(lines, roll.AttackRating != 0, "Attack Rating:  ", Whole(roll.AttackRating),
            Raised(family, "WA1", "WC1", "WM1", "WH1", "WP1", "WS2", "OA2"));
        Add(lines, roll.Absorb != 0, "Absorb Rating:  ", Tenths(roll.Absorb + ItemAbsorbCorrection.For(item.CatalogKey)),
            Raised(family, "OM1", "DA1", "DS1", "DA2", "DB1", "DG1"));
        Add(lines, roll.Block != 0, "Block Rating:  ", (int)roll.Block + "%", Raised(family, "DS1"));
        Add(lines, roll.MoveSpeed != 0, "Speed:  ", Tenths(roll.MoveSpeed));
        Add(lines, details.DurabilityCurrent != 0 || details.DurabilityMax != 0, "Integrity:  ", $"{details.DurabilityCurrent}/{details.DurabilityMax}",
            Raised(family, "WA1", "WH1", "WS1", "DS1", "OM1", "DA1", "DA2", "DB1", "DG1", "OA2"));
        var resistances = roll.Resistances;
        short[] elements = [resistances.Bionic, resistances.Earth, resistances.Fire, resistances.Ice, resistances.Lighting, resistances.Poison,
            resistances.Water, resistances.Wind];
        for (var element = 0; element < elements.Length; element++)
        {
            Add(lines, elements[element] != 0, ResistanceLabels[element], Whole(elements[element]));
        }

        var forceOrb = item.Kind == ItemKind.ForceOrb;
        Add(lines, roll.LifeRegen != 0 && !forceOrb, "HP Regen:  ", Tenths(roll.LifeRegen));
        Add(lines, roll.ManaRegen != 0 && !forceOrb, "MP Regen: ", Tenths(roll.ManaRegen));
        Add(lines, roll.StaminaRegen != 0 && !forceOrb, "STM Regen: ", Tenths(roll.StaminaRegen));
        Add(lines, roll.AddLife != 0, "Add. HP: ", Whole(roll.AddLife));
        Add(lines, roll.AddMana != 0, "Add. MP:  ", Whole(roll.AddMana), Raised(family, "WM1", "WS1", "WT1"));
        Add(lines, roll.AddStamina != 0, "Add. STM:  ", Whole(roll.AddStamina));
        Add(lines, details.PotionSpace != 0, "Potion Storage:  ", Whole(details.PotionSpace));

        var level = owner?.Level ?? 0;
        var required = roll.Requirements;
        Requirement(lines, roll.RequiredLevel, level, "Req. Level:  ");
        Requirement(lines, required.Strength, owner?.Strength ?? 0, "Req. Strength:  ");
        if (!forceOrb)
        {
            Requirement(lines, required.Spirit, owner?.Spirit ?? 0, "Req. Spirit:  ");
        }

        Requirement(lines, required.Talent, owner?.Talent ?? 0, "Req. Talent:  ");
        Requirement(lines, required.Agility, owner?.Agility ?? 0, "Req. Agility:  ");
        Requirement(lines, required.Health, owner?.Health ?? 0, "Req. Health:  ");
        if (roll.SpecJob != 0 && !forceOrb)
        {
            AddSpecialization(lines, roll, forceOrb);
        }

        AddTail(lines, price, hint);
        return lines;
    }

    // The gauge of an aged copy, with the owner's live count of a worn copy when the server sent one.
    public static AgingGauge? Gauge(OwnedItemView item, OwnerState owner)
    {
        if (item?.Details is not { } details || !Aged(details))
        {
            return null;
        }

        var count = details.AgingCount;
        var goal = details.AgingGoal;
        foreach (var progress in owner?.Aging ?? [])
        {
            if (progress.InstanceId == item.InstanceId)
            {
                (count, goal) = (progress.Count, progress.Goal);
            }
        }

        return new AgingGauge(details.UpgradeLevel, count, goal);
    }

    public static void Draw(HudPainter painter, HudLayout layout, Point pointer, IReadOnlyList<(string Text, Color Color)> lines,
        AgingGauge? gauge = null)
    {
        var textWidth = gauge is null ? 0 : GaugeWidth;
        foreach (var line in lines)
        {
            textWidth = Math.Max(textWidth, painter.Measure(line.Text));
        }

        var width = Math.Min(layout.Width - 8, textWidth + 28);
        var size = HudPainter.InfoBoxBounds(0, 0, width, 26 + (lines.Count - 1) * LineHeight + 8);
        var bounds = layout.PlaceOverlay(new Point(pointer.X - size.Width / 2, pointer.Y - size.Height - 8), size.Width, size.Height);
        bounds = HudPainter.InfoBoxBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        painter.Flush();
        painter.InfoBox(bounds);
        for (var index = 0; index < lines.Count; index++)
        {
            var y = index == 0 ? bounds.Y + 9 : bounds.Y + 26 + (index - 1) * LineHeight;
            painter.TextCentered(painter.Fit(lines[index].Text, bounds.Width - 16), bounds.Left, bounds.Right, y, lines[index].Color);
        }

        if (gauge is { } aging)
        {
            DrawGauge(painter, bounds, aging);
        }
    }

    // A maturing copy shows the bar filling toward its goal, a finished one the small plate; both carry the level.
    private static void DrawGauge(HudPainter painter, Rectangle bounds, AgingGauge gauge)
    {
        var center = bounds.X + bounds.Width / 2;
        var top = bounds.Y + 26;
        if (gauge.Goal > 0)
        {
            var left = center - GaugeWidth / 2;
            painter.Sprite(Npc.HudCraftArt.AgingBar, left, top);
            var fill = (int)(GaugeFill * Math.Clamp(gauge.Count / (float)gauge.Goal, 0f, 1f));
            if (fill > 0)
            {
                painter.Sprite(Npc.HudCraftArt.AgingFill, left + 2, top + 7, new Rectangle(0, 0, fill, 4));
            }
        }
        else
        {
            painter.Sprite(Npc.HudCraftArt.AgingPlate, center - 11, top);
        }

        painter.TextCentered($"+{gauge.Level}", center - 20, center + 20, top + 3, Color.Black, false, Point.Zero);
    }

    private static bool Aged(ItemDetailView details) => details.UpgradeLevel > 0 || details.AgingGoal > 0;

    private static Color Raised(string family, params string[] families) =>
        family.Length > 0 && Array.IndexOf(families, family) >= 0 ? AgingColor : ValueColor;

    private static void AddSpecialization(List<(string, Color)> lines, ItemRoll roll, bool forceOrb)
    {
        var spec = roll.Spec;
        lines.Add((SpecClassName(roll.SpecJob) + " Spec. ", SpecHeaderColor));
        AddSpec(lines, spec.AttackSpeed != 0, "Spec. ATK SPD:  ", Whole(spec.AttackSpeed));
        AddSpec(lines, spec.Critical != 0, "Spec. CRIT:  ", spec.Critical + "%");
        AddSpec(lines, spec.Defence != 0, "Spec. DEF RTG:  ", Whole(spec.Defence));
        AddSpec(lines, spec.Absorb != 0, "Spec. ABS RTG:  ", Tenths(spec.Absorb));
        AddSpec(lines, spec.Block != 0, "Spec. BLK RTG:  ", (int)spec.Block + "%");
        AddSpec(lines, spec.MagicMastery != 0, "Magic APT:  ", Whole((int)spec.MagicMastery));
        AddSpec(lines, spec.MoveSpeed != 0, "Spec. SPD:  ", Tenths(spec.MoveSpeed));
        AddSpec(lines, spec.LevelDamage != 0, "Spec. ATK POW:  ", "LV/" + spec.LevelDamage);
        AddSpec(lines, spec.LevelAttackRating != 0, "Spec. ATK RTG:  ", "LV/" + spec.LevelAttackRating);
        AddSpec(lines, spec.ShootingRange != 0, "Spec. RNG:  ", Whole(spec.ShootingRange));
        AddSpec(lines, spec.LevelLife != 0, "Max HP Boost:  ", "LV/" + spec.LevelLife);
        AddSpec(lines, spec.LevelMana != 0, "Max MP Boost:  ", "LV/" + spec.LevelMana);
        AddSpec(lines, spec.LifeRegen != 0 && !forceOrb, "HP Recovery:  ", Tenths(spec.LifeRegen));
        AddSpec(lines, spec.ManaRegen != 0, "MP Recovery:  ", Tenths(spec.ManaRegen));
        AddSpec(lines, spec.StaminaRegen != 0, "STM Recovery:  ", Tenths(spec.StaminaRegen));
    }

    private static void AddTail(List<(string, Color)> lines, string price, string hint)
    {
        if (!string.IsNullOrEmpty(price))
        {
            lines.Add(("", PriceColor));
            lines.Add((price, PriceColor));
        }

        if (!string.IsNullOrEmpty(hint))
        {
            lines.Add((hint, HintColor));
        }
    }

    private static void Add(List<(string, Color)> lines, bool shown, string label, string value, Color? color = null)
    {
        if (shown)
        {
            lines.Add((label + value, color ?? ValueColor));
        }
    }

    private static void AddSpec(List<(string, Color)> lines, bool shown, string label, string value)
    {
        if (shown)
        {
            lines.Add((label + value, SpecColor));
        }
    }

    private static void Requirement(List<(string, Color)> lines, int required, int actual, string label)
    {
        if (required != 0)
        {
            lines.Add((label + Whole(required), required > actual ? UnmetColor : RequirementColor));
        }
    }

    private static string Whole(int value) => value.ToString(CultureInfo.InvariantCulture);

    // One decimal, truncated, as the original rates absorb, speed and regeneration.
    public static string Tenths(float value)
    {
        var tenths = (int)(value * 10.001f);
        return $"{tenths / 10}.{Math.Abs(tenths % 10)}";
    }

    private static string SpecClassName(uint job)
    {
        var characterClass = (JobBitCode)job switch
        {
            JobBitCode.Mechanician => PlayerClass.Mechanician,
            JobBitCode.Fighter => PlayerClass.Fighter,
            JobBitCode.Pikeman => PlayerClass.Pikeman,
            JobBitCode.Archer => PlayerClass.Archer,
            JobBitCode.Knight => PlayerClass.Knight,
            JobBitCode.Atalanta => PlayerClass.Atalanta,
            JobBitCode.Priestess => PlayerClass.Priestess,
            JobBitCode.Magician => PlayerClass.Magician,
            JobBitCode.Assassin => PlayerClass.Assassin,
            JobBitCode.Shaman => PlayerClass.Shaman,
            _ => (PlayerClass?)null
        };
        return characterClass.HasValue ? HudJobTitles.For(characterClass.Value)[0] : "Class";
    }
}
