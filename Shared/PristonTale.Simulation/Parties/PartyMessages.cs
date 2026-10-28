namespace PristonTale.Simulation.Parties;

// The original's party lines: replies to the player who acted and notices every member receives.
public static class PartyMessages
{
    public const string Recommended = "Party Leader {0} has recommended {1} to join the party";
    public const string Requested = "sent a party request to {0}";
    public const string AlreadyMember = "{0} is already a party member";
    public const string InOtherParty = "{0} is already a member of other party";
    public const string LevelGap = "Level difference is too big";
    public const string NotFound = "{0} is currently offline, or cannot be found";
    public const string YouLeft = "You have left the party";
    public const string Expelled = "{0} has been expelled from the party";
    public const string Disbanded = "Party has been disbanded";
    public const string Delegated = "{0} has been delegated as the new party leader";

    public const string LeaderNow = "party leader is now {0}";
    public const string Released = "party has been disbanded";
    public const string Entered = "{0} has entered the party";
    public const string CannotEnter = "Unable to enter the party";
    public const string Left = "{0} has left the party";

    // The request box's second line, under the name of who asks; a member's recommendation names the player instead.
    public const string Request = "has sent a party request";
    public const string Recommends = "recommends {0}";

    public static string Format(string message, string first, string second = "") =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, message, first, second);
}
