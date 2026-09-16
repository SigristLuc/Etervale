using System.Text;

namespace PristonTale.Gameplay.Audio;

public static class LegacyTalkSound
{
    private static readonly Encoding Cp949 = CreateCp949();

    public static uint GetSoundCode(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return GetSoundCode(Cp949.GetBytes(message));
    }

    internal static uint GetSoundCode(ReadOnlySpan<byte> message)
    {
        uint weighted = 0;
        uint quadratic = 0;
        for (var index = 0; index < message.Length; index++)
        {
            var value = message[index];
            if (value is >= (byte)'a' and <= (byte)'z')
            {
                value -= 0x20;
            }

            weighted = unchecked(weighted + (uint)(value * (index + 1)));
            quadratic = unchecked(quadratic + (uint)(value * index * index));
        }

        return unchecked((quadratic << 24) | ((uint)message.Length << 16) | weighted);
    }

    private static Encoding CreateCp949()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(949);
    }
}
