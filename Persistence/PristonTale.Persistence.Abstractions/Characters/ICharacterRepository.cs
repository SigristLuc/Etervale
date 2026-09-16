namespace PristonTale.Persistence.Abstractions.Characters;

public interface ICharacterRepository
{
    Task<IReadOnlyList<CharacterRecord>> ListByAccountAsync(long accountId, CancellationToken ct = default);
    Task<CharacterRecord?> FindByNameForAccountAsync(long accountId, string name, CancellationToken ct = default);
    Task<bool> NameExistsAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<short>> UsedSlotsAsync(long accountId, CancellationToken ct = default);
    Task<CharacterRecord> CreateAsync(CharacterRecord record, CancellationToken ct = default);

    Task<bool> DeleteByNameAsync(long accountId, string name, CancellationToken ct = default);

    Task<bool> UpdateAsync(CharacterRecord record, CancellationToken ct = default);
}
