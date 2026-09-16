using PristonTale.Application.Services;
using PristonTale.Client.Net.Online;
using PristonTale.Client.Net.WorldCatalog;

namespace PristonTale.Client.Net;

public sealed class OnlineBackend : IGameBackend
{
    private readonly GameServerConnection _connection;

    public OnlineBackend(GameServerConnection connection)
    {
        _connection = connection;
    }

    public string? SessionToken => _connection.SessionToken;

    public bool SupportsLivePresence => true;

    public async Task<LoginOutcome> LoginAsync(string account, string password, int clientVersion, CancellationToken ct = default)
    {
        var reply = await _connection.LoginAsync(account, password, clientVersion, ct);
        return reply.Ok
            ? LoginOutcome.Success(reply.SessionToken, reply.Message)
            : LoginOutcome.Denied(reply.Message);
    }

    public async Task<IReadOnlyList<CharacterSummary>> ListCharactersAsync(CancellationToken ct = default)
    {
        var reply = await _connection.ListCharactersAsync(ct);
        var characters = new List<CharacterSummary>(reply.Characters.Count);
        foreach (var c in reply.Characters)
        {
            characters.Add(ToSummary(c));
        }

        return characters;
    }

    public async Task<CreateCharacterOutcome> CreateCharacterAsync(string name, int classId, int faceIndex, CancellationToken ct = default)
    {
        var reply = await _connection.CreateCharacterAsync(name, classId, faceIndex, ct);
        if (!reply.Ok)
        {
            return CreateCharacterOutcome.Failed(reply.Message);
        }

        var c = reply.Character;
        return CreateCharacterOutcome.Success(ToSummary(c));
    }

    public async Task<DeleteCharacterOutcome> DeleteCharacterAsync(string name, CancellationToken ct = default)
    {
        var reply = await _connection.DeleteCharacterAsync(name, ct);
        return reply.Ok
            ? DeleteCharacterOutcome.Success()
            : DeleteCharacterOutcome.Denied(reply.Message);
    }

    public async Task<EnterWorldOutcome> EnterWorldAsync(string characterName, CancellationToken ct = default)
    {
        var reply = await _connection.EnterWorldAsync(characterName, ct);
        return reply.Ok
            ? EnterWorldOutcome.Success(reply.FieldId, reply.EntityId, reply.Spawn.X, reply.Spawn.Z, reply.Message)
            : EnterWorldOutcome.Denied(reply.Message);
    }

    public IPlayChannel OpenPlayChannel(CancellationToken ct = default) =>
        new OnlinePlayChannel(_connection.OpenPlayStream(ct), _connection.ApplyInventoryAsync,
            _connection.ApplyProgressionAsync, _connection.InteractNpcAsync);

    public async Task<WorldRenderCatalog> GetWorldCatalogAsync(CancellationToken ct = default)
    {
        var reply = await _connection.GetWorldCatalogAsync(ct);
        return WorldCatalogMapping.ToNeutral(reply);
    }

    public Task<bool> SaveCharacterAsync(CancellationToken ct = default) => Task.FromResult(false);

    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    private static CharacterSummary ToSummary(Protocol.Character.CharacterSummary character)
    {
        return new CharacterSummary(
            character.Name,
            character.ClassId,
            character.Level,
            character.FaceIndex,
            character.Tier,
            character.HairIndex,
            character.ArmorKey,
            character.WeaponKey,
            character.OffHandKey,
            character.InVillage);
    }
}
