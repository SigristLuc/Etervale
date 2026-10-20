namespace PristonTale.Simulation.Items;

public static class LegacyItemCode
{
    public static bool TryParse(string code, out uint value)
    {
        value = 0;
        if (code.Length != 5
            || !char.IsAsciiDigit(code[3])
            || !char.IsAsciiDigit(code[4]))
        {
            return false;
        }

        var category = code[..3].ToUpperInvariant() switch
        {
            "WA1" => 0x01010000u,
            "WC1" => 0x01020000u,
            "WH1" => 0x01030000u,
            "WM1" => 0x01040000u,
            "WP1" => 0x01050000u,
            "WS1" => 0x01060000u,
            "WS2" => 0x01070000u,
            "WT1" => 0x01080000u,
            "DA1" => 0x02010000u,
            "DB1" => 0x02020000u,
            "DG1" => 0x02030000u,
            "DS1" => 0x02040000u,
            "DA2" => 0x02050000u,
            "OA1" => 0x03010000u,
            "OA2" => 0x03020000u,
            "OM1" => 0x03030000u,
            "OR1" => 0x03040000u,
            "OR2" => 0x03050000u,
            "OS1" => 0x02350000u,
            "FO1" => 0x03060000u,
            "SE1" => 0x03070000u,
            "PR1" => 0x03080000u,
            "PR2" => 0x03090000u,
            "PR3" => 0x03100000u,
            "PR4" => 0x03110000u,
            "PM1" => 0x04010000u,
            "PL1" => 0x04020000u,
            "PS1" => 0x04030000u,
            "GG1" => 0x05010000u,
            "BS1" => 0x05020000u,
            "EC1" => 0x06010000u,
            "QT1" => 0x07010000u,
            "SP1" => 0x08010000u,
            "GP1" => 0x08020000u,
            "QW1" => 0x08030000u,
            "GF1" => 0x08040000u,
            "PZ1" => 0x08060000u,
            "PZ2" => 0x08070000u,
            "CH1" => 0x08080000u,
            "SD2" => 0x08090000u,
            "BC1" => 0x080A0000u,
            "BI1" => 0x080B0000u,
            "BI2" => 0x080C0000u,
            "GP2" => 0x080D0000u,
            "MA1" => 0x09010000u,
            "MA2" => 0x09020000u,
            "WR1" => 0x0A010000u,
            "DR1" => 0x0A020000u,
            _ => 0u,
        };

        if (category == 0)
        {
            return false;
        }

        var ordinal = (uint)((code[3] - '0') * 10 + (code[4] - '0'));
        value = category | (ordinal << 8);
        return true;
    }

    // Inverse of TryParse for the item codes carried on the wire (category | ordinal). Returns false for unknown categories.
    public static bool TryFormat(uint value, out string code)
    {
        code = "";
        var ordinal = (value >> 8) & 0xFF;
        if ((value & 0xFF) != 0 || ordinal > 99 || !Prefixes.TryGetValue(value & 0xFFFF0000u, out var prefix))
        {
            return false;
        }

        code = prefix + ordinal.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }

    private static readonly Dictionary<uint, string> Prefixes = new()
    {
        [0x01010000u] = "WA1",
        [0x01020000u] = "WC1",
        [0x01030000u] = "WH1",
        [0x01040000u] = "WM1",
        [0x01050000u] = "WP1",
        [0x01060000u] = "WS1",
        [0x01070000u] = "WS2",
        [0x01080000u] = "WT1",
        [0x02010000u] = "DA1",
        [0x02020000u] = "DB1",
        [0x02030000u] = "DG1",
        [0x02040000u] = "DS1",
        [0x02050000u] = "DA2",
        [0x03010000u] = "OA1",
        [0x03020000u] = "OA2",
        [0x03030000u] = "OM1",
        [0x03040000u] = "OR1",
        [0x03050000u] = "OR2",
        [0x02350000u] = "OS1",
        [0x03060000u] = "FO1",
        [0x03070000u] = "SE1",
        [0x03080000u] = "PR1",
        [0x03090000u] = "PR2",
        [0x03100000u] = "PR3",
        [0x03110000u] = "PR4",
        [0x04010000u] = "PM1",
        [0x04020000u] = "PL1",
        [0x04030000u] = "PS1",
        [0x05010000u] = "GG1",
        [0x05020000u] = "BS1",
        [0x06010000u] = "EC1",
        [0x07010000u] = "QT1",
        [0x08010000u] = "SP1",
        [0x08020000u] = "GP1",
        [0x08030000u] = "QW1",
        [0x08040000u] = "GF1",
        [0x08060000u] = "PZ1",
        [0x08070000u] = "PZ2",
        [0x08080000u] = "CH1",
        [0x08090000u] = "SD2",
        [0x080A0000u] = "BC1",
        [0x080B0000u] = "BI1",
        [0x080C0000u] = "BI2",
        [0x080D0000u] = "GP2",
        [0x09010000u] = "MA1",
        [0x09020000u] = "MA2",
        [0x0A010000u] = "WR1",
        [0x0A020000u] = "DR1",
    };

    public static uint Parse(string code)
    {
        if (!TryParse(code, out var value))
        {
            throw new FormatException($"Unknown legacy item code '{code}'.");
        }

        return value;
    }
}
