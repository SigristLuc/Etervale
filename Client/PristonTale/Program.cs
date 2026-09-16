using System;
using PristonTale;
using PristonTale.Display;

if (args.Length == 2 && args[0] == "--validate-display")
{
    using var probe = new DisplayValidationProbe(args[1]);
    probe.Run();
    await probe.WriteReportAsync();
    return 0;
}

string settingsPath = null;
if (args.Length == 2 && args[0] == "--settings")
{
    settingsPath = args[1];
}
else if (args.Length != 0)
{
    Console.Error.WriteLine("Usage: PristonTale [--settings PATH | --validate-display REPORT.json]");
    return 1;
}

using var game = new Game1(settingsPath);
game.Run();
return 0;
