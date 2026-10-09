using PristonTale.Simulation.Progression;
using PristonTale.Simulation.World.Npcs;
using System.Numerics;
using System.Text.Json;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World;

namespace PristonTale.Application.Services;

public sealed class SessionAppService
{
    private readonly ISessionRegistry _sessions;
    private readonly CharacterClassRegistry _classes;
    private readonly ICharacterRepository _characters;
    private readonly WorldCatalog _world;
    private readonly WorldServer? _host;
    private readonly WorldDataHolder _worldData;

    public SessionAppService(
        ISessionRegistry sessions,
        CharacterClassRegistry classes,
        ICharacterRepository characters,
        WorldCatalog world,
        WorldServer? host = null,
        WorldDataHolder? worldData = null)
    {
        _sessions = sessions;
        _classes = classes;
        _characters = characters;
        _world = world;
        _host = host;
        _worldData = worldData ?? new WorldDataHolder();
    }

    public async Task<EnterWorldOutcome> EnterWorldAsync(string sessionToken, string characterName, CancellationToken ct = default)
    {
        if (!_sessions.TryGet(sessionToken, out var session))
        {
            return EnterWorldOutcome.Denied(EnterWorldFailure.InvalidSession);
        }

        if (!session.TryBeginEntry())
        {
            return EnterWorldOutcome.Denied(EnterWorldFailure.AlreadyEntering);
        }

        long? claimedCharacter = null;
        var ready = false;
        try
        {
            var record = await _characters.FindByNameForAccountAsync(session.AccountId, characterName ?? string.Empty, ct);
            if (record is null)
            {
                return EnterWorldOutcome.Denied(EnterWorldFailure.NotFound);
            }

            if (!_world.TryGetFieldByCode(record.FieldId, out var field) || !Enum.IsDefined(typeof(PlayerClass), record.JobCode))
            {
                return EnterWorldOutcome.Denied(EnterWorldFailure.InvalidSavedClassOrField);
            }

            if (!_sessions.TryClaimCharacter(record.Id, sessionToken))
            {
                return EnterWorldOutcome.Denied(EnterWorldFailure.AlreadyActive);
            }

            claimedCharacter = record.Id;
            var attributes = new CharacterAttributes(record.Strength, record.Spirit, record.Talent, record.Agility, record.Health);
            var character = new PlayerCharacter(_classes.Get((PlayerClass)record.JobCode), record.Name, record.Level, attributes);
            try
            {
                CharacterStateCodec.Restore(character, record, _worldData.Current.Items);
            }
            catch (Exception exception) when (exception is InvalidDataException or JsonException or ArgumentException)
            {
                return EnterWorldOutcome.Denied(EnterWorldFailure.UnsupportedSavedState);
            }
            if (record.Life > 0)
            {
                character.Life = Math.Clamp(record.Life, 1, character.MaxLife);
                character.Mana = Math.Clamp(record.Mana, 0, character.MaxMana);
                character.Stamina = Math.Clamp(record.Stamina, 0, character.MaxStamina);
            }

            var spawn = ResolveSpawn(record, field);
            if (!_sessions.TryGet(sessionToken, out var current) || !ReferenceEquals(current, session))
            {
                return EnterWorldOutcome.Denied(EnterWorldFailure.SessionClosed);
            }

            session.CharacterName = record.Name;
            session.Character = character;
            session.Loaded = record;
            session.FieldId = field.FieldCode;
            session.SpawnPosition = spawn;
            session.InVillage = field.State == FieldState.Village;
            if (!session.CompleteEntry(true))
            {
                return EnterWorldOutcome.Denied(EnterWorldFailure.SessionClosed);
            }

            ready = true;
            return EnterWorldOutcome.Success(session.FieldId, session.EntityId, spawn.X, spawn.Z, $"entered field {session.FieldId}");
        }
        finally
        {
            if (!ready && claimedCharacter.HasValue)
            {
                _sessions.ReleaseCharacter(claimedCharacter.Value, sessionToken);
            }

            if (!ready)
            {
                session.CompleteEntry(false);
            }
        }
    }

    private static Vector3 ResolveSpawn(CharacterRecord record, FieldDef field)
    {
        var saved = new Vector3(record.PosX, 0f, record.PosZ);
        if (WorldPosition.IsValid(saved) && (record.PosX != 0f || record.PosZ != 0f))
        {
            return saved;
        }

        if (field.StartPoints.Count > 0)
        {
            var start = field.StartPoints[0];
            return new Vector3(start.X, 0f, start.Z);
        }

        return new Vector3(field.Center.X, 0f, field.Center.Z);
    }

    public async Task<bool> SaveCharacterAsync(string sessionToken, CancellationToken ct = default)
    {
        if (!_sessions.TryGet(sessionToken, out var session))
        {
            return false;
        }

        await session.SaveGate.WaitAsync(ct);
        try
        {
            if (session.Character is null || session.Loaded is null)
            {
                return false;
            }

            var snapshot = _host is null
                || !session.IsPlaying ? CharacterRecordFactory.Capture(
                    session.Loaded,
                    session.Character,
                    session.FieldId,
                    session.SpawnPosition) : await _host.CaptureForSaveAsync(session);
            if (snapshot is null)
            {
                return false;
            }

            return await _characters.UpdateAsync(snapshot.Encode(), ct);
        }
        finally
        {
            session.SaveGate.Release();
        }
    }

    public Task<InventoryOutcome> ApplyInventoryAsync(string sessionToken, InventoryCommand command, CancellationToken ct = default)
    {
        return ApplyDurableAsync(sessionToken, session => _host!.PrepareInventoryAsync(session, command),
            (session, operation) => _host!.CommitInventoryAsync(session, operation),
            message => new InventoryOutcome(false, 0, message), ct);
    }

    public Task<ProgressionOutcome> ApplyProgressionAsync(
        string sessionToken, ProgressionCommand command, CancellationToken ct = default)
    {
        return ApplyDurableAsync(sessionToken, session => _host!.PrepareProgressionAsync(session, command),
            (session, operation) => _host!.CommitProgressionAsync(session, operation),
            message => new ProgressionOutcome(false, 0, message), ct);
    }

    private async Task<TOutcome> ApplyDurableAsync<TOutcome>(string token,
        Func<PlayerSession, Task<PreparedCharacterOperation<TOutcome>?>> prepare,
        Func<PlayerSession, PreparedCharacterOperation<TOutcome>, Task<bool>> commit,
        Func<string, TOutcome> rejected, CancellationToken ct)
    {
        if (_host is null || !_sessions.TryGet(token, out var session))
        {
            return rejected("invalid session");
        }

        // One admitted mutation per character; saves and all economic operations share this gate.
        if (!await session.SaveGate.WaitAsync(0, ct))
        {
            return rejected("save or character operation pending; retry");
        }

        PreparedCharacterOperation<TOutcome>? prepared = null;
        bool stored = false;
        try
        {
            prepared = await prepare(session);
            if (prepared is null)
            {
                return rejected("character is not playing");
            }

            if (prepared.Snapshot is null)
            {
                return prepared.Outcome;
            }

            // Finish an admitted transaction despite cancellation. Retrying its UUID recovers the outcome.
            if (!await _characters.UpdateAsync(prepared.Snapshot.Encode(), CancellationToken.None))
            {
                return rejected("storage rejected operation; character unchanged");
            }

            stored = true;
            await commit(session, prepared);
            return prepared.Outcome;
        }
        finally
        {
            try
            {
                if (!stored && prepared is PreparedPickupOperation)
                {
                    await _host.AbortCharacterOperationAsync(prepared);
                }
            }
            finally
            {
                session.SaveGate.Release();
            }
        }
    }
}
