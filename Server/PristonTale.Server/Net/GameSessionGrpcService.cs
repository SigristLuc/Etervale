using System.Numerics;
using System.Threading.Channels;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Protocol;
using PristonTale.Protocol.Common;
using PristonTale.Protocol.Session;
using PristonTale.Protocol.World;
using PristonTale.Server.Net.Mapping;
using PristonTale.Application.World;

namespace PristonTale.Server.Net;

public sealed class GameSessionGrpcService : GameSessionService.GameSessionServiceBase
{
    public override async Task<InventoryReply> ApplyInventory(InventoryRequest request, ServerCallContext context)
    {
        if (request.OperationId.Length != 16 || request.ItemId.Length != 16)
        {
            return new InventoryReply { Ok = false, Message = "invalid operation identity" };
        }

        var command = new PristonTale.Simulation.Items.InventoryCommand(new Guid(request.OperationId.Span), request.ExpectedRevision,
            (PristonTale.Simulation.Items.InventoryAction)request.Action, new Guid(request.ItemId.Span), request.Destination,
            request.Count, (PristonTale.Simulation.Characters.EquipmentSlot)request.Slot);
        var outcome = await _session.ApplyInventoryAsync(request.SessionToken, command, context.CancellationToken);
        return new InventoryReply { Ok = outcome.Ok, Revision = outcome.Revision, Message = outcome.Message };
    }

    public override async Task<ProgressionReply> ApplyProgression(ProgressionRequest request, ServerCallContext context)
    {
        if (request.OperationId.Length != 16)
        {
            return new ProgressionReply { Message = "invalid operation identity" };
        }

        var command = new PristonTale.Simulation.Progression.ProgressionCommand(new Guid(request.OperationId.Span), request.ExpectedRevision,
            (PristonTale.Simulation.Progression.ProgressionAction)request.Action,
            (PristonTale.Simulation.Characters.CharacterAttribute)request.Attribute, request.Amount, request.SkillCode, request.NpcEntityId);
        var outcome = await _session.ApplyProgressionAsync(request.SessionToken, command, context.CancellationToken);
        return new ProgressionReply { Ok = outcome.Ok, Revision = outcome.Revision, Message = outcome.Message };
    }

    public override async Task<NpcInteractionReply> InteractNpc(NpcInteractionRequest request, ServerCallContext context)
    {
        if (!_sessions.TryGet(request.SessionToken, out var session))
        {
            return new NpcInteractionReply { Message = "invalid session" };
        }

        var outcome = await _world.InteractNpcAsync(session, request.EntityId);
        var reply = new NpcInteractionReply
        {
            Ok = outcome.Ok,
            EntityId = outcome.EntityId,
            Name = outcome.Name,
            Services = (uint)outcome.Services,
            Message = outcome.Message
        };
        reply.Dialog.AddRange(outcome.Dialog);
        return reply;
    }

    private const string SessionTokenHeader = "session-token";

    private readonly ISessionRegistry _sessions;
    private readonly SessionAppService _session;
    private readonly WorldServer _world;
    private readonly WorldDataHolder _worldData;
    private readonly ILogger<GameSessionGrpcService> _logger;

    public GameSessionGrpcService(
        ISessionRegistry sessions, SessionAppService session, WorldServer world,
        WorldDataHolder worldData, ILogger<GameSessionGrpcService> logger)
    {
        _sessions = sessions;
        _session = session;
        _world = world;
        _worldData = worldData;
        _logger = logger;
    }

    public override async Task<EnterWorldReply> EnterWorld(EnterWorldRequest request, ServerCallContext context)
    {
        var outcome = await _session.EnterWorldAsync(
            request.SessionToken, request.CharacterName, context.CancellationToken);

        if (!outcome.Ok)
            return new EnterWorldReply { Ok = false, Message = outcome.Message };

        _logger.LogInformation(
            "EnterWorld '{Character}' field {Field} entity {Entity} spawn ({X},{Z})",
            request.CharacterName, outcome.FieldId, outcome.EntityId, outcome.SpawnX, outcome.SpawnZ);

        return new EnterWorldReply
        {
            Ok = true,
            FieldId = outcome.FieldId,
            EntityId = outcome.EntityId,
            Spawn = new Vec3 { X = outcome.SpawnX, Z = outcome.SpawnZ },
            Message = outcome.Message,
        };
    }

    public override Task<WorldCatalogReply> GetWorldCatalog(
        GetWorldCatalogRequest request, ServerCallContext context)
    {
        if (string.IsNullOrEmpty(request.SessionToken) || !_sessions.TryGet(request.SessionToken, out _))
            throw new RpcException(new Status(StatusCode.Unauthenticated, "missing or invalid session token"));

        var reply = ProtocolMapping.ToCatalogProto(_worldData.Current.Monsters, _worldData.Current.Npcs);
        return Task.FromResult(reply);
    }

    public override async Task Play(
        IAsyncStreamReader<ClientInput> requestStream,
        IServerStreamWriter<WorldUpdate> responseStream,
        ServerCallContext context)
    {
        // Bidirectional stream authentication travels in call metadata.
        var token = context.RequestHeaders.GetValue(SessionTokenHeader);
        if (token == null || !_sessions.TryGet(token, out var session))
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "missing or invalid session token"));
        }

        if (session.Character == null || session.Loaded == null)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "call EnterWorld before Play"));
        }

        var player = WorldPlayerFactory.Create(session);
        // Full-snapshot outboxes keep the tick thread independent of slow clients.
        var outbound = new WorldOutbox();
        if (!session.TryBeginPlay())
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "session already playing or closing"));
        }

        if (!await _world.JoinAsync(session.FieldId, player, outbound.Writer, session))
        {
            session.EndPlay();
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "world join rejected"));
        }

        var presence = new SubscriberPresence(session.EntityId);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        var sendTask = PumpAsync(outbound.Reader, responseStream, presence, lifetime);
        try
        {
            await foreach (var input in requestStream.ReadAllAsync(lifetime.Token))
            {
                if (input.KindCase == ClientInput.KindOneofCase.Move)
                {
                    await _world.SetMoveTargetAsync(session.FieldId, session.EntityId, ProtocolMapping.ToNumeric(input.Move.Target), input.Move.Run);
                }
                else if (input.KindCase == ClientInput.KindOneofCase.Attack)
                {
                    await _world.SetAttackTargetAsync(session.FieldId, session.EntityId, input.Attack.TargetEntityId, input.Attack.Sustained, input.Attack.Sequence);
                }
                else if (input.KindCase == ClientInput.KindOneofCase.EnterField)
                {
                    // The tick mailbox serializes the field swap with world mutations.
                    var newFieldId = input.EnterField.FieldCode;
                    var position = ProtocolMapping.ToNumeric(input.EnterField.Position);
                    await _world.MoveToFieldAsync(session.FieldId, newFieldId, player, position, outbound.Writer);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            await lifetime.CancelAsync();
            outbound.Writer.TryComplete();
            await _world.LeaveAsync(session.FieldId, session.EntityId, outbound.Writer);
            session.EndPlay();
            session.SpawnPosition = new Vector3(player.Position.X, 0f, player.Position.Z);
            try
            {
                if (!await _session.SaveCharacterAsync(token, CancellationToken.None))
                {
                    _logger.LogError("logout save did not update character (entity {EntityId})", session.EntityId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "failed to save character on logout (entity {EntityId})", session.EntityId);
            }

            _sessions.Remove(token);
            try
            {
                await sendTask;
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "presence stream failed (entity {EntityId})", session.EntityId);
            }
        }
    }

    private static async Task PumpAsync(
        ChannelReader<WorldSnapshot> reader,
        IServerStreamWriter<WorldUpdate> responseStream,
        SubscriberPresence presence,
        CancellationTokenSource lifetime)
    {
        var cancellationToken = lifetime.Token;
        PristonTale.Simulation.Characters.OwnerState lastOwner = null;
        try
        {
            await foreach (var snapshot in reader.ReadAllAsync(cancellationToken))
            {
                var diff = presence.Diff(snapshot);
                var update = ProtocolMapping.ToProto(snapshot.ServerTick, diff, snapshot.FieldId);
                if (snapshot.Owner is not null && snapshot.Owner != lastOwner)
                {
                    update.Owner = OwnerStateMapping.ToProto(snapshot.Owner);
                    lastOwner = snapshot.Owner;
                }

                await responseStream.WriteAsync(update, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            // A failed response writer must also release a stalled request reader and its session.
            await lifetime.CancelAsync();
        }
    }
}
