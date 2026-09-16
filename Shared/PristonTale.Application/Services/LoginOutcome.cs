namespace PristonTale.Application.Services;

public sealed record LoginOutcome(bool Ok, string SessionToken, string Message)
{
    public static LoginOutcome Denied(string message) => new(false, string.Empty, message);

    public static LoginOutcome Success(string sessionToken, string message) =>
        new(true, sessionToken, message);
}
