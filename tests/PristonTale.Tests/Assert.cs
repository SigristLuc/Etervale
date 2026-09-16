namespace PristonTale.Tests;

internal static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void Near(float expected, float actual, float tolerance = 0.0001f)
    {
        True(float.IsFinite(actual) && MathF.Abs(expected - actual) <= tolerance, $"expected {expected}, got {actual}");
    }

    public static async Task ThrowsAsync(Func<Task> operation)
    {
        var threw = false;
        try
        {
            await operation();
        }
        catch
        {
            threw = true;
        }

        True(threw, "expected operation to fail");
    }
}
