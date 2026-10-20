using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Numerics;
using System.Threading.Channels;
using PristonTale.Application.Services;
using PristonTale.Client.Net.Online;
using PristonTale.Client.Net.Presence;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Protocol.Transport;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Client.Net;

// Client side of the original Priston flow over one TCP connection. The original protocol has no request ids, so
// login, character and world-entry requests run one at a time and complete on their reply opcodes; inventory and
// progression commands correlate by operation id and NPC clicks by NPC serial.
public sealed class GameServerConnection : IGameBackend
{
    // Client version of the English retail build; the server's VERSION reply sets the minimum.
    public const int EnglishClientVersion = 3122;
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(4);

    private readonly string _host;
    private readonly int _port;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly object _sendGate = new();
    private readonly byte[] _scratch = new byte[PristonFrameCodec.MaxFrameSize];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly WorldRenderCatalog _catalog = new();
    private readonly PresenceDecoder _presence;
    private readonly RecordDataAssembler _record = new();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<OperationResultPacket>> _operations = new();
    private readonly ConcurrentDictionary<uint, NpcRequest> _npcRequests = new();
    private readonly Channel<PresenceUpdate> _presenceUpdates = Channel.CreateBounded<PresenceUpdate>(new BoundedChannelOptions(4096)
    {
        SingleReader = true,
        SingleWriter = true,
        FullMode = BoundedChannelFullMode.Wait
    });
    private PristonConnection? _connection;
    private Task? _run;
    private Task? _heartbeat;
    private Exception? _terminalError;
    private IReadOnlyList<CharacterSummary> _characters = [];
    private PendingLogin? _login;
    private TaskCompletionSource<CharCommand2>? _characterReply;
    private TaskCompletionSource<EnterWorldOutcome>? _enter;
    private TaskCompletionSource<bool>? _joined;
    private TaskCompletionSource<bool>? _left;
    private RecordDataPacket? _entered;
    private int _versionLimit;
    private int _clientVersion = EnglishClientVersion;
    private string _account = "";
    private bool _disposed;

    public GameServerConnection(string address)
    {
        var uri = new Uri(address, UriKind.Absolute);
        if (uri.Scheme != "tcp" || uri.Host.Length == 0 || uri.Port < 1)
        {
            throw new ArgumentException("Expected a tcp://host:port server address.", nameof(address));
        }

        _host = uri.Host;
        _port = uri.Port;
        _presence = new PresenceDecoder(_catalog, RequestDescription);
    }

    public string? SessionToken { get; private set; }

    public long BytesReceived => _connection?.BytesReceived ?? 0;

    public long BytesSent => _connection?.BytesSent ?? 0;

    public async Task<LoginOutcome> LoginAsync(string account, string password, int clientVersion, CancellationToken ct = default)
    {
        await ConnectAsync(ct);
        await _requestGate.WaitAsync(ct);
        try
        {
            _clientVersion = clientVersion;
            if (!PristonText.IsRepresentable(account, CharCommand2.TextLength) || !PristonText.IsRepresentable(password, CharCommand2.TextLength))
            {
                return LoginOutcome.Denied(LoginFailure.BadCredentials);
            }

            var pending = new PendingLogin();
            _login = pending;
            _account = account;
            Send(new CharCommand2(PristonOpcodes.IdGetUserInfo, 0, 0, 0, account, password), encrypt: true);
            var outcome = await pending.Completion.Task.WaitAsync(ct);
            if (outcome.Ok)
            {
                SessionToken = outcome.SessionToken;
            }

            return outcome;
        }
        finally
        {
            _login = null;
            _requestGate.Release();
        }
    }

    public Task<IReadOnlyList<CharacterSummary>> ListCharactersAsync(CancellationToken ct = default)
    {
        ThrowIfDisconnected();
        return Task.FromResult(Volatile.Read(ref _characters));
    }

    public async Task<CreateCharacterOutcome> CreateCharacterAsync(string name, int classId, int faceIndex, CancellationToken ct = default)
    {
        if (!PristonText.IsRepresentable(name, CharCommand2.TextLength))
        {
            return CreateCharacterOutcome.Failed(CreateCharacterFailure.NameLength);
        }

        // INSRECORDDATA carries the class code and face in LParam/SParam, which the original leaves unset.
        var reply = await CharacterRequestAsync(new CharCommand2(PristonOpcodes.InsRecordData, classId, 0, faceIndex, _account, name), ct);
        if (reply.WParam == 0)
        {
            return CreateCharacterOutcome.Failed((CreateCharacterFailure)reply.LParam);
        }

        var summary = _characters.FirstOrDefault(candidate => string.Equals(candidate.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        return summary is null ? CreateCharacterOutcome.Failed(CreateCharacterFailure.Rejected) : CreateCharacterOutcome.Success(summary);
    }

    public async Task<DeleteCharacterOutcome> DeleteCharacterAsync(string name, CancellationToken ct = default)
    {
        if (!PristonText.IsRepresentable(name, CharCommand2.TextLength))
        {
            return DeleteCharacterOutcome.Denied("character not found");
        }

        var reply = await CharacterRequestAsync(new CharCommand2(PristonOpcodes.DelRecordData, 0, 0, 0, _account, name), ct);
        return reply.WParam != 0 ? DeleteCharacterOutcome.Success() : DeleteCharacterOutcome.Denied("character not found");
    }

    private async Task<CharCommand2> CharacterRequestAsync(CharCommand2 request, CancellationToken ct)
    {
        await _requestGate.WaitAsync(ct);
        try
        {
            var pending = new TaskCompletionSource<CharCommand2>(TaskCreationOptions.RunContinuationsAsynchronously);
            _characterReply = pending;
            Send(request, encrypt: true);
            return await pending.Task.WaitAsync(ct);
        }
        finally
        {
            _characterReply = null;
            _requestGate.Release();
        }
    }

    public async Task<EnterWorldOutcome> EnterWorldAsync(string characterName, CancellationToken ct = default)
    {
        if (!PristonText.IsRepresentable(characterName, CharCommand.NameLength))
        {
            return EnterWorldOutcome.Denied(EnterWorldFailure.NotFound);
        }

        await _requestGate.WaitAsync(ct);
        try
        {
            var pending = new TaskCompletionSource<EnterWorldOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            _enter = pending;
            // GETRECORDDATA {SParam = Client_Version, szName}; the original sends it as plain Send.
            Send(new CharCommand(PristonOpcodes.GetRecordData, 0, 0, _clientVersion, characterName), encrypt: false);
            return await pending.Task.WaitAsync(ct);
        }
        finally
        {
            _enter = null;
            _requestGate.Release();
        }
    }

    public IPlayChannel OpenPlayChannel(CancellationToken ct = default) => new OnlinePlayChannel(this, ct);

    public Task<WorldRenderCatalog> GetWorldCatalogAsync(CancellationToken ct = default) => Task.FromResult(_catalog);

    internal async Task OpenPlayAsync(CancellationToken ct)
    {
        await _requestGate.WaitAsync(ct);
        try
        {
            var entered = _entered ?? throw new InvalidOperationException("Enter the world before opening a play channel.");
            var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _joined = pending;
            _presence.SelfSerial = entered.Character.ObjectSerial;
            var info = new PlayerInfoPacket(entered.Character, entered.Character.ObjectSerial, entered.PosX, 0, entered.PosZ);
            lock (_sendGate)
            {
                Connection.Send(_scratch.AsSpan(0, info.Write(_scratch)));
            }

            await pending.Task.WaitAsync(ct);
        }
        finally
        {
            _joined = null;
            _requestGate.Release();
        }
    }

    internal async Task ClosePlayAsync(CancellationToken ct)
    {
        await _requestGate.WaitAsync(ct);
        try
        {
            var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _left = pending;
            Send(new TransCommand(EtervaleOpcodes.LeaveWorld), encrypt: true);
            await pending.Task.WaitAsync(ct);
            _entered = null;
        }
        finally
        {
            _left = null;
            _requestGate.Release();
        }
    }

    internal async Task<InventoryOutcome> ApplyInventoryAsync(InventoryCommand command, CancellationToken ct)
    {
        var packet = new InventoryCommandPacket(command.OperationId, command.ExpectedRevision, (int)command.Action, command.ItemId, command.Destination,
            command.Count, (int)command.Slot);
        var result = await OperationAsync(command.OperationId, buffer => packet.Write(buffer), ct);
        return new InventoryOutcome(result.Ok, result.Revision, result.Message);
    }

    internal async Task<ProgressionOutcome> ApplyProgressionAsync(ProgressionCommand command, CancellationToken ct)
    {
        var packet = new ProgressionCommandPacket(command.OperationId, command.ExpectedRevision, (int)command.Action, (int)command.Attribute,
            command.Amount, command.SkillCode, command.NpcEntityId);
        var result = await OperationAsync(command.OperationId, buffer => packet.Write(buffer), ct);
        return new ProgressionOutcome(result.Ok, result.Revision, result.Message);
    }

    private async Task<OperationResultPacket> OperationAsync(Guid operationId, Func<byte[], int> write, CancellationToken ct)
    {
        ThrowIfDisconnected();
        var pending = new TaskCompletionSource<OperationResultPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_operations.TryAdd(operationId, pending))
        {
            throw new InvalidOperationException("An operation with this id is already in flight.");
        }

        try
        {
            lock (_sendGate)
            {
                Connection.SendEncrypted(_scratch.AsSpan(0, write(_scratch)));
            }

            return await pending.Task.WaitAsync(ct);
        }
        finally
        {
            _operations.TryRemove(operationId, out _);
        }
    }

    internal async Task<NpcInteraction> InteractNpcAsync(uint entityId, CancellationToken ct)
    {
        ThrowIfDisconnected();
        var request = new NpcRequest();
        if (!_npcRequests.TryAdd(entityId, request))
        {
            return NpcInteraction.Rejected("already talking to this NPC");
        }

        try
        {
            SendAttackPacket(entityId, 0, 0);
            return await request.Completion.Task.WaitAsync(ct);
        }
        finally
        {
            _npcRequests.TryRemove(entityId, out _);
        }
    }

    internal void SendMovement(Vector3 position, float facing, bool moving, bool running, uint actionTarget, int fieldCode)
    {
        var serial = _presence.SelfSerial;
        var action = !moving ? MotionStateCode.Stand : running ? MotionStateCode.Run : MotionStateCode.Walk;
        var buffer = new PlayBuffer(PristonUnits.ToFixed(position.X), PristonUnits.ToFixed(position.Y), PristonUnits.ToFixed(position.Z),
            PristonUnits.FrameNone, PristonUnits.ToAngle(facing), action, actionTarget);
        var data = new TransPlayData(serial, 0, 0, 0, 0, 0, (short)fieldCode, -1, 0, 0, [buffer]);
        lock (_sendGate)
        {
            Connection.Send(_scratch.AsSpan(0, data.Write(_scratch)));
        }
    }

    // ATTACK_DAMAGE {AttackState bit 0 = held attack, AttackCount = request sequence}; Power is ignored by the server.
    internal void SendAttackPacket(uint target, int attackState, ulong sequence)
    {
        var attack = new AttackDamagePacket(_presence.SelfSerial, target, 0, 0, 0, attackState, 48 * 256, 0, 0, 0, (uint)Environment.TickCount,
            unchecked((int)sequence), 0, (short)(_presence.FieldId ?? -1));
        lock (_sendGate)
        {
            Connection.SendEncrypted(_scratch.AsSpan(0, attack.Write(_scratch)));
        }
    }

    internal IAsyncEnumerable<PresenceUpdate> ReadPresenceAsync(CancellationToken ct) => _presenceUpdates.Reader.ReadAllAsync(ct);

    private PristonConnection Connection
    {
        get
        {
            ThrowIfDisconnected();
            return _connection ?? throw new InvalidOperationException("Log in before sending a request.");
        }
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        if (_connection is not null)
        {
            return;
        }

        await _connectGate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_connection is not null)
            {
                return;
            }

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(_host, _port, ct);
            }
            catch
            {
                socket.Dispose();
                throw;
            }

            var connection = new PristonConnection(socket);
            _connection = connection;
            _run = RunAsync(connection);
            _heartbeat = HeartbeatAsync(connection);
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private async Task RunAsync(PristonConnection connection)
    {
        Exception? error = null;
        try
        {
            await connection.RunAsync(HandleAsync, _lifetime.Token);
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            error ??= new IOException("Server closed the connection: " + (connection.CloseReason ?? "unknown"));
            Volatile.Write(ref _terminalError, error);
            _login?.Completion.TrySetException(error);
            _characterReply?.TrySetException(error);
            _enter?.TrySetException(error);
            _joined?.TrySetException(error);
            _left?.TrySetException(error);
            foreach (var pending in _operations.Values)
            {
                pending.TrySetException(error);
            }

            foreach (var pending in _npcRequests.Values)
            {
                pending.Completion.TrySetException(error);
            }

            _presenceUpdates.Writer.TryComplete(error);
        }
    }

    private async Task HeartbeatAsync(PristonConnection connection)
    {
        using var timer = new PeriodicTimer(HeartbeatInterval);
        var counter = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(connection.Closed))
            {
                // CHECK_NETSTATE {LParam = counter, WParam = client time}.
                Send(new TransCommand(PristonOpcodes.CheckNetState, ++counter, Environment.TickCount), encrypt: true);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidOperationException)
        {
        }
    }

    private ValueTask HandleAsync(ReadOnlyMemory<byte> memory)
    {
        var packet = memory.Span;
        if (_presence.TryHandle(packet, out var update))
        {
            Publish(update);
            return ValueTask.CompletedTask;
        }

        var code = BinaryPrimitives.ReadUInt32LittleEndian(packet[4..]);
        switch (code)
        {
            case PristonOpcodes.Version:
                _versionLimit = TransCommand.Read(packet).WParam;
                break;
            case PristonOpcodes.FailConnect:
                OnFailConnect(TransCommand.Read(packet));
                break;
            case PristonOpcodes.IdSetUserInfo:
                OnCharacterList(UserCharacterList.Read(packet));
                break;
            case PristonOpcodes.ServerInfo:
                if (_login is { } login)
                {
                    login.ServerList = true;
                    TryCompleteLogin(login);
                }

                break;
            case PristonOpcodes.InsRecordData:
            case PristonOpcodes.DelRecordData:
                _characterReply?.TrySetResult(CharCommand2.Read(packet));
                break;
            case PristonOpcodes.RecordData:
                OnRecordChunk(packet);
                break;
            case PristonOpcodes.Connected:
                _joined?.TrySetResult(true);
                break;
            case EtervaleOpcodes.LeaveWorld:
                _left?.TrySetResult(true);
                break;
            case EtervaleOpcodes.InventoryResult:
            case EtervaleOpcodes.ProgressionResult:
            {
                var result = OperationResultPacket.Read(packet);
                if (_operations.TryGetValue(result.OperationId, out var pending))
                {
                    pending.TrySetResult(result);
                }

                break;
            }
            case PristonOpcodes.WhisperMessage:
            {
                var whisper = ChatMessagePacket.Read(packet);
                if (_npcRequests.TryGetValue(whisper.ObjectSerial, out var request))
                {
                    request.Lines.Add(whisper.Message);
                }

                break;
            }
            case EtervaleOpcodes.NpcResult:
            {
                var result = NpcResultPacket.Read(packet);
                if (_npcRequests.TryGetValue(result.NpcSerial, out var request))
                {
                    request.Completion.TrySetResult(new NpcInteraction(result.Ok, result.NpcSerial, result.Name, (NpcServices)result.Services,
                        request.Lines.ToArray(), result.Message));
                }

                break;
            }
        }

        return ValueTask.CompletedTask;
    }

    private void OnFailConnect(TransCommand failure)
    {
        // FAILCONNECT {WParam = reason, EParam = 1/2 login, 3 = Etervale character-record load}.
        if (failure.EParam == 3)
        {
            _enter?.TrySetResult(EnterWorldOutcome.Denied((EnterWorldFailure)failure.LParam));
        }
        else
        {
            _login?.Completion.TrySetResult(LoginOutcome.Denied((LoginFailure)failure.WParam));
        }
    }

    private void OnCharacterList(UserCharacterList list)
    {
        var summaries = list.Characters.Select(entry => new CharacterSummary(entry.Name, (int)entry.JobCode, entry.Level, entry.FaceIndex, entry.Tier,
            entry.HairIndex, ItemKey(entry.ArmorCode), ItemKey(entry.WeaponCode), ItemKey(entry.OffHandCode), entry.InVillage)).ToArray();
        Volatile.Write(ref _characters, summaries);
        if (_login is { } login)
        {
            login.CharacterList = true;
            TryCompleteLogin(login);
        }
    }

    private void TryCompleteLogin(PendingLogin login)
    {
        if (!login.CharacterList || !login.ServerList)
        {
            return;
        }

        // As in the original, a client older than the server's limit cannot play.
        login.Completion.TrySetResult(_clientVersion < _versionLimit
            ? new LoginOutcome(false, "", "client version is older than the server requires") { Failure = LoginFailure.LoginDenied }
            : LoginOutcome.Success(_account, $"welcome, {_account}"));
    }

    private void OnRecordChunk(ReadOnlySpan<byte> chunk)
    {
        var record = _record.Add(chunk);
        if (record is null)
        {
            return;
        }

        var entered = RecordDataPacket.Read(record);
        _entered = entered;
        _enter?.TrySetResult(EnterWorldOutcome.Success(entered.FieldCode, entered.Character.ObjectSerial, PristonUnits.FromFixed(entered.PosX),
            PristonUnits.FromFixed(entered.PosZ), $"entered field {entered.FieldCode}"));
    }

    private static string ItemKey(uint code) => code != 0 && LegacyItemCode.TryFormat(code, out var key) ? key : "";

    private void Publish(PresenceUpdate? update)
    {
        if (update is not null && !_presenceUpdates.Writer.TryWrite(update))
        {
            throw new InvalidDataException("Client presence queue exceeded its capacity.");
        }
    }

    private void RequestDescription(uint serial) => Send(new TransCommand(PristonOpcodes.GetPlayerInfo, 0, (int)serial), encrypt: false);

    private void Send(TransCommand command, bool encrypt)
    {
        lock (_sendGate)
        {
            var span = _scratch.AsSpan(0, command.Write(_scratch));
            if (encrypt)
            {
                Connection.SendEncrypted(span);
            }
            else
            {
                Connection.Send(span);
            }
        }
    }

    private void Send(CharCommand command, bool encrypt)
    {
        lock (_sendGate)
        {
            var span = _scratch.AsSpan(0, command.Write(_scratch));
            if (encrypt)
            {
                Connection.SendEncrypted(span);
            }
            else
            {
                Connection.Send(span);
            }
        }
    }

    private void Send(CharCommand2 command, bool encrypt)
    {
        lock (_sendGate)
        {
            var span = _scratch.AsSpan(0, command.Write(_scratch));
            if (encrypt)
            {
                Connection.SendEncrypted(span);
            }
            else
            {
                Connection.Send(span);
            }
        }
    }

    private void ThrowIfDisconnected()
    {
        if (Volatile.Read(ref _terminalError) is { } error)
        {
            throw new IOException("TCP connection is closed.", error);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _lifetime.CancelAsync();
        _connection?.Close("client disposed");
        if (_run is not null)
        {
            await _run;
        }

        if (_heartbeat is not null)
        {
            await _heartbeat;
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _lifetime.Dispose();
        _connectGate.Dispose();
        _requestGate.Dispose();
    }

    private sealed class PendingLogin
    {
        public TaskCompletionSource<LoginOutcome> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CharacterList { get; set; }
        public bool ServerList { get; set; }
    }

    private sealed class NpcRequest
    {
        public TaskCompletionSource<NpcInteraction> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Lines { get; } = new();
    }
}
