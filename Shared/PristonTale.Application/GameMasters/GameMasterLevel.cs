namespace PristonTale.Application.GameMasters;

// Account game-master levels. Each command names the lowest level that may use it; a higher level may use every command
// of the levels below.
public static class GameMasterLevel
{
    public const int Player = 0;

    // Information, monster calls, hiding.
    public const int Guide = 1;

    // Notices, kicks, warps, account moves and the shutdown.
    public const int Moderator = 2;

    // Item gifts and the server's view of gold.
    public const int Operator = 3;

    // Item creation, premium resets, monster absorption, experience and gold.
    public const int Administrator = 4;

    public const int Highest = Administrator;
}
