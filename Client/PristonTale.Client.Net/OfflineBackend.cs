using Microsoft.Extensions.DependencyInjection;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Client.Net.Offline;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Sqlite;
using PristonTale.Simulation.Characters;

namespace PristonTale.Client.Net;

public sealed class OfflineBackend : IGameBackend
{
    private readonly ServiceProvider _provider;
    private readonly AuthAppService _auth;
    private readonly CharacterAppService _characters;
    private readonly SessionAppService _session;
    private readonly ISessionRegistry _sessions;

    private readonly WorldServer _world;

    private string? _sessionToken;
    private OfflinePlayChannel? _playChannel;
    private OfflineWorldTick? _tick;
    private readonly Action<string> _logError;
    private readonly CancellationTokenSource _checkpointCancellation = new();
    private Task _checkpoints = Task.CompletedTask;
    private Task? _disposeTask;
    public Exception? LastSaveError { get; private set; }

    public WorldData World { get; }

    public OfflineBackend(string databasePath, Action<string>? logError = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _logError = logError ?? Console.Error.WriteLine;
        var services = new ServiceCollection();
        services.AddPristonSqlite(databasePath);
        services.AddSingleton<CharacterClassRegistry>();
        services.AddSingleton<ISessionRegistry, SessionRegistry>();
        services.AddSingleton<AuthAppService>();
        services.AddSingleton<CharacterAppService>();
        // A factory prevents DI from selecting the collection constructor with an empty field set.
        services.AddSingleton(_ => new PristonTale.Simulation.World.WorldCatalog());
        services.AddSingleton<WorldDataHolder>();
        services.AddSingleton<WorldServer>();
        services.AddSingleton<SessionAppService>();
        services.AddSingleton<WorldDataAppService>();
        _provider = services.BuildServiceProvider();
        _provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync().GetAwaiter().GetResult();
        _auth = _provider.GetRequiredService<AuthAppService>();
        _characters = _provider.GetRequiredService<CharacterAppService>();
        _session = _provider.GetRequiredService<SessionAppService>();
        _sessions = _provider.GetRequiredService<ISessionRegistry>();
        World = _provider.GetRequiredService<WorldDataAppService>().BuildAsync().GetAwaiter().GetResult();
        _provider.GetRequiredService<WorldDataHolder>().Current = World;
        _world = _provider.GetRequiredService<WorldServer>();
    }

    public string? SessionToken => _sessionToken;

    public bool SupportsLivePresence => true;

    public async Task<LoginOutcome> LoginAsync(string account, string password, int clientVersion, CancellationToken ct = default)
    {
        var outcome = await _auth.LoginLocalProfileAsync(account, ct);
        if (outcome.Ok)
            _sessionToken = outcome.SessionToken;
        return outcome;
    }

    public Task<IReadOnlyList<CharacterSummary>> ListCharactersAsync(CancellationToken ct = default) =>
        _characters.ListCharactersAsync(RequireSession(), ct);

    public Task<CreateCharacterOutcome> CreateCharacterAsync(string name, int classId, int faceIndex, CancellationToken ct = default) =>
        _characters.CreateCharacterAsync(RequireSession(), name, classId, faceIndex, ct);

    public Task<DeleteCharacterOutcome> DeleteCharacterAsync(string name, CancellationToken ct = default) =>
        _characters.DeleteCharacterAsync(RequireSession(), name, ct);

    public Task<EnterWorldOutcome> EnterWorldAsync(string characterName, CancellationToken ct = default) =>
        _session.EnterWorldAsync(RequireSession(), characterName, ct);

    public IPlayChannel OpenPlayChannel(CancellationToken ct = default)
    {
        var token = RequireSession();
        if (!_sessions.TryGet(token, out var session))
        {
            throw new InvalidOperationException("offline play channel: no live session (call LoginAsync first)");
        }

        if (_playChannel is not null)
        {
            throw new InvalidOperationException("offline play channel already opened");
        }

        _playChannel = new OfflinePlayChannel(_world, session, ownsTick: false, sessionApp: _session);
        _tick = new OfflineWorldTick(_world);
        _checkpoints = RunCheckpointsAsync(_checkpointCancellation.Token);
        return _playChannel;
    }

    public Task<WorldRenderCatalog> GetWorldCatalogAsync(CancellationToken ct = default) =>
        Task.FromResult(WorldCatalogProjection.ToRenderCatalog(World.Monsters, World.Npcs));

    public Task<bool> SaveCharacterAsync(CancellationToken ct = default) =>
        _sessionToken is null ? Task.FromResult(false) : _session.SaveCharacterAsync(_sessionToken, ct);

    private async Task RunCheckpointsAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await SaveObservedAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task SaveObservedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_sessionToken is not null
                && _sessions.TryGet(_sessionToken, out var session)
                && session.Loaded is not null
                && !await SaveCharacterAsync(cancellationToken))
            {
                throw new IOException("offline save did not update the character record");
            }

            LastSaveError = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LastSaveError = ex;
            _logError($"[OfflineSave] save failed; next checkpoint/shutdown will retry: {ex}");
        }
    }

    public ValueTask DisposeAsync()
    {
        _disposeTask ??= DisposeCoreAsync();
        return new ValueTask(_disposeTask);
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            await _checkpointCancellation.CancelAsync();
            await _checkpoints;
            if (_playChannel is not null)
            {
                await _playChannel.DisposeAsync();
            }

            await SaveObservedAsync();
            if (LastSaveError is not null)
            {
                throw new IOException("Offline progress could not be saved.", LastSaveError);
            }
        }
        finally
        {
            if (_tick is not null)
            {
                await _tick.DisposeAsync();
            }

            if (_sessionToken is not null)
            {
                _sessions.Remove(_sessionToken);
            }

            _checkpointCancellation.Dispose();
            await _provider.DisposeAsync();
        }
    }

    private string RequireSession() =>
        _sessionToken ?? throw new InvalidOperationException("not logged in: call LoginAsync first");
}
