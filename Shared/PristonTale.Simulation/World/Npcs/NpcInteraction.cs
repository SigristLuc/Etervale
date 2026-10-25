namespace PristonTale.Simulation.World.Npcs;

// One typed service of a clicked NPC as the server offers it; Available is false for services Etervale does not run yet.
public sealed record NpcServiceOffer(NpcServiceKind Kind, int Value, bool Available);

public sealed record NpcInteraction(bool Ok, ulong EntityId, string Name, IReadOnlyList<NpcServiceOffer> Services, IReadOnlyList<string> Dialog,
    string Message)
{
    public const float Reach = 96f;

    public NpcServiceSet ServiceSet
    {
        get
        {
            var set = NpcServiceSet.None;
            foreach (var offer in Services)
            {
                set = set.With(offer.Kind);
            }

            return set;
        }
    }

    public bool Offers(NpcServiceKind kind) => Ok && Services.Any(offer => offer.Kind == kind && offer.Available);

    public static NpcInteraction Rejected(string message) => new(false, 0, "", [], [], message);
}
