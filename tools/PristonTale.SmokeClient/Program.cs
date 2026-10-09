using PristonTale.Client.Net;
using PristonTale.Simulation.Characters;

var address = args.Length > 0 ? args[0] : "tcp://localhost:10009";
var account = args.Length > 1 ? args[1] : "smoke";
var password = args.Length > 2 ? args[2] : "smoke-pw";
Console.WriteLine($"Connecting to {address}…");
await using var conn = new GameServerConnection(address);

var login = await conn.LoginAsync(account, password, GameServerConnection.EnglishClientVersion);
Console.WriteLine($"Login            : ok={login.Ok} msg=\"{login.Message}\"");
if (!login.Ok)
{
    return 1;
}

var before = await conn.ListCharactersAsync();
Console.WriteLine($"List (before)    : {before.Count} character(s)");

var name = "Smoke" + Guid.NewGuid().ToString("N")[..6];
var created = await conn.CreateCharacterAsync(name, (int)PlayerClass.Mechanician, faceIndex: 0);
Console.WriteLine($"Create {name,-11}: ok={created.Ok} msg=\"{created.Message}\"");
if (!created.Ok)
{
    return 1;
}

var after = await conn.ListCharactersAsync();
Console.WriteLine($"List (after)     : {after.Count} character(s)");
foreach (var c in after)
{
    Console.WriteLine($"    - {c.Name} (class {c.ClassId}, level {c.Level})");
}

var enter = await conn.EnterWorldAsync(name);
Console.WriteLine($"EnterWorld       : ok={enter.Ok} field={enter.FieldId} entity={enter.EntityId} msg=\"{enter.Message}\"");
if (!enter.Ok)
{
    return 1;
}

var catalog = await conn.GetWorldCatalogAsync();
await using (var play = conn.OpenPlayChannel())
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var spawns = 0;
    var owner = false;
    try
    {
        await foreach (var update in play.ReadAllAsync(timeout.Token))
        {
            spawns += update.Spawns.Count;
            owner |= update.Owner is not null;
            if (spawns > 1 && owner)
            {
                break;
            }
        }
    }
    catch (OperationCanceledException)
    {
    }

    Console.WriteLine($"Presence         : {spawns} spawn(s), owner status={owner}, catalog {catalog.Monsters.Count} monsters / {catalog.Npcs.Count} NPCs");
    if (spawns == 0 || !owner)
    {
        return 1;
    }

    await play.SendMovementAsync(new System.Numerics.Vector3(enter.SpawnX + 20, 0, enter.SpawnZ), 0f, false, false, 0, enter.FieldId);
}

Console.WriteLine("SMOKE OK");
return 0;
