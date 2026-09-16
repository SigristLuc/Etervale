using PristonTale.Simulation.Characters;

namespace PristonTale.Simulation.Actors;

public sealed record ActorSocketNames(
    string RightHand,
    string LeftHand,
    string Shield,
    string Back,
    string BowBack,
    string CrossbowBack);

public sealed record PlayerCompositionDef(
    CharacterClassDef ClassDef,
    CharacterAssetRef Body,
    CharacterAssetRef Head,
    CharacterMotionName Motion,
    IReadOnlyList<EquipmentRef> Equipment,
    ActorSocketNames Sockets);

public sealed class PlayerCompositionFactory
{
    private static readonly ActorSocketNames DefaultSockets = new(
        "Bip weapon01",
        "Bip L Hand",
        "Bip Shield",
        "Bip01 Spine2",
        "Bip Bow",
        "Bip CrossBow");

    public PlayerCompositionDef CreateDefault(CharacterClassDef classDef) =>
        new(
            classDef,
            classDef.DefaultVisuals.Body,
            classDef.DefaultVisuals.Head,
            classDef.DefaultMotion,
            classDef.StartingEquipment,
            DefaultSockets);
}
