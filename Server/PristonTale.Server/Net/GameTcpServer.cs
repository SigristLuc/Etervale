using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using PristonTale.Application.Chat;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Protocol.Transport;
using PristonTale.Server.Hosting;

namespace PristonTale.Server.Net;

public sealed class GameTcpServer(
    AuthAppService auth,
    CharacterAppService characters,
    SessionAppService sessionsApp,
    ISessionRegistry sessions,
    WorldServer world,
    WorldDataHolder worldData,
    NpcAppService npc,
    ItemUseAppService itemUse,
    RestartAppService restart,
    WingGateAppService wingGates,
    QuestAppService quests,
    ChatAppService chat,
    GameServerControl control,
    IConfiguration configuration,
    ILogger<GameTcpServer> logger) : BackgroundService
{
    // The client heartbeats every 4 s; silence beyond this closes the connection.
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);
    private readonly TimeSpan _reportInterval = TimeSpan.FromSeconds(Math.Max(1, configuration.GetValue("Server:ReportSeconds", World.WorldTickService.DefaultReportSeconds)));
    private static readonly HashSet<string> NormalCloseReasons = ["peer closed", "receive ended", "cancelled", "server stopping", "disposed"];
    private readonly ConcurrentDictionary<ClientSession, byte> _clients = new();
    // Every accepted socket, from the TLS handshake until its session ends.
    private readonly ConcurrentDictionary<Task, byte> _connections = new();
    private readonly TimeSpan _handshakeTimeout = TimeSpan.FromSeconds(Math.Max(1, configuration.GetValue("Network:HandshakeTimeoutSeconds", 10)));
    private readonly NetworkLimits _limits = NetworkLimits.From(configuration);
    private readonly LogSampler _refusedLog = new(5000);
    private readonly LogSampler _closedLog = new(1000);
    private readonly TaskCompletionSource<int> _boundPort = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ConnectionLimiter? _perAddress;
    private LoginThrottle? _loginThrottle;
    private long _closedBytesSent;
    private long _closedBytesReceived;

    public int ConnectionCount => _clients.Count;

    // The port the listener bound; Network:Port 0 asks the operating system for a free one (in-process tests).
    public Task<int> BoundPort => _boundPort.Task;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _perAddress = new ConnectionLimiter(_limits.MaxConnectionsPerIp);
        _loginThrottle = new LoginThrottle(_limits.LoginFailuresPerAccount, _limits.LoginFailuresPerIp, _limits.LoginFailureWindowMs);
        control.Attach(() => _clients.Count, _loginThrottle);
        var services = new SessionServices(auth, characters, sessionsApp, sessions, world, worldData, logger, _limits, _loginThrottle, npc, itemUse,
            restart, wingGates, quests, chat, control);
        using var certificate = ServerCertificate.Load(configuration, logger);
        var certificateContext = SslStreamCertificateContext.Create(certificate, additionalCertificates: null);
        using var listener = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp) { DualMode = true };
        var port = configuration.GetValue("Network:Port", HostingExtensions.DefaultGamePort);
        if (port is < 0 or > 65535)
        {
            var error = new InvalidOperationException("Network:Port must be between 0 and 65535.");
            _boundPort.TrySetException(error);
            throw error;
        }

        try
        {
            listener.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
            listener.Listen(512);
        }
        catch (Exception ex)
        {
            _boundPort.TrySetException(ex);
            throw;
        }

        port = ((IPEndPoint)listener.LocalEndPoint!).Port;
        _boundPort.TrySetResult(port);
        logger.LogInformation("Etervale TLS listening on port {Port}", port);
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

                var connection = RunClientAsync(socket, certificateContext, services, address, stoppingToken);
                _connections[connection] = 0;
                _ = connection.ContinueWith(static (task, state) => ((ConcurrentDictionary<Task, byte>)state!).TryRemove(task, out _), _connections,
                    TaskScheduler.Default);
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

            await Task.WhenAll(_connections.Keys);
            await housekeeping;
        }
    }

    private async Task RunClientAsync(Socket socket, SslStreamCertificateContext certificate, SessionServices services, IPAddress? address,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        FrameConnection connection;
        try
        {
            connection = await TlsTransport.AcceptAsync(socket, certificate, _handshakeTimeout, cancellationToken);
        }
        catch (Exception ex)
        {
            socket.Dispose();
            _perAddress?.Release(address);
            if (!cancellationToken.IsCancellationRequested && _closedLog.TryEnter(Environment.TickCount64, out var skipped))
            {
                logger.LogWarning("TLS handshake with {Address} failed: {Message} ({Suppressed} similar closes not logged)", address, ex.Message, skipped);
            }

            return;
        }

        var session = new ClientSession(connection, services);
        _clients[session] = 0;
        if (cancellationToken.IsCancellationRequested)
        {
            connection.Close("server stopping");
        }

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
                if (elapsed >= _reportInterval)
                {
                    var outRate = (sent - lastSent) / 1024.0 / elapsed.TotalSeconds;
                    logger.LogInformation("TCP: {Connections} connections, {Players} in world, out {OutKiB:F1} KiB/s ({PerConnection:F2} KiB/s per connection), in {InKiB:F1} KiB/s",
                        _clients.Count, world.PlayerCount, outRate, outRate / Math.Max(1, _clients.Count), (received - lastReceived) / 1024.0 / elapsed.TotalSeconds);
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
