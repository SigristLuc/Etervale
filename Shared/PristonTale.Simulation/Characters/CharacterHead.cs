namespace PristonTale.Simulation.Characters;

public static class CharacterHead
{
    public static CharacterVisualSet Apply(CharacterVisualSet defaults, PlayerClass playerClass, int tier, int faceIndex, int hairIndex)
    {
        // Existing saves use faceIndex for the three creation variants and zero hairIndex until a hair override exists.
        var variant = hairIndex == 0 ? faceIndex : hairIndex;
        ValidateTier(tier);
        ValidateHairVariant(hairIndex);
        if (!CharacterFace.IsValidIndex(faceIndex))
        {
            throw new ArgumentOutOfRangeException(nameof(faceIndex), faceIndex, $"faceIndex must be 0..{CharacterFace.FaceCount - 1}");
        }

        var pattern = CharacterHeadModels.Get(playerClass, tier);
        if (variant >= pattern.VariantCount)
        {
            // Assassin/Shaman only ship the three creation faces for advanced tiers. A persisted hair
            // potion index from another tier must keep the actor renderable without inventing a filename.
            variant = faceIndex;
        }

        return defaults with
        {
            Head = new CharacterAssetRef(Resolve(playerClass, tier, variant)),
            FaceIndex = faceIndex,
            HairIndex = hairIndex
        };
    }

    public static string Resolve(PlayerClass playerClass, int tier, int hairVariant)
    {
        ValidateTier(tier);
        ValidateHairVariant(hairVariant);

        var pattern = CharacterHeadModels.Get(playerClass, tier);
        if (hairVariant >= pattern.VariantCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(hairVariant),
                hairVariant,
                $"{playerClass} tier {tier} only provides {pattern.VariantCount} head variants.");
        }

        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"char/tmABCD/{pattern.Prefix}{hairVariant + 1:D2}{pattern.Suffix}.inx");
    }

    // The big head of the big-head potion: look 1 or 2, cycling three heads by hair. Archer, Atalanta and Priestess ship
    // only the second look, which they show for both.
    public static string BigHead(PlayerClass playerClass, int look, int hairIndex)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(look, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(look, 2);
        var secondOnly = playerClass is PlayerClass.Archer or PlayerClass.Atalanta or PlayerClass.Priestess;
        var suffix = look == 1 && !secondOnly ? "b" : "t";
        var prefix = CharacterHeadModels.Get(playerClass, 0).Prefix;
        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"char/tmABCD/{prefix}{Math.Abs(hairIndex) % 3 + 1:D2}{suffix}.inx");
    }

    public static CharacterVisualSet ApplyBigHead(CharacterVisualSet visuals, PlayerClass playerClass, int look) =>
        look is 1 or 2 ? visuals with { Head = new CharacterAssetRef(BigHead(playerClass, look, visuals.HairIndex)) } : visuals;

    private static void ValidateTier(int tier)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tier);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tier, 3);
    }

    private static void ValidateHairVariant(int hairVariant)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(hairVariant);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(hairVariant, CharacterVisualSet.HairPotionCount);
    }
}
