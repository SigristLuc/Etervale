using System.Net;

namespace PristonTale.Server.Net;

// Connection hardening read from the "Network" configuration section; a value of 0 disables that limit.
internal sealed record NetworkLimits
{
    // The original server disconnected a client that sent more than 251 packets in 5 s.
    public int PacketLimit { get; init; } = 251;
    public int PacketWindowMs { get; init; } = 5000;
    public int MaxConnectionsPerIp { get; init; } = 8;
    public int LoginTimeoutMs { get; init; } = 30_000;
    public int LoginFailuresPerAccount { get; init; } = 5;
    public int LoginFailuresPerIp { get; init; } = 20;
    public int LoginFailureWindowMs { get; init; } = 300_000;
    public int PlayerInfoBurst { get; init; } = 512;
    public int PlayerInfoPerSecond { get; init; } = 128;

    public static NetworkLimits From(IConfiguration configuration)
    {
        var defaults = new NetworkLimits();
        return new NetworkLimits
        {
            PacketLimit = Read(configuration, "PacketLimit", defaults.PacketLimit),
            PacketWindowMs = Read(configuration, "PacketWindowSeconds", defaults.PacketWindowMs / 1000) * 1000,
            MaxConnectionsPerIp = Read(configuration, "MaxConnectionsPerIp", defaults.MaxConnectionsPerIp),
            LoginTimeoutMs = Read(configuration, "LoginTimeoutSeconds", defaults.LoginTimeoutMs / 1000) * 1000,
            LoginFailuresPerAccount = Read(configuration, "LoginFailuresPerAccount", defaults.LoginFailuresPerAccount),
            LoginFailuresPerIp = Read(configuration, "LoginFailuresPerIp", defaults.LoginFailuresPerIp),
            LoginFailureWindowMs = Read(configuration, "LoginFailureWindowSeconds", defaults.LoginFailureWindowMs / 1000) * 1000,
            PlayerInfoBurst = Read(configuration, "PlayerInfoBurst", defaults.PlayerInfoBurst),
            PlayerInfoPerSecond = Read(configuration, "PlayerInfoPerSecond", defaults.PlayerInfoPerSecond)
        };
    }

    private static int Read(IConfiguration configuration, string key, int fallback)
    {
        var value = configuration.GetValue("Network:" + key, fallback);
        if (value < 0)
        {
            throw new InvalidOperationException($"Network:{key} must not be negative.");
        }

        return value;
    }

    public static IPAddress? AddressOf(EndPoint? endpoint)
    {
        if (endpoint is not IPEndPoint ip)
        {
            return null;
        }

        return ip.Address.IsIPv4MappedToIPv6 ? ip.Address.MapToIPv4() : ip.Address;
    }
}
