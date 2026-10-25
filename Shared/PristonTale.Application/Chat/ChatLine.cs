namespace PristonTale.Application.Chat;

// The kind of a chat line; the value is the colour and filter code the client reads (the wire copy is
// PristonTale.Protocol.Packets.ChatChannelCode).
public enum ChatKind : byte
{
    // Replies to the player's own commands and other server lines (orange-red).
    Server = 0,
    Whisper = 1,
    System = 2,
    Notice = 3,
    Party = 4,
    Clan = 5,
    Trade = 6,
    // The assistant window's lines (quest hints).
    Assist = 9,
    // A spoken line of a player.
    Player = 0x80
}

// One line for one receiver. SpeakerId is the entity that said it (0 for server lines): the receiver shows it in a
// bubble over that entity when it sees it.
public readonly record struct ChatLine(ChatKind Kind, ulong SpeakerId, string Text)
{
    public static ChatLine Reply(string text) => new(ChatKind.Server, 0, text);
}
