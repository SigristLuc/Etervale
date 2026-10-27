using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Characters;

namespace PristonTale.Application.Services;

// Why a durable operation did not run, or did not reach its commit.
public enum DurableFailure
{
    NoSession,
    Busy,
    NotPlaying,
    StorageFailed,
    // Storage holds a newer copy of something the operation read (the account warehouse).
    Conflict
}

// The durable path of every character change. The session's save gate admits one change per character at a time (saves
// share it); under it the tick prepares, storage writes the prepared snapshot, then the tick commits.
public sealed class DurableOperations(ICharacterRepository characters)
{
    public Task<DurableLease> AdmitAsync(ISessionRegistry sessions, string sessionToken, CancellationToken ct) =>
        sessions.TryGet(sessionToken, out var session)
            ? AdmitAsync(session, TimeSpan.Zero, ct)
            : Task.FromResult(DurableLease.Refused(DurableFailure.NoSession));

    public async Task<DurableLease> AdmitAsync(PlayerSession session, TimeSpan wait, CancellationToken ct) =>
        await session.SaveGate.WaitAsync(wait, ct) ? new DurableLease(characters, session) : DurableLease.Refused(DurableFailure.Busy);
}

// One admission to a session's save gate, released on Dispose. A refused lease runs nothing and answers its failure.
public sealed class DurableLease : IDisposable
{
    private readonly ICharacterRepository? _characters;
    private PlayerSession? _session;

    internal DurableLease(ICharacterRepository characters, PlayerSession session)
    {
        _characters = characters;
        _session = session;
    }

    private DurableLease(DurableFailure failure) => Failure = failure;

    public DurableFailure? Failure { get; }

    public PlayerSession Session => _session ?? throw new InvalidOperationException("The durable lease was refused or released.");

    internal static DurableLease Refused(DurableFailure failure) => new(failure);

    // Prepares on the tick; a preparation without a snapshot answers at once. The write runs to its end despite
    // cancellation, so retrying the operation id recovers the outcome. Store replaces the character write (null when
    // stored); Abort runs whenever a prepared snapshot was not stored, a storage exception included.
    public async Task<TReply> RunAsync<TPrepared, TReply>(
        Func<PlayerSession, Task<TPrepared?>> prepare,
        Func<PlayerSession, TPrepared, Task> commit,
        Func<TPrepared, TReply> reply,
        Func<DurableFailure, TReply> failed,
        Func<PlayerSession, TPrepared, Task<DurableFailure?>>? store = null,
        Func<PlayerSession, TPrepared, Task>? abort = null)
        where TPrepared : PreparedOperation
    {
        if (Failure is { } refused)
        {
            return failed(refused);
        }

        var session = Session;
        var prepared = await prepare(session);
        if (prepared is null)
        {
            return failed(DurableFailure.NotPlaying);
        }

        if (prepared.Snapshot is not { } snapshot)
        {
            return reply(prepared);
        }

        DurableFailure? failure = DurableFailure.StorageFailed;
        try
        {
            failure = store is not null
                ? await store(session, prepared)
                : await _characters!.UpdateAsync(snapshot.Encode(), CancellationToken.None) ? null : DurableFailure.StorageFailed;
        }
        finally
        {
            if (failure is not null && abort is not null)
            {
                await abort(session, prepared);
            }
        }

        if (failure is { } notStored)
        {
            return failed(notStored);
        }

        await commit(session, prepared);
        return reply(prepared);
    }

    public void Dispose() => Interlocked.Exchange(ref _session, null)?.SaveGate.Release();
}
