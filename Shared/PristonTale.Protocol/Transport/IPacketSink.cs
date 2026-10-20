namespace PristonTale.Protocol.Transport;

// Destination for complete Priston packets: Send writes plaintext, SendEncrypted uses the encrypted envelope when it fits.
public interface IPacketSink
{
    bool Send(ReadOnlySpan<byte> packet);

    bool SendEncrypted(ReadOnlySpan<byte> packet);
}
