using System.Numerics;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;

namespace PristonTale.Application.Sessions;

public sealed class PlayerSession
{
    // 0 = authenticated, 1 = loading, 2 = ready, 3 = playing, 4 = closing.
    private int _state;

    internal SemaphoreSlim SaveGate { get; } = new(1, 1);
    internal bool TryBeginEntry() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;
    internal bool CompleteEntry(bool success) => Interlocked.CompareExchange(ref _state, success ? 2 : 0, 1) == 1;
    internal void MarkClosed() => Interlocked.Exchange(ref _state, 4);
    public bool IsPlaying => Volatile.Read(ref _state) == 3;
    public bool TryBeginPlay() => Interlocked.CompareExchange(ref _state, 3, 2) == 2;
    public void EndPlay() => Interlocked.CompareExchange(ref _state, 4, 3);

    public required string SessionToken { get; init; }
    public required long AccountId { get; init; }
    public required string UserId { get; init; }
    public required uint EntityId { get; init; }

    public string? CharacterName { get; set; }
    public int FieldId { get; set; }
    public Vector3 SpawnPosition { get; set; }

    public PlayerCharacter? Character { get; set; }
    public bool InVillage { get; set; }

    public CharacterRecord? Loaded { get; set; }
}
