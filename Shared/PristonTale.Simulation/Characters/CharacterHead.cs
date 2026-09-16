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
