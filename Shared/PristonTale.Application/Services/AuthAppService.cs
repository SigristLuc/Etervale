using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Security;

namespace PristonTale.Application.Services;

public sealed class AuthAppService
{
    private readonly ISessionRegistry _sessions;
    private readonly IAccountRepository _accounts;

    public AuthAppService(ISessionRegistry sessions, IAccountRepository accounts)
    {
        _sessions = sessions;
        _accounts = accounts;
    }

    public async Task<LoginOutcome> LoginAsync(string account, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrEmpty(password))
            return LoginOutcome.Denied("account and password are required");

        var record = await _accounts.FindByUserIdAsync(account, ct);

        if (record is null)
        {
            // First login provisions the account (dev bootstrap — see class remarks).
            record = await _accounts.CreateAsync(account, PasswordHasher.Hash(password), ct);
        }
        else
        {
            if (record.IsBlocked)
                return LoginOutcome.Denied("account is blocked");
            if (!PasswordHasher.Verify(password, record.PasswordHash))
                return LoginOutcome.Denied("invalid account or password");
        }

        await _accounts.UpdateLastLoginAsync(record.Id, ct);
        var session = _sessions.Create(record.Id, record.UserId);

        return LoginOutcome.Success(session.SessionToken, $"welcome, {record.UserId}");
    }

    public async Task<LoginOutcome> LoginLocalProfileAsync(string profileName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(profileName))
            return LoginOutcome.Denied("a profile name is required");

        var record = await _accounts.FindByUserIdAsync(profileName, ct);

        if (record is null)
        {
            // First use creates the named local profile; no real password (offline never verifies).
            record = await _accounts.CreateAsync(profileName, PasswordHasher.Hash(string.Empty), ct);
        }

        await _accounts.UpdateLastLoginAsync(record.Id, ct);
        var session = _sessions.Create(record.Id, record.UserId);

        return LoginOutcome.Success(session.SessionToken, $"welcome, {record.UserId}");
    }
}
