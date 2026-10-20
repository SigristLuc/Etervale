using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Protocol.Transport;
using PristonTale.Server.Hosting;

namespace PristonTale.Server.Net;

public sealed class PristonTcpServer(
    AuthAppService auth,
    CharacterAppService characters,
    SessionAppService sessionsApp,
    ISessionRegistry sessions,
    WorldServer world,
    WorldDataHolder worldData,
    IConfiguration configuration,
    ILogger<PristonTcpServer> logger) : BackgroundService
{
    // The client heartbeats every 4 s (TransCheckNetwork); silence beyond this closes the connection.
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(30);
    private static readonly HashSet<string> NormalCloseReasons = ["peer closed", "receive ended", "cancelled", "server stopping", "disposed"];
    private readonly ConcurrentDictionary<ClientSession, Task> _clients = new();
    private readonly NetworkLimits _limits = NetworkLimits.From(configuration);
    private readonly LogSampler _refusedLog = new(5000);
    private readonly LogSampler _closedLog = new(1000);
    private ConnectionLimiter? _perAddress;
    private LoginThrottle? _loginThrottle;
    private long _closedBytesSent;
    private long _closedBytesReceived;

    public int ConnectionCount => _clients.Count;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _perAddress = new ConnectionLimiter(_limits.MaxConnectionsPerIp);
        _loginThrottle = new LoginThrottle(_limits.LoginFailuresPerAccount, _limits.LoginFailuresPerIp, _limits.LoginFailureWindowMs);
        var services = new SessionServices(auth, characters, sessionsApp, sessions, world, worldData, logger, _limits, _loginThrottle);
        using var listener = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp) { DualMode = true };
        listener.Bind(new IPEndPoint(IPAddress.IPv6Any, HostingExtensions.DefaultGamePort));
        listener.Listen(512);
        logger.LogInformation("Priston TCP listening on port {Port}", HostingExtensions.DefaultGamePort);
        var housekeeping = HousekeepingAsync(stoppingToken);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var socket = await listener.AcceptAsync(stoppingToken);
                var address = NetworkLimits.AddressOf(socket.RemoteEndPoint);
                if (!_perAddress.TryAcquire(address))
                {
                    socket.Close();
                    if (_refusedLog.TryEnter(Environment.TickCount64, out var suppressed))
                    {
                        logger.LogWarning("Refused connection from {Address}: more than {Limit} open ({Suppressed} similar refusals not logged)",
                            address, _limits.MaxConnectionsPerIp, suppressed);
                    }

                    continue;
                }

                var session = new ClientSession(new PristonConnection(socket), services);
                _clients[session] = RunClientAsync(session, address, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            foreach (var client in _clients.Keys)
            {
                client.Connection.Close("server stopping");
            }

            await Task.WhenAll(_clients.Values);
            await housekeeping;
        }
    }

    private async Task RunClientAsync(ClientSession session, IPAddress? address, CancellationToken cancellationToken)
    {
        await Task.Yield();
        try
        {
            await session.RunAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Client session {Remote} failed", session.Connection.RemoteEndPoint);
        }
        finally
        {
            var connection = session.Connection;
            if (connection.CloseReason is { } reason && !NormalCloseReasons.Contains(reason)
                && _closedLog.TryEnter(Environment.TickCount64, out var suppressed))
            {
                logger.LogWarning("Closed {Remote}: {Reason} ({Suppressed} similar closes not logged)", connection.RemoteEndPoint, reason, suppressed);
            }

            _perAddress?.Release(address);

            Interlocked.Add(ref _closedBytesSent, connection.BytesSent);
            Interlocked.Add(ref _closedBytesReceived, connection.BytesReceived);
            _clients.TryRemove(session, out _);
            await connection.DisposeAsync();
        }
    }

    private async Task HousekeepingAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        var lastReport = DateTime.UtcNow;
        long lastSent = 0;
        long lastReceived = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var now = Environment.TickCount64;
                long sent = Interlocked.Read(ref _closedBytesSent);
                long received = Interlocked.Read(ref _closedBytesReceived);
                _loginThrottle?.Prune(now);
                foreach (var client in _clients.Keys)
                {
                    if (now - client.LastReceivedMs > IdleTimeout.TotalMilliseconds)
                    {
                        client.Connection.Close("idle timeout");
                    }
                    else if (_limits.LoginTimeoutMs > 0 && !client.IsAuthenticated && now - client.ConnectedMs > _limits.LoginTimeoutMs)
                    {
                        // A heartbeat keeps a socket alive, but not past the login deadline.
                        client.Connection.Close("login timeout");
                    }

                    sent += client.Connection.BytesSent;
                    received += client.Connection.BytesReceived;
                }

                var elapsed = DateTime.UtcNow - lastReport;
                if (elapsed >= ReportInterval)
                {
                    logger.LogInformation("TCP: {Connections} connections, {Players} in world, out {OutKiB:F1} KiB/s, in {InKiB:F1} KiB/s",
                        _clients.Count, world.PlayerCount, (sent - lastSent) / 1024.0 / elapsed.TotalSeconds,
                        (received - lastReceived) / 1024.0 / elapsed.TotalSeconds);
                    lastReport = DateTime.UtcNow;
                    lastSent = sent;
                    lastReceived = received;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
