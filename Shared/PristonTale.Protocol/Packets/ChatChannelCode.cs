namespace PristonTale.Protocol.Packets;

// The kind of a chat line the server sends (ChatLinePacket.Kind): it selects the colour and the chat tab of a line on the
// client.
public static class ChatChannelCode
{
    public const byte Server = 0;
    public const byte Whisper = 1;
    public const byte System = 2;
    public const byte Notice = 3;
    public const byte Party = 4;
    public const byte Clan = 5;
    public const byte Trade = 6;
    public const byte Assist = 9;

    // A spoken line of a player.
    public const byte Player = 0x80;
}
