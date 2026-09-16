using Microsoft.Extensions.DependencyInjection;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.Security;
using PristonTale.Persistence.Postgres;
using PristonTale.Simulation.Characters;

// Dev CLI to seed accounts + characters into the dev Postgres, for testing the login / char-select wire.
// Reuses the real persistence layer (PasswordHasher, repositories, CharacterRecordFactory) so seeded data
// is identical to what the server would create. The schema is ensured idempotently via DatabaseMigrator.

const string DefaultConnection = "Host=localhost;Port=5433;Database=pristontale;Username=pristontale;Password=pristontale";
const int StartingFieldId = 3; // Ricarten — matches CharacterGrpcService.StartingFieldId
const int MaxSlots = 6;

var argv = new List<string>(args);
var connection = TakeOption(argv, "--conn") ?? Environment.GetEnvironmentVariable("PT_PG_CONN") ?? DefaultConnection;

if (argv.Count == 0)
{
    PrintUsage();
    return 1;
}
var command = argv[0].ToLowerInvariant();

var services = new ServiceCollection();
services.AddLogging(); // no provider — the migrator's ILogger is a no-op; results/errors print to console
services.AddPristonPostgres(connection);
services.AddSingleton<CharacterClassRegistry>();
await using var provider = services.BuildServiceProvider();

var migrator = provider.GetRequiredService<IDatabaseMigrator>();
var accounts = provider.GetRequiredService<IAccountRepository>();
var characters = provider.GetRequiredService<ICharacterRepository>();
var classes = provider.GetRequiredService<CharacterClassRegistry>();

try
{
    await migrator.MigrateAsync(); // idempotent: ensures the schema exists even if the server never ran
    return command switch
    {
        "create-account" => await CreateAccount(argv),
        "add-char" => await AddCharacter(argv),
        "list" => await ListCharacters(argv),
        _ => Usage($"unknown command '{command}'"),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    Console.Error.WriteLine("(is the dev Postgres up? container pgsql-pristontale on localhost:5433)");
    return 1;
}

async Task<int> CreateAccount(List<string> a)
{
    if (a.Count < 3)
        return Usage("usage: create-account <user> <password>");
    var (user, password) = (a[1], a[2]);

    if (await accounts.FindByUserIdAsync(user) is not null)
    {
        Console.WriteLine($"account '{user}' already exists — leaving it unchanged");
        return 0;
    }
    var account = await accounts.CreateAsync(user, PasswordHasher.Hash(password));
    Console.WriteLine($"created account #{account.Id} '{account.UserId}'");
    return 0;
}

async Task<int> AddCharacter(List<string> a)
{
    if (a.Count < 4)
        return Usage($"usage: add-char <user> <name> <class> [level]   (class: {ClassList()})");
    var (user, name, classText) = (a[1], a[2], a[3]);
    if (!TryParseClass(classText, out var playerClass))
        return Usage($"unknown class '{classText}' (valid: {ClassList()})");
    var level = a.Count > 4 && int.TryParse(a[4], out var parsed) ? Math.Max(1, parsed) : 1;

    var account = await accounts.FindByUserIdAsync(user);
    if (account is null)
        return Usage($"no account '{user}' — run create-account first");
    if (name.Length is < 3 or > 16)
        return Usage("character name must be 3–16 characters");
    if (await characters.NameExistsAsync(name))
        return Usage($"character name '{name}' is already taken");

    var used = await characters.UsedSlotsAsync(account.Id);
    if (used.Count >= MaxSlots)
        return Usage($"account '{user}' has no free character slots (max {MaxSlots})");
    short slot = 0;
    while (used.Contains(slot)) slot++;

    var record = CharacterRecordFactory.NewStarter(classes, account.Id, name, slot, playerClass, StartingFieldId, level: level);
    var created = await characters.CreateAsync(record);
    Console.WriteLine($"created character #{created.Id} '{created.Name}' ({playerClass}, level {created.Level}) in slot {slot} of '{user}'");
    return 0;
}

async Task<int> ListCharacters(List<string> a)
{
    if (a.Count < 2)
        return Usage("usage: list <user>");
    var account = await accounts.FindByUserIdAsync(a[1]);
    if (account is null)
        return Usage($"no account '{a[1]}'");

    var rows = await characters.ListByAccountAsync(account.Id);
    Console.WriteLine($"account '{account.UserId}' (#{account.Id}) — {rows.Count} character(s):");
    foreach (var r in rows)
        Console.WriteLine($"  slot {r.Slot}: {r.Name,-16} {(PlayerClass)r.JobCode,-10} Lv {r.Level,-3} " +
                          $"str {r.Strength} spr {r.Spirit} tal {r.Talent} agi {r.Agility} hea {r.Health}");
    return 0;
}

// --- helpers -------------------------------------------------------------------------------------

static bool TryParseClass(string text, out PlayerClass playerClass)
{
    if (Enum.TryParse(text, ignoreCase: true, out playerClass) && Enum.IsDefined(playerClass))
        return true;
    if (int.TryParse(text, out var id) && Enum.IsDefined((PlayerClass)id))
    {
        playerClass = (PlayerClass)id;
        return true;
    }
    playerClass = default;
    return false;
}

static string ClassList() => string.Join(", ", Enum.GetValues<PlayerClass>().Select(c => $"{(int)c}={c}"));

// Extract `--name value` from the arg list (mutating it), returning the value or null.
static string? TakeOption(List<string> a, string name)
{
    var i = a.IndexOf(name);
    if (i < 0 || i + 1 >= a.Count)
        return null;
    var value = a[i + 1];
    a.RemoveRange(i, 2);
    return value;
}

static int Usage(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("""
        ptdevaccounts — seed dev accounts/characters into the dev Postgres.

        Commands:
          create-account <user> <password>            create a login account (hashed)
          add-char <user> <name> <class> [level]      seed a character on an account
          list <user>                                 list an account's characters

        Options:
          --conn "<npgsql connection string>"         override the DB (default: dev localhost:5433)

        Examples:
          dotnet run --project tools/PristonTale.DevAccounts -- create-account demo change-me
          dotnet run --project tools/PristonTale.DevAccounts -- add-char demo Hero Knight 20
          dotnet run --project tools/PristonTale.DevAccounts -- list demo
        """);
}
