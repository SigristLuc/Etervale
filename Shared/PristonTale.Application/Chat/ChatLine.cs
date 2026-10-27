using PristonTale.Contracts.Social;

namespace PristonTale.Application.Chat;

// One line for one receiver. SpeakerId is the entity that said it (0 for server lines): the receiver shows it in a
// bubble over that entity when it sees it.
public readonly record struct ChatLine(ChatKind Kind, ulong SpeakerId, string Text)
{
    public static ChatLine Reply(string text) => new(ChatKind.Server, 0, text);
}
