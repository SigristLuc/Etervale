namespace PristonTale.Client.Net;

public static class OfflineSaveLocation
{
    private const string FolderName = "PristonTale";
    private const string DatabaseFileName = "offline.db";

    public static string Default()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(root, FolderName);
        Directory.CreateDirectory(directory);

        Console.WriteLine(directory);

        return Path.Combine(directory, DatabaseFileName);
    }
}
