namespace PristonTale.Application.Services;

// Values are the FAILCONNECT WParam codes the original login screen understands.
// Unknown accounts report a bad password so the reply does not reveal which accounts exist.
public enum LoginFailure
{
    None = 0,
    BadCredentials = -2,
    LoginDenied = -24
}

public sealed record LoginOutcome(bool Ok, string SessionToken, string Message)
{
    public LoginFailure Failure { get; init; }

    public static LoginOutcome Denied(LoginFailure failure) => new(false, string.Empty, MessageFor(failure)) { Failure = failure };

    public static LoginOutcome Success(string sessionToken, string message) =>
        new(true, sessionToken, message);

    public static string MessageFor(LoginFailure failure) => failure switch
    {
        LoginFailure.None => "",
        LoginFailure.BadCredentials => "invalid account or password",
        LoginFailure.LoginDenied => "account is blocked",
        _ => "login failed"
    };
}
