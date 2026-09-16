using System.Text.Json;
using System.Text.Json.Serialization;
using PristonTale.Simulation.Characters;

namespace PristonTale.Persistence.Abstractions.Characters;

public readonly record struct JsonText(string Value)
{
    public static readonly JsonText EmptyArray = new("[]");

    public static readonly JsonText EmptyObject = new("{}");

    public override string ToString() => Value;
}

public static class CharacterJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    public static JsonText Write<T>(T value) => new(JsonSerializer.Serialize(value, Options));

    public static T? Read<T>(JsonText json) =>
        string.IsNullOrWhiteSpace(json.Value) ? default : JsonSerializer.Deserialize<T>(json.Value, Options);
}

public sealed record DerivedStatsDto(
    int MaxLife,
    int MaxMana,
    int MaxStamina,
    int AttackRating,
    int AttackDamageMin,
    int AttackDamageMax,
    int Defence,
    int Absorption,
    int MoveSpeed,
    int MaxWeight,
    int AttackSpeed)
{
    public static DerivedStatsDto From(DerivedStats d) => new(
        d.MaxLife, d.MaxMana, d.MaxStamina,
        d.AttackRating, d.AttackDamageMin, d.AttackDamageMax,
        d.Defence, d.Absorption, d.MoveSpeed, d.MaxWeight, d.AttackSpeed);
}
