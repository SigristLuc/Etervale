using PristonTale.Persistence.Abstractions.Characters;

namespace PristonTale.Tests;

internal sealed class MemoryCharacters(CharacterRecord initial) : ICharacterRepository
{
    public CharacterRecord Record { get; private set; } = initial;
    public Func<CharacterRecord, Task<bool>>? BeforeUpdate { get; set; }
    public int UpdateCount { get; private set; }

    public Task<CharacterRecord?> FindByNameForAccountAsync(long accountId, string name, CancellationToken ct = default)
    {
        return Task.FromResult<CharacterRecord?>(Record.AccountId == accountId && Record.Name == name ? Record : null);
    }

    public async Task<bool> UpdateAsync(CharacterRecord record, CancellationToken ct = default)
    {
        UpdateCount++;
        if (BeforeUpdate is not null && !await BeforeUpdate(record))
        {
            return false;
        }

        Record = record;
        return true;
    }

    // One character's rows only: a trade between two characters needs MemoryCharacterBook.
    public Task<bool> UpdateAllAsync(IReadOnlyList<CharacterRecord> records, CancellationToken ct = default) =>
        records.Count == 1 ? UpdateAsync(records[0], ct) : throw new NotSupportedException();

    public Task<IReadOnlyList<CharacterRecord>> ListByAccountAsync(long accountId, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<CharacterRecord>>([Record]);
    }

    public Task<bool> NameExistsAsync(string name, CancellationToken ct = default) => Task.FromResult(Record.Name == name);
    public Task<IReadOnlyList<short>> UsedSlotsAsync(long accountId, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<short>>([Record.Slot]);
    }

    public Task<CharacterRecord> CreateAsync(CharacterRecord record, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> DeleteByNameAsync(long accountId, string name, CancellationToken ct = default) => throw new NotSupportedException();
}
