namespace PristonTale.Gameplay.UI.Npc;

public static class HudCraftArt
{
    private const string Shop = "image/Sinimage/shopall/";
    private const string Aging = Shop + "Aging/";
    private const string Craft = Shop + "CraftItem/";

    public const string Panel = Craft + "CraftItemMain.bmp";
    public const string StartButton = Craft + "Button.bmp";
    public const string MixStartInfo = Craft + "ButtonInfo.bmp";
    public const string MixTitle = Craft + "Title.bmp";
    public const string AgingTitle = Aging + "Aging_Title.bmp";
    public const string AgingStartInfo = Aging + "ButtonInfo.bmp";

    public const string AgingBar = Aging + "Bar.bmp";
    public const string AgingFill = Aging + "Bar_.bmp";
    public const string AgingPlate = Aging + "Aging.bmp";

    public static string SheltomHint(int ordinal) => $"{Aging}itos{100 + ordinal}_.bmp";

    public static readonly string[] All =
    [
        Panel, StartButton, MixStartInfo, MixTitle, AgingTitle, AgingStartInfo, AgingBar, AgingFill, AgingPlate,
        .. Enumerable.Range(1, 14).Select(SheltomHint)
    ];
}
