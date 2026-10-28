using PristonTale.Gameplay.UI.Quests;

namespace PristonTale.Gameplay.UI.Parties;

// Asset-relative paths of the party window's party page (its frame is the quest page's), the request box, the minimap
// mark and the buttons under another player's portrait. A button image ending in '_' is its idle look; the plain one is
// drawn under the pointer.
public static class HudPartyArt
{
    private const string Party = "image/party/";
    private const string Effect = "Effect/Party/";

    public const string Window = HudQuestArt.Window;
    public const string Title = Party + "title-NoClan_Party.tga";

    // In PartyCommand order, then close.
    public static readonly string[] Buttons = [Party + "icon-1.bmp", Party + "icon-2.bmp", Party + "icon-3.bmp", Party + "icon-4.bmp", Party + "ButtonExit.bmp"];
    public static readonly string[] ButtonsIdle = [Party + "icon-1_.bmp", Party + "icon-2_.bmp", Party + "icon-3_.bmp", Party + "icon-4_.bmp", Party + "close_.bmp"];
    public static readonly string[] Tips =
    [
        Party + "ButtonInfo-1.bmp", Party + "ButtonInfo-2.bmp", Party + "ButtonInfo-3.bmp", Party + "ButtonInfo-4.bmp", Party + "ButtonInfo-5.bmp"
    ];

    public const string LifeRed = Effect + "LifeRed.bmp";
    public const string LifeBlue = Effect + "LifeBlue.bmp";

    // A member's mark: idle, under the pointer or picked, and the leader's.
    public const string Member = Effect + "Man1.bmp";
    public const string MemberHovered = Effect + "Man2.bmp";
    public const string Leader = Effect + "Man3.bmp";

    public const string RequestBox = Effect + "MessageBackGround.tga";
    public const string Ok = Effect + "Ok1.bmp";
    public const string OkHovered = Effect + "Ok2.bmp";
    public const string Cancel = Effect + "Cancel1.bmp";
    public const string CancelHovered = Effect + "Cancel2.bmp";

    public const string MapMark = "image/party.tga";

    // The trade, party and friend buttons under another player's portrait.
    public const string PlayerMenu = "image/cw-1.tga";
    public static readonly string[] PlayerButtons = [Party + "icon-c5.bmp", Party + "icon-c1.bmp", Party + "icon-c2.bmp"];
    public static readonly string[] PlayerButtonsIdle = [Party + "icon-c5_.bmp", Party + "icon-c1_.bmp", Party + "icon-c2_.bmp"];
    public static readonly string[] PlayerTips = [Party + "ButtonInfo-c5.bmp", Party + "ButtonInfo-c1.bmp", Party + "ButtonInfo-c0.bmp"];

    public static readonly string[] All =
    [
        Window, Title, .. Buttons, .. ButtonsIdle, .. Tips, LifeRed, LifeBlue, Member, MemberHovered, Leader, RequestBox, Ok, OkHovered, Cancel,
        CancelHovered, MapMark, PlayerMenu, .. PlayerButtons, .. PlayerButtonsIdle, .. PlayerTips
    ];
}
