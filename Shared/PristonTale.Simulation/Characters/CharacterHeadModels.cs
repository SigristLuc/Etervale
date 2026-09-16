namespace PristonTale.Simulation.Characters;

internal static class CharacterHeadModels
{
    internal readonly record struct Pattern(string Prefix, string Suffix, int VariantCount = CharacterVisualSet.HairPotionCount);

    private static readonly Dictionary<PlayerClass, Pattern[]> ByClass = new()
    {
        [PlayerClass.Mechanician] =
        [
            new("tmh-a", ""),
            new("tmh-a", "a"),
            new("tmh_a", "b"),
            new("tmh-a", "c")
        ],
        [PlayerClass.Fighter] =
        [
            new("tmh-b", ""),
            new("tmh-b", "a"),
            new("tmh_b", "b"),
            new("tmh-b", "c")
        ],
        [PlayerClass.Pikeman] =
        [
            new("tmh-c", ""),
            new("tmh-c", "a"),
            new("tmh_c", "b"),
            new("tmhC-", "c")
        ],
        [PlayerClass.Archer] =
        [
            new("tfh-D", ""),
            new("tfh-D", "a"),
            new("tfh_D", "b"),
            new("tfh-D", "c")
        ],
        [PlayerClass.Knight] =
        [
            new("Mmh-A", ""),
            new("Mmh-A", "a"),
            new("Mmh_A", "b"),
            new("Mmh-A", "c")
        ],
        [PlayerClass.Atalanta] =
        [
            new("Mfh-B", ""),
            new("Mfh-B", "a"),
            new("Mfh_B", "b"),
            new("Mfh-B", "c")
        ],
        [PlayerClass.Priestess] =
        [
            new("Mfh-C", ""),
            new("Mfh-C", "a"),
            new("Mfh_C", "b"),
            new("Mfh-C", "c")
        ],
        [PlayerClass.Magician] =
        [
            new("Mmh-D", ""),
            new("Mmh-D", "a"),
            new("Mmh_D", "b"),
            new("Mmh-D", "c")
        ],
        [PlayerClass.Assassin] =
        [
            new("tfh-e", ""),
            new("tfh-e", "a", CharacterFace.FaceCount),
            new("tfh-e", "b", CharacterFace.FaceCount),
            new("tfh-e", "c", CharacterFace.FaceCount)
        ],
        [PlayerClass.Shaman] =
        [
            new("Mmh-E", ""),
            new("Mmh-E", "a", CharacterFace.FaceCount),
            new("Mmh-E", "b", CharacterFace.FaceCount),
            new("Mmh-E", "c", CharacterFace.FaceCount)
        ]
    };

    internal static Pattern Get(PlayerClass playerClass, int tier)
    {
        if (!ByClass.TryGetValue(playerClass, out var tiers))
        {
            throw new ArgumentOutOfRangeException(nameof(playerClass));
        }

        return tiers[tier];
    }
}
