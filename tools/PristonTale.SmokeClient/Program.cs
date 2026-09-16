using PristonTale.Client.Net;
using PristonTale.Simulation.Characters;

var address = args.Length > 0 ? args[0] : "http://localhost:10012";
Console.WriteLine($"Connecting to {address}…");
await using var conn = new GameServerConnection(address);

var login = await conn.LoginAsync("smoke", "smoke-pw", clientVersion: 1);
Console.WriteLine($"Login            : ok={login.Ok} msg=\"{login.Message}\"");
if (!login.Ok)
    return 1;

var before = await conn.ListCharactersAsync();
Console.WriteLine($"List (before)    : {before.Characters.Count} character(s)");

var name = "Smoke" + Guid.NewGuid().ToString("N")[..6];
var created = await conn.CreateCharacterAsync(name, (int)PlayerClass.Mechanician, faceIndex: 0);
Console.WriteLine($"Create {name,-11}: ok={created.Ok} msg=\"{created.Message}\"");
if (!created.Ok)
    return 1;

var after = await conn.ListCharactersAsync();
Console.WriteLine($"List (after)     : {after.Characters.Count} character(s)");
foreach (var c in after.Characters)
    Console.WriteLine($"    - {c.Name} (class {c.ClassId}, level {c.Level})");

var enter = await conn.EnterWorldAsync(name);
Console.WriteLine($"EnterWorld       : ok={enter.Ok} field={enter.FieldId} entity={enter.EntityId} msg=\"{enter.Message}\"");

Console.WriteLine(enter.Ok ? "SMOKE OK" : "SMOKE FAILED");
return enter.Ok ? 0 : 1;
