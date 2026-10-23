namespace PristonTale.Protocol.Packets;

// The kind field (the first field after the header) of chat lines the server sends: it selects the colour and the chat
// tab of a line on the client.
public static class ChatChannelCode
{
    public const uint Server = 0;
    public const uint Whisper = 1;
    public const uint System = 2;
    public const uint Notice = 3;
    public const uint Party = 4;
    public const uint Clan = 5;
    public const uint Trade = 6;
    public const uint Assist = 9;

    // A spoken line of a player. The classic server put the speaker's network address here; a client draws any code
    // without a colour of its own in the player colour, so the address is not sent.
    public const uint Player = 0x80;
}
