using Grpc.Net.Client;
using Google.Protobuf;
using PristonTale.Simulation.Items;
using PristonTale.Protocol;
using PristonTale.Protocol.Auth;
using PristonTale.Protocol.Character;
using PristonTale.Protocol.Session;
using PristonTale.Protocol.World;

namespace PristonTale.Client.Net;

public sealed class GameServerConnection : IAsyncDisposable
{
    private readonly GrpcChannel _channel;
    private readonly AuthService.AuthServiceClient _auth;
    private readonly CharacterService.CharacterServiceClient _characters;
    private readonly GameSessionService.GameSessionServiceClient _session;

    public string? SessionToken { get; private set; }

    public GameServerConnection(string address)
    {
        _channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler
            {
                EnableMultipleHttp2Connections = true,
            }
        });
        _auth = new AuthService.AuthServiceClient(_channel);
        _characters = new CharacterService.CharacterServiceClient(_channel);
        _session = new GameSessionService.GameSessionServiceClient(_channel);
    }

    public async Task<LoginReply> LoginAsync(string account, string password, int clientVersion, CancellationToken ct = default)
    {
        var reply = await _auth.LoginAsync(
            new LoginRequest { Account = account, Password = password, ClientVersion = clientVersion },
            cancellationToken: ct);
        if (reply.Ok)
            SessionToken = reply.SessionToken;
        return reply;
    }

    public Task<ListCharactersReply> ListCharactersAsync(CancellationToken ct = default) =>
        _characters.ListCharactersAsync(new ListCharactersRequest { SessionToken = RequireToken() }, cancellationToken: ct).ResponseAsync;

    public Task<CreateCharacterReply> CreateCharacterAsync(string name, int classId, int faceIndex, CancellationToken ct = default) =>
        _characters.CreateCharacterAsync(
            new CreateCharacterRequest { SessionToken = RequireToken(), Name = name, ClassId = classId, FaceIndex = faceIndex },
            cancellationToken: ct).ResponseAsync;

    public Task<DeleteCharacterReply> DeleteCharacterAsync(string name, CancellationToken ct = default) =>
        _characters.DeleteCharacterAsync(
            new DeleteCharacterRequest { SessionToken = RequireToken(), Name = name },
            cancellationToken: ct).ResponseAsync;

    public Task<EnterWorldReply> EnterWorldAsync(string characterName, CancellationToken ct = default) =>
        _session.EnterWorldAsync(
            new EnterWorldRequest { SessionToken = RequireToken(), CharacterName = characterName },
            cancellationToken: ct).ResponseAsync;


    public Task<WorldCatalogReply> GetWorldCatalogAsync(CancellationToken ct = default) =>
        _session.GetWorldCatalogAsync(
            new GetWorldCatalogRequest { SessionToken = RequireToken() },
            cancellationToken: ct).ResponseAsync;

    public Grpc.Core.AsyncDuplexStreamingCall<ClientInput, WorldUpdate> OpenPlayStream(CancellationToken ct = default)
    {
        var metadata = new Grpc.Core.Metadata { { "session-token", RequireToken() } };
        return _session.Play(new Grpc.Core.CallOptions(headers: metadata, cancellationToken: ct));
    }

    private string RequireToken() =>
        SessionToken ?? throw new InvalidOperationException("Not logged in: call LoginAsync first.");

    internal async Task<InventoryOutcome> ApplyInventoryAsync(InventoryCommand command, CancellationToken ct)
    {
        var request = new InventoryRequest
        {
            SessionToken = RequireToken(),
            OperationId = ByteString.CopyFrom(command.OperationId.ToByteArray()),
            ExpectedRevision = command.ExpectedRevision,
            Action = (int)command.Action,
            ItemId = ByteString.CopyFrom(command.ItemId.ToByteArray()),
            Destination = command.Destination,
            Count = command.Count,
            Slot = (int)command.Slot
        };
        var reply = await _session.ApplyInventoryAsync(request, cancellationToken: ct);
        return new InventoryOutcome(reply.Ok, reply.Revision, reply.Message);
    }

    internal async Task<PristonTale.Simulation.Progression.ProgressionOutcome> ApplyProgressionAsync(
        PristonTale.Simulation.Progression.ProgressionCommand command, CancellationToken ct)
    {
        var reply = await _session.ApplyProgressionAsync(new ProgressionRequest
        {
            SessionToken = RequireToken(),
            OperationId = ByteString.CopyFrom(command.OperationId.ToByteArray()),
            ExpectedRevision = command.ExpectedRevision,
            Action = (int)command.Action,
            Attribute = (int)command.Attribute,
            Amount = command.Amount,
            SkillCode = command.SkillCode,
            NpcEntityId = command.NpcEntityId
        }, cancellationToken: ct);
        return new PristonTale.Simulation.Progression.ProgressionOutcome(reply.Ok, reply.Revision, reply.Message);
    }

    internal async Task<PristonTale.Simulation.World.Npcs.NpcInteraction> InteractNpcAsync(uint entityId, CancellationToken ct)
    {
        var reply = await _session.InteractNpcAsync(new NpcInteractionRequest
        {
            SessionToken = RequireToken(),
            EntityId = entityId
        }, cancellationToken: ct);
        return new PristonTale.Simulation.World.Npcs.NpcInteraction(reply.Ok, reply.EntityId, reply.Name,
            (PristonTale.Simulation.World.Npcs.NpcServices)reply.Services, reply.Dialog.ToArray(), reply.Message);
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.ShutdownAsync();
        _channel.Dispose();
    }
}
