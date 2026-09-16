namespace PristonTale.Application.Services;

public sealed record EnterWorldOutcome(
    bool Ok,
    int FieldId,
    uint EntityId,
    float SpawnX,
    float SpawnZ,
    string Message)
{
    public static EnterWorldOutcome Denied(string message) =>
        new(false, 0, 0, 0f, 0f, message);

    public static EnterWorldOutcome Success(int fieldId, uint entityId, float spawnX, float spawnZ, string message) =>
        new(true, fieldId, entityId, spawnX, spawnZ, message);
}
