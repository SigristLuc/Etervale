using System.Buffers.Binary;
using System.Net;
using System.Numerics;
using PristonTale.Application.Chat;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Protocol.Transport;
using PristonTale.Server.Hosting;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Server.Net;

internal sealed record SessionServices(
    AuthAppService Auth,
    CharacterAppService Characters,
    SessionAppService Sessions,
    ISessionRegistry Registry,
    WorldServer World,
    WorldDataHolder WorldData,
    ILogger Logger,
    NetworkLimits Limits,
    LoginThrottle LoginThrottle,
    NpcAppService Npc,
    ItemUseAppService ItemUse,
    RestartAppService Restart,
    QuestAppService Quests,
    ChatAppService Chat,
    GameServerControl Control)
{
    // Shared by every connection so a flood from many peers still yields one warning per interval.
    public LogSampler RejectedPackets { get; } = new(5000);
}

// One client connection following the classic order: Version, IdGetUserInfo, IdSetUserInfo + ServerInfo,
// character records, GetRecordData -> RecordData, PlayerInfo -> Connected, then play traffic.
internal sealed partial class ClientSession : IMovementVerdictSink
{
    // Default minimum client version of the original server; the client compares it with its own version.
    public const int LimitVersion = 167;
    private const long CorrectionIntervalMs = 300;
    // FAILCONNECT -16: "traffic busy" on the original login screen.
    private const int TrafficBusy = -16;

    private readonly PristonConnection _connection;
    private readonly SessionServices _services;
    private readonly byte[] _scratch = new byte[PristonFrameCodec.MaxFrameSize];
    private readonly object _sendGate = new();
    private string? _token;
    private string? _account;
    private PlayerSession? _session;
    private IReadOnlyList<CharacterSummary> _characters = [];
    private WorldPlayer? _player;
    private WorldOutbox? _outbox;
    private PresenceEncoder? _encoder;
    private Task? _pump;
    private long _lastCorrectionMs;
    private readonly PacketRateLimiter _packetRate;
    private readonly TokenBucket _playerInfoRate;

    public ClientSession(PristonConnection connection, SessionServices services)
    {
        _connection = connection;
        _services = services;
        ConnectedMs = Environment.TickCount64;
        LastReceivedMs = ConnectedMs;
        _packetRate = new PacketRateLimiter(services.Limits.PacketLimit, services.Limits.PacketWindowMs);
        _playerInfoRate = new TokenBucket(services.Limits.PlayerInfoBurst, services.Limits.PlayerInfoPerSecond);
        _countingSink = new CountingSink(this);
    }

    public long ConnectedMs { get; }

    public long LastReceivedMs { get; private set; }

    public bool IsAuthenticated => Volatile.Read(ref _token) is not null;

    public PristonConnection Connection => _connection;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // VERSION {WParam = minimum client version, LParam = players in the world when the server is full, else 0}.
        var players = _services.World.PlayerCount;
        Send(new TransCommand(PristonOpcodes.Version, _services.Control.Full(players) ? Math.Max(1, players) : 0, LimitVersion), encrypt: true);
        try
        {
            await _connection.RunAsync(HandleAsync, cancellationToken);
        }
        finally
        {
            await ClosePlayAsync();
            if (_token is not null)
            {
                _services.Registry.Remove(_token);
            }
        }
    }

    private async ValueTask HandleAsync(ReadOnlyMemory<byte> memory)
    {
        var now = Environment.TickCount64;
        LastReceivedMs = now;
        Interlocked.Increment(ref _packetsReceived);
        var code = BinaryPrimitives.ReadUInt32LittleEndian(memory.Span[4..]);
        // Description requests have their own bucket: one is sent per newly seen entity, so a crowd can exceed the
        // original's 28-entity updates; every other packet counts toward the flood limit.
        if (code != EtervaleOpcodes.DescribeEntity && !_packetRate.Admit(now))
        {
            LogRejected(code, "packet flood, closing");
            _connection.Close("packet flood");
            return;
        }

        try
        {
            switch (code)
            {
                case PristonOpcodes.CheckNetState:
                case PristonOpcodes.CheckNetState2:
                case PristonOpcodes.CheckNetState3:
                case PristonOpcodes.CheckNetState4:
                case PristonOpcodes.CheckNetState5:
                {
                    var beat = TransCommand.Read(memory.Span);
                    Send(beat with { SParam = Environment.TickCount }, encrypt: true);
                    if (_player is not null && _session is { } playing)
                    {
                        _services.Chat.Heartbeat(playing.EntityId);
                    }

                    break;
                }
                case PristonOpcodes.ChatMessage:
                    await SayAsync(ChatInputPacket.Read(memory.Span), party: false);
                    break;
                case PristonOpcodes.PartyChatMessage:
                    await SayAsync(ChatInputPacket.Read(memory.Span), party: true);
                    break;
                case PristonOpcodes.IdGetUserInfo:
                    await LoginAsync(CharCommand2.Read(memory.Span));
                    break;
                case PristonOpcodes.IsRecordData:
                {
                    var query = CharCommand2.Read(memory.Span);
                    var exists = await _services.Characters.NameExistsAsync(RequireToken(), query.Name);
                    Send(query with { WParam = exists ? 1 : 0 }, encrypt: true);
                    break;
                }
                case PristonOpcodes.InsRecordData:
                    await CreateCharacterAsync(CharCommand2.Read(memory.Span));
                    break;
                case PristonOpcodes.DelRecordData:
                    await DeleteCharacterAsync(CharCommand2.Read(memory.Span));
                    break;
                case PristonOpcodes.GetRecordData:
                    await EnterWorldAsync(CharCommand.Read(memory.Span));
                    break;
                case PristonOpcodes.PlayerInfo:
                    await JoinWorldAsync(PlayerInfoPacket.Read(memory.Span));
                    break;
                case PristonOpcodes.TransPlayData:
                    ReportMovement(TransPlayData.Read(memory.Span));
                    break;
                case PristonOpcodes.AttackDamage:
                    await AttackAsync(memory);
                    break;
                case EtervaleOpcodes.DescribeEntity:
                    DescribeObject(DescribeEntityPacket.Read(memory.Span));
                    break;
                case PristonOpcodes.RecordData:
                    AcknowledgeClientSave(memory.Span);
                    break;
                case EtervaleOpcodes.InventoryCommand:
                    await ApplyInventoryAsync(InventoryCommandPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.ProgressionCommand:
                    await ApplyProgressionAsync(ProgressionCommandPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.NpcServiceRequest:
                    await RequestNpcServiceAsync(NpcServiceRequestPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.ItemUseCommand:
                    await UseItemAsync(ItemUseCommandPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.RestartRequest:
                    await RestartAsync(RestartRequestPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.QuestRequest:
                    await RequestQuestAsync(QuestRequestPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.SkillCast:
                    await CastSkillAsync(SkillCastPacket.Read(memory.Span));
                    break;
                case EtervaleOpcodes.LeaveWorld:
                    await ClosePlayAsync();
                    Send(new TransCommand(EtervaleOpcodes.LeaveWorld, 0, 1), encrypt: true);
                    break;
                default:
                    // The original dispatcher also ignores codes it does not handle (PlayerInfo2, network-quality reports, ...).
                    break;
            }
        }
        catch (Exception ex) when (ex is InvalidSessionException or InvalidOperationException)
        {
            LogRejected(code, ex.Message);
        }
        catch (System.Data.Common.DbException ex)
        {
            _services.Logger.LogWarning("Storage failed for {Code:X8} from {Remote}: {Message}", code, _connection.RemoteEndPoint, ex.Message);
            ReplyStorageFailure(code, memory.Span);
        }
    }

    // Persistence errors answer the pending request instead of dropping the connection.
    private void ReplyStorageFailure(uint code, ReadOnlySpan<byte> packet)
    {
        switch (code)
        {
            case PristonOpcodes.IdGetUserInfo:
                Send(new TransCommand(PristonOpcodes.FailConnect, 0, TrafficBusy, 0, 2), encrypt: true);
                break;
            case PristonOpcodes.InsRecordData:
            case PristonOpcodes.DelRecordData:
            case PristonOpcodes.IsRecordData:
                Send(CharCommand2.Read(packet) with { WParam = 0, LParam = (int)CreateCharacterFailure.Rejected }, encrypt: true);
                break;
            case PristonOpcodes.GetRecordData:
                Send(new TransCommand(PristonOpcodes.FailConnect, (int)EnterWorldFailure.Rejected, 0, 0, 3), encrypt: true);
                break;
        }
    }

    private async Task LoginAsync(CharCommand2 request)
    {
        if (_token is not null)
        {
            Send(new TransCommand(PristonOpcodes.FailConnect, 0, -4, 0, 2), encrypt: true);
            return;
        }

        var address = NetworkLimits.AddressOf(_connection.RemoteEndPoint);
        var throttle = _services.LoginThrottle;
        if (throttle.IsBlocked(address, request.Id, Environment.TickCount64))
        {
            LogRejected(PristonOpcodes.IdGetUserInfo, "login throttled");
            Send(new TransCommand(PristonOpcodes.FailConnect, 0, TrafficBusy, 0, 2), encrypt: true);
            return;
        }

        var outcome = await _services.Auth.LoginAsync(request.Id, request.Name);
        if (!outcome.Ok || !_services.Registry.TryGet(outcome.SessionToken, out var session))
        {
            if (outcome.Failure == LoginFailure.BadCredentials)
            {
                throttle.RecordFailure(address, request.Id, Environment.TickCount64);
            }

            Send(new TransCommand(PristonOpcodes.FailConnect, 0, (int)outcome.Failure, 0, 2), encrypt: true);
            return;
        }

        throttle.RecordSuccess(request.Id);
        if (session.GmLevel == 0 && _services.Control.Full(_services.World.PlayerCount))
        {
            // Only game masters may join a full server.
            _services.Registry.Remove(outcome.SessionToken);
            Send(new TransCommand(PristonOpcodes.FailConnect, 0, TrafficBusy, 0, 2), encrypt: true);
            return;
        }

        Volatile.Write(ref _token, outcome.SessionToken);
        _account = request.Id;
        _session = session;
        await SendCharacterListAsync();
        SendServerList();
    }

    private async Task SendCharacterListAsync()
    {
        _characters = await _services.Characters.ListCharactersAsync(RequireToken());
        var list = new UserCharacterList(_account!, _characters.Take(UserCharacterList.MaxCharacters).Select(WireMapping.Entry).ToArray());
        SendLarge(list.Write, UserCharacterList.Size, encrypt: true);
    }

    private void SendServerList()
    {
        // The original client opens its game socket from this entry and reuses the login socket when it matches.
        var address = LocalAddress() ?? "127.0.0.1";
        var port = _connection.LocalEndPoint is IPEndPoint local ? (uint)local.Port : HostingExtensions.DefaultGamePort;
        var entry = new PristonServerEntry("Etervale", address, "", "", port, port, 0, 0);
        var list = new PristonServerList("Etervale", (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 0, 0, 1, [entry]);
        var size = PristonServerListCodec.HeaderSize + PristonServerListCodec.EntrySize;
        SendLarge(buffer => PristonServerListCodec.Write(buffer, list, PristonText.Strict), size, encrypt: true);
    }

    private string? LocalAddress() => _connection.LocalEndPoint is IPEndPoint endpoint ? endpoint.Address.MapToIPv4().ToString() : null;

    private async Task CreateCharacterAsync(CharCommand2 request)
    {
        var token = RequireToken();
        var outcome = await _services.Characters.CreateCharacterAsync(token, request.Name, request.LParam, request.SParam);
        if (outcome.Ok)
        {
            await SendCharacterListAsync();
        }

        Send(request with { WParam = outcome.Ok ? 1 : 0, LParam = (int)outcome.Failure }, encrypt: true);
    }

    private async Task DeleteCharacterAsync(CharCommand2 request)
    {
        var outcome = await _services.Characters.DeleteCharacterAsync(RequireToken(), request.Name);
        if (outcome.Ok)
        {
            await SendCharacterListAsync();
        }

        // The original sends no reply; Etervale echoes the request so the client knows the result.
        Send(request with { WParam = outcome.Ok ? 1 : 0 }, encrypt: true);
    }

    private async Task EnterWorldAsync(CharCommand request)
    {
        var token = RequireToken();
        var outcome = await _services.Sessions.EnterWorldAsync(token, request.Name);
        var summary = _characters.FirstOrDefault(candidate => string.Equals(candidate.Name, request.Name, StringComparison.OrdinalIgnoreCase));
        if (!outcome.Ok || summary is null || _session?.Character is null)
        {
            // FAILCONNECT {WParam 0 = cannot connect, LParam = Etervale entry failure, EParam 3 = record load}.
            Send(new TransCommand(PristonOpcodes.FailConnect, (int)(outcome.Ok ? EnterWorldFailure.Rejected : outcome.Failure), 0, 0, 3), encrypt: true);
            return;
        }

        var character = WireMapping.DescribeOwner(_session.Character, summary, outcome.EntityId);
        var record = new RecordDataPacket(character, outcome.FieldId, PristonUnits.ToFixed(outcome.SpawnX), PristonUnits.ToFixed(outcome.SpawnZ)).Build();
        foreach (var chunk in RecordDataChunks.Split(record))
        {
            SendRaw(chunk, encrypt: true);
        }
    }

    private async Task JoinWorldAsync(PlayerInfoPacket request)
    {
        var session = _session;
        if (_player is not null || session?.Character is null || session.Loaded is null || request.ObjectSerial != session.EntityId
            || !session.TryBeginPlay())
        {
            throw new InvalidOperationException("PLAYERINFO before a successful GETRECORDDATA.");
        }

        var player = WorldPlayerFactory.Create(session);
        var outbox = new WorldOutbox();
        bool joined;
        try
        {
            joined = await _services.World.JoinAsync(session.FieldId, player, outbox.Writer, session);
        }
        catch (WorldBusyException)
        {
            joined = false;
        }

        if (!joined)
        {
            session.EndPlay();
            throw new InvalidOperationException("World join rejected.");
        }

        _player = player;
        _outbox = outbox;
        _encoder = new PresenceEncoder(session.EntityId, _services.WorldData);
        // CONNECTED {LParam = server milliseconds, WParam = object serial}.
        Send(new TransCommand(PristonOpcodes.Connected, Environment.TickCount, (int)session.EntityId), encrypt: true);
        _pump = PumpAsync(outbox, _encoder);
        JoinChat(session);
    }

    private async Task PumpAsync(WorldOutbox outbox, PresenceEncoder encoder)
    {
        try
        {
            // The outbox completes when the session leaves the world or closes; waiting without a token allocates nothing.
            while (await outbox.Reader.WaitToReadAsync())
            {
                while (outbox.Reader.TryRead(out var snapshot))
                {
                    try
                    {
                        encoder.Encode(snapshot, _countingSink);
                    }
                    finally
                    {
                        snapshot.ReleaseFrame();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidOperationException ex)
        {
            _services.Logger.LogWarning("Presence stream ended for {Remote}: {Message}", _connection.RemoteEndPoint, ex.Message);
            _connection.Close("presence overflow");
        }
        finally
        {
            encoder.Close();
        }
    }

    private void ReportMovement(TransPlayData report)
    {
        var session = _session;
        var player = _player;
        var outbox = _outbox;
        if (session is null || player is null || outbox is null || report.ObjectSerial != session.EntityId)
        {
            return;
        }

        var last = report.Buffers[^1];
        var position = new Vector3(PristonUnits.FromFixed(last.X), PristonUnits.FromFixed(last.Y), PristonUnits.FromFixed(last.Z));
        if (report.FieldCode >= 0 && report.FieldCode != session.FieldId)
        {
            Task<bool> transition;
            try
            {
                transition = _services.World.MoveToFieldAsync(session.FieldId, report.FieldCode, player, position, outbox.Writer);
            }
            catch (WorldBusyException)
            {
                Correct(force: true);
                return;
            }

            _ = transition.ContinueWith(task =>
            {
                if (!task.IsCompletedSuccessfully || !task.Result)
                {
                    Correct(force: true);
                }
            }, TaskScheduler.Default);
            return;
        }

        var moving = last.Action is MotionStateCode.Walk or MotionStateCode.Run;
        // A saturated queue or quota drops the report; the next one carries the newer position.
        _services.World.TryReportMovement(session.FieldId, session.EntityId, position, PristonUnits.FromAngle(last.AngleY), moving,
            last.Action == MotionStateCode.Run, last.TargetSerial, this);
    }

    // On the tick thread: a rejection is answered from the thread pool, where the correction may wait for the encoder.
    void IMovementVerdictSink.OnMovementVerdict(MovementVerdict verdict)
    {
        if (verdict == MovementVerdict.Rejected)
        {
            ThreadPool.UnsafeQueueUserWorkItem(static session => session.Correct(force: false), this, preferLocal: false);
        }
    }

    // WARPFIELD back to the authoritative field and position after a rejected report or field change.
    private void Correct(bool force)
    {
        var now = Environment.TickCount64;
        var encoder = _encoder;
        var session = _session;
        if (encoder?.SelfPosition is not { } position || session is null || !force && now - _lastCorrectionMs < CorrectionIntervalMs)
        {
            return;
        }

        _lastCorrectionMs = now;
        Send(new TransCommand(PristonOpcodes.WarpField, PristonUnits.ToFixed(position.Z), session.FieldId, PristonUnits.ToFixed(position.X), 1), encrypt: true);
    }

    private async Task AttackAsync(ReadOnlyMemory<byte> memory)
    {
        var session = _session;
        if (session is null || _player is null || _encoder is null)
        {
            return;
        }

        AttackDamagePacket attack;
        try
        {
            attack = AttackDamagePacket.Read(memory.Span, session.EntityId);
        }
        catch (InvalidDataException ex)
        {
            LogRejected(PristonOpcodes.AttackDamage, ex.Message);
            return;
        }

        if (attack.TargetSerial != 0 && _encoder.KindOf(attack.TargetSerial) == EntityKindView.Npc)
        {
            await InteractNpcAsync(session, attack.TargetSerial);
            return;
        }

        // AttackState bit 0 marks a held (automatic) attack; AttackCount carries the client's request sequence.
        var sustained = (attack.AttackState & 1) != 0;
        _ = _services.World.SetAttackTargetAsync(session.FieldId, session.EntityId, attack.TargetSerial, sustained, (uint)attack.AttackCount);
    }

    // SkillCast is answered with SkillCastResult on this connection; the start, impact and statuses follow on the
    // presence timeline. The server ignores the client's rank, cost and timing and validates the cast in the tick.
    private async Task CastSkillAsync(SkillCastPacket packet)
    {
        var session = _session;
        if (session is null || _player is null)
        {
            return;
        }

        var hasPoint = packet.X != 0 || packet.Y != 0 || packet.Z != 0;
        var point = new Vector3(PristonUnits.FromFixed(packet.X), PristonUnits.FromFixed(packet.Y), PristonUnits.FromFixed(packet.Z));
        var request = new SkillCastRequest(packet.Sequence, packet.SkillCode, packet.TargetSerial, point, hasPoint, (SkillCastCommand)packet.Flags);
        SkillCastOutcome outcome;
        try
        {
            outcome = await _services.World.CastSkillAsync(session.FieldId, session.EntityId, request);
        }
        catch (WorldBusyException)
        {
            outcome = SkillCastOutcome.Refused(request, SkillCastVerdict.Busy);
        }

        if (!outcome.Accepted)
        {
            LogRejected(EtervaleOpcodes.SkillCast, $"skill {packet.SkillCode:X8}: {outcome.Verdict}");
        }

        var result = new SkillCastResultPacket(outcome.Sequence, outcome.SkillCode, (SkillCastStatus)outcome.Verdict, (byte)Math.Clamp(outcome.Rank, 0, 255),
            (uint)Math.Max(0, outcome.CooldownMs), (uint)Math.Max(0, outcome.RemainingMs), outcome.Mana, outcome.Stamina, 0);
        SendLarge(result.Write, SkillCastResultPacket.Size, encrypt: true);
    }

    private async Task InteractNpcAsync(PlayerSession session, uint npc)
    {
        var interaction = await _services.World.InteractNpcAsync(session, npc);
        var services = interaction.Services.Select(offer => new NpcServiceEntry((byte)offer.Kind, offer.Available, offer.Value)).ToArray();
        var dialog = new NpcDialogPacket(npc, interaction.Ok, interaction.Name, interaction.Message, services, interaction.Dialog);
        SendLarge(dialog.Write, PristonFrameCodec.MaxFrameSize, encrypt: true);
    }

    private async Task RequestNpcServiceAsync(NpcServiceRequestPacket request)
    {
        var session = _session;
        var command = new NpcServiceCommand(request.OperationId, request.NpcSerial, (NpcServiceKind)request.Service, (NpcServiceAction)request.Action,
            request.ExpectedRevision, request.ExpectedStorageRevision, request.ItemCode, request.ItemId, request.Anchor, request.Count, request.Amount);
        NpcServiceReply reply;
        try
        {
            reply = session is null || _player is null
                ? new NpcServiceReply(NpcServiceOutcome.Failed(NpcServiceStatus.Rejected, "enter the world first"))
                : await _services.Npc.RequestAsync(RequireToken(), command);
        }
        catch (Exception ex) when (ex is InvalidSessionException or InvalidOperationException or System.Data.Common.DbException)
        {
            reply = new NpcServiceReply(NpcServiceOutcome.Failed(NpcServiceStatus.StorageFailed, ex.Message));
        }

        NpcWireMapping.SendState(request, reply, packet => SendLarge(packet, PristonFrameCodec.MaxFrameSize, encrypt: true));
        if (reply.Arrival is { } arrival)
        {
            // WARPFIELD {LParam = Z, WParam = field, SParam = X, EParam 1 = forced}.
            Send(new TransCommand(PristonOpcodes.WarpField, PristonUnits.ToFixed(arrival.Position.Z), arrival.FieldCode,
                PristonUnits.ToFixed(arrival.Position.X), 1), encrypt: true);
        }
    }

    private async Task RequestQuestAsync(QuestRequestPacket request)
    {
        var session = _session;
        QuestOutcome outcome;
        try
        {
            outcome = session is null || _player is null
                ? QuestOutcome.Failed(QuestStatus.Rejected, "enter the world first")
                : await _services.Quests.RequestAsync(RequireToken(), QuestWireMapping.ToCommand(request));
        }
        catch (Exception ex) when (ex is InvalidSessionException or InvalidOperationException or System.Data.Common.DbException)
        {
            outcome = QuestOutcome.Failed(QuestStatus.StorageFailed, ex.Message);
        }

        var result = QuestWireMapping.ToResult(request.OperationId, outcome);
        SendLarge(result.Write, result.Capacity, encrypt: true);
    }

    private void DescribeObject(DescribeEntityPacket request)
    {
        // A small request costs a description of up to a few hundred bytes, so requests beyond the bucket are dropped.
        if (!_playerInfoRate.TryTake(Environment.TickCount64))
        {
            LogRejected(EtervaleOpcodes.DescribeEntity, "description rate exceeded");
            return;
        }

        // Ids are 64-bit on the wire; the world hands out 32-bit ones, so a larger id is never in view.
        if (_encoder is { } encoder && request.Serial <= uint.MaxValue)
        {
            lock (_sendGate)
            {
                var length = encoder.WriteDescription((uint)request.Serial, _scratch);
                if (length > 0)
                {
                    SendSpan(_scratch.AsSpan(0, length), encrypt: false);
                }
            }
        }
    }

    private void AcknowledgeClientSave(ReadOnlySpan<byte> chunk)
    {
        // The server owns the character record; an original client's autosave is acknowledged and discarded.
        var reader = new PacketReader(chunk, RecordDataChunks.ChunkHeader);
        var count = reader.Int32();
        var total = reader.Int32();
        if (total == 0 || count == total - 1)
        {
            Send(new TransCommand(PristonOpcodes.RecordResult, 0, 1), encrypt: true);
        }
    }

    private async Task ApplyInventoryAsync(InventoryCommandPacket request)
    {
        var command = new InventoryCommand(request.OperationId, request.ExpectedRevision, (InventoryAction)request.Action, request.ItemId,
            request.Destination, request.Count, (EquipmentSlot)request.Slot);
        InventoryOutcome outcome;
        try
        {
            outcome = await _services.Sessions.ApplyInventoryAsync(RequireToken(), command);
        }
        catch (Exception ex) when (ex is InvalidSessionException or InvalidOperationException or System.Data.Common.DbException)
        {
            outcome = new InventoryOutcome(false, request.ExpectedRevision, ex.Message);
        }

        var result = new OperationResultPacket(EtervaleOpcodes.InventoryResult, request.OperationId, outcome.Ok, outcome.Revision, outcome.Message);
        SendLarge(result.Write, OperationResultPacket.Size, encrypt: true);
    }

    // The item use answers with ItemUseResult; a use that moved the player to another field also sends a forced WARPFIELD.
    private async Task UseItemAsync(ItemUseCommandPacket request)
    {
        var command = new ItemUseCommand(request.OperationId, request.ExpectedRevision, request.ItemId, request.Argument, request.Text);
        ItemUseReply reply;
        try
        {
            reply = _session is null || _player is null
                ? new ItemUseReply(ItemUseOutcome.Refused(request.ExpectedRevision, ItemUseStatus.Rejected))
                : await _services.ItemUse.UseAsync(RequireToken(), command);
        }
        catch (WorldBusyException)
        {
            reply = new ItemUseReply(ItemUseOutcome.Refused(request.ExpectedRevision, ItemUseStatus.Busy));
        }
        catch (Exception ex) when (ex is InvalidSessionException or InvalidOperationException or System.Data.Common.DbException)
        {
            reply = new ItemUseReply(ItemUseOutcome.Refused(request.ExpectedRevision, ItemUseStatus.StorageFailed));
        }

        var outcome = reply.Outcome;
        var result = new ItemUseResultPacket(request.OperationId, outcome.Ok, outcome.Revision, (ushort)outcome.Status, outcome.ItemCode, outcome.Message);
        SendLarge(result.Write, ItemUseResultPacket.MaximumSize, encrypt: true);
        if (reply.Arrival is { } arrival)
        {
            Send(new TransCommand(PristonOpcodes.WarpField, PristonUnits.ToFixed(arrival.Position.Z), arrival.FieldCode,
                PristonUnits.ToFixed(arrival.Position.X), 1), encrypt: true);
        }
    }

    // A dead player's restart answers with RestartResult; a restart that moved the player sends a forced WARPFIELD.
    private async Task RestartAsync(RestartRequestPacket request)
    {
        var command = new RestartCommand(request.OperationId, (RestartChoice)request.Choice);
        RestartReply reply;
        try
        {
            reply = _session is null || _player is null
                ? new RestartReply(new RestartOutcome(RestartStatus.Rejected, command.Choice))
                : await _services.Restart.RestartAsync(RequireToken(), command);
        }
        catch (WorldBusyException)
        {
            reply = new RestartReply(new RestartOutcome(RestartStatus.Busy, command.Choice));
        }
        catch (Exception ex) when (ex is InvalidSessionException or InvalidOperationException or System.Data.Common.DbException)
        {
            reply = new RestartReply(new RestartOutcome(RestartStatus.StorageFailed, command.Choice));
        }

        var outcome = reply.Outcome;
        var result = new RestartResultPacket(request.OperationId, (byte)outcome.Status, (byte)outcome.Choice, outcome.ScrollUsed, outcome.ExperienceLost,
            outcome.GoldLost);
        SendLarge(result.Write, RestartResultPacket.Size, encrypt: true);
        if (outcome.Ok)
        {
            Send(new TransCommand(PristonOpcodes.WarpField, PristonUnits.ToFixed(reply.Position.Z), reply.FieldId,
                PristonUnits.ToFixed(reply.Position.X), 1), encrypt: true);
        }
    }

    private async Task ApplyProgressionAsync(ProgressionCommandPacket request)
    {
        var command = new ProgressionCommand(request.OperationId, request.ExpectedRevision, (ProgressionAction)request.Action,
            (CharacterAttribute)request.Attribute, request.Amount, request.SkillCode, request.NpcSerial);
        ProgressionOutcome outcome;
        try
        {
            outcome = await _services.Sessions.ApplyProgressionAsync(RequireToken(), command);
        }
        catch (Exception ex) when (ex is InvalidSessionException or InvalidOperationException or System.Data.Common.DbException)
        {
            outcome = new ProgressionOutcome(false, request.ExpectedRevision, ex.Message);
        }

        var result = new OperationResultPacket(EtervaleOpcodes.ProgressionResult, request.OperationId, outcome.Ok, outcome.Revision, outcome.Message);
        SendLarge(result.Write, OperationResultPacket.Size, encrypt: true);
    }

    private async Task ClosePlayAsync()
    {
        var outbox = _outbox;
        var player = _player;
        var session = _session;
        var pump = _pump;
        if (outbox is null || player is null || session is null)
        {
            return;
        }

        _outbox = null;
        _player = null;
        _pump = null;
        LeaveChat(session);
        outbox.Writer.TryComplete();
        await _services.World.LeaveAsync(session.FieldId, session.EntityId, outbox.Writer);
        session.EndPlay();
        session.SpawnPosition = new Vector3(player.Position.X, 0f, player.Position.Z);
        try
        {
            await _services.Sessions.SaveCharacterAsync(_token!, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _services.Logger.LogError(ex, "Could not save character on leave.");
        }

        if (pump is not null)
        {
            await pump;
        }
    }

    private string RequireToken() => _token ?? throw new InvalidOperationException("Log in first.");

    private void LogRejected(uint code, string reason)
    {
        if (_services.RejectedPackets.TryEnter(Environment.TickCount64, out var suppressed))
        {
            _services.Logger.LogWarning("Rejected {Code:X8} from {Remote}: {Reason} ({Suppressed} similar rejections not logged)",
                code, _connection.RemoteEndPoint, reason, suppressed);
        }
    }

    private void Send(TransCommand command, bool encrypt)
    {
        lock (_sendGate)
        {
            SendSpan(_scratch.AsSpan(0, command.Write(_scratch)), encrypt);
        }
    }

    private void Send(CharCommand2 command, bool encrypt)
    {
        lock (_sendGate)
        {
            SendSpan(_scratch.AsSpan(0, command.Write(_scratch)), encrypt);
        }
    }

    internal delegate int PacketWrite(Span<byte> destination);

    private void SendLarge(PacketWrite write, int capacity, bool encrypt)
    {
        lock (_sendGate)
        {
            if (capacity > _scratch.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            SendSpan(_scratch.AsSpan(0, write(_scratch)), encrypt);
        }
    }

    private void SendRaw(byte[] packet, bool encrypt) => SendSpan(packet, encrypt);

    private void SendSpan(ReadOnlySpan<byte> packet, bool encrypt)
    {
        Interlocked.Increment(ref _packetsSent);
        if (encrypt)
        {
            _connection.SendEncrypted(packet);
        }
        else
        {
            _connection.Send(packet);
        }
    }
}
