namespace PristonTale.Gameplay.UI.Npc;

// Asset-relative paths of the original aging and mixing window images.
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

    // The tooltip's aging gauge: the bar of a maturing item with its fill, or the plate of an item that finished its aging.
    public const string AgingBar = Aging + "Bar.bmp";
    public const string AgingFill = Aging + "Bar_.bmp";
    public const string AgingPlate = Aging + "Aging.bmp";

    // The grey hint of a sheltom (1 Lucidy .. 14 Ordo) in the sheltom cells.
    public static string SheltomHint(int ordinal) => $"{Aging}itos{100 + ordinal}_.bmp";

    public static readonly string[] All =
    [
        Panel, StartButton, MixStartInfo, MixTitle, AgingTitle, AgingStartInfo, AgingBar, AgingFill, AgingPlate,
        .. Enumerable.Range(1, 14).Select(SheltomHint)
    ];
}
