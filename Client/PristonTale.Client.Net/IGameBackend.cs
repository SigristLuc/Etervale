using PristonTale.Application.Services;
using PristonTale.Client.Net.WorldCatalog;

namespace PristonTale.Client.Net;

public interface IGameBackend : IAsyncDisposable
{
    string? SessionToken { get; }

    bool SupportsLivePresence { get; }

    Task<LoginOutcome> LoginAsync(string account, string password, int clientVersion, CancellationToken ct = default);

    Task<IReadOnlyList<CharacterSummary>> ListCharactersAsync(CancellationToken ct = default);

    Task<CreateCharacterOutcome> CreateCharacterAsync(string name, int classId, int faceIndex, CancellationToken ct = default);

    Task<DeleteCharacterOutcome> DeleteCharacterAsync(string name, CancellationToken ct = default);

    Task<EnterWorldOutcome> EnterWorldAsync(string characterName, CancellationToken ct = default);

    IPlayChannel OpenPlayChannel(CancellationToken ct = default);

    Task<WorldRenderCatalog> GetWorldCatalogAsync(CancellationToken ct = default);

    Task<bool> SaveCharacterAsync(CancellationToken ct = default);
}
