namespace PristonTale.Persistence.Abstractions.Accounts;

public sealed record AccountRecord(
    long Id,
    string UserId,
    string PasswordHash,
    bool IsBlocked,
    bool IsTester);
