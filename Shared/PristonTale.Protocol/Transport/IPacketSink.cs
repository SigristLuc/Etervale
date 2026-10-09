namespace PristonTale.Protocol.Transport;

// Destination for complete Priston packets: Send is smWINSOCK::Send, SendEncrypted is Send2.
public interface IPacketSink
{
    bool Send(ReadOnlySpan<byte> packet);

    bool SendEncrypted(ReadOnlySpan<byte> packet);
}
