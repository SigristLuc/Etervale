using PristonTale.Contracts.Social;

namespace PristonTale.Application.World;

// A line for one player that a world change answered (a party or a trade), delivered through the chat after the world
// answered (ChatDirectory.Deliver).
public readonly record struct WorldNotice(ulong To, ChatKind Kind, string Text);
