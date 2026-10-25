using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Client.Net.Presence;
using PristonTale.Simulation.Social;

namespace PristonTale.Gameplay.UI.Chat;

// The chat on the client: the window, the input line, the speech bubbles and the lines the server sends. A line over a
// character in view becomes a bubble (and, from a player, a log line in the player colour); other lines go to the log in
// their own colour, and the first whisper or notice while the window is closed opens it with the whisper sound.
public sealed class HudChat
{
    private static readonly Color PlayerNameColor = new(255, 255, 200);
    private static readonly Color PartyName = new(220, 255, 160);
    private static readonly Color MonsterName = new(255, 210, 210);
    private static readonly Color NpcName = new(180, 180, 255);
    private static readonly Color HoveredName = Color.White;
    private static readonly Color HoveredMonster = new(255, 230, 200);

    public HudChatWindow Window { get; } = new();
    public HudChatBubbles Bubbles { get; } = new();

    // Sends a typed line (party: the party channel); set by the screen.
    public Func<string, bool, Task> Send { get; set; }

    // What the client knows of an entity in view (HudCombat): its kind and a summon's owner.
    public Func<ulong, (RemoteEntityKind Kind, ulong OwnerId)> Lookup { get; set; } = static _ => (RemoteEntityKind.Unspecified, 0);

    public string PlayerName { get; set; } = "";
    public ulong SelfId { get; set; }

    // Raised when a whisper or notice opens the closed window (the whisper sound).
    public event Action WhisperArrived;

    // Raised when the server asks the client to save and quit.
    public event Action CloseRequested;

    public bool KeyboardCaptured => Window.Focused;

    public void Observe(PresenceUpdate update, double nowMs)
    {
        foreach (var line in update.Chat)
        {
            Receive(line, nowMs);
        }

        if (update.CloseRequested)
        {
            CloseRequested?.Invoke();
        }
    }

    public void Receive(ChatLineInfo line, double nowMs)
    {
        var (kind, _) = line.SpeakerId == 0 ? (RemoteEntityKind.Unspecified, 0u) : Lookup(line.SpeakerId);
        if (kind != RemoteEntityKind.Unspecified && line.Kind != HudChatLog.PartyKind)
        {
            Bubbles.Say(line.SpeakerId, line.Text, nowMs, ChatRules.OtherBubbleMs);
            if (kind == RemoteEntityKind.Player)
            {
                Window.Log.Add(line.Text, HudChatLog.PlayerKind);
            }

            return;
        }

        Window.Log.Add(line.Text, line.Kind);
        if (line.Kind is HudChatLog.WhisperKind or HudChatLog.NoticeKind && !Window.Open)
        {
            Window.Reveal();
            WhisperArrived?.Invoke();
        }
    }

    // A line of the client's own (results of the player's actions) in the server colour.
    public void System(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            Window.Log.Add(text, HudChatLog.ServerKind);
        }
    }

    // A quest hint in the assistant's colour and page.
    public void Assist(string text) => Window.Log.Add(text, HudChatLog.AssistKind);

    // Enter opens the input or sends its line (Escape closes the window through the HUD); Ctrl+V pastes; Page Up and Page Down scroll;
    // Up and Down recall commands. Typed characters (backspace included) arrive from the window's text input.
    public void Update(KeyboardState keyboard, KeyboardState previous, IEnumerable<char> typed, double nowMs, HudChatTab tab)
    {
        bool Pressed(Keys key) => keyboard.IsKeyDown(key) && !previous.IsKeyDown(key);
        var control = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
        if (!Window.Focused)
        {
            if (Pressed(Keys.Enter) && !control)
            {
                Window.OpenInput();
            }

            return;
        }

        foreach (var character in typed)
        {
            if (character == '\b')
            {
                Window.Input.Backspace();
            }
            else
            {
                Window.Input.Type(character);
            }
        }

        if (control && Pressed(Keys.V))
        {
            Window.Input.Paste(Clipboard.GetText());
        }

        if (Pressed(Keys.PageUp))
        {
            Window.Log.ScrollBy(Math.Max(1, Window.ShownMessages));
        }

        if (Pressed(Keys.PageDown))
        {
            Window.Log.ScrollBy(-Math.Max(1, Window.ShownMessages));
        }

        if (Pressed(Keys.Up))
        {
            Window.Input.Recall(older: true);
        }

        if (Pressed(Keys.Down))
        {
            Window.Input.Recall(older: false);
        }

        if (Pressed(Keys.Enter) && !control)
        {
            Submit(nowMs, tab);
        }
    }

    private void Submit(double nowMs, HudChatTab tab)
    {
        if (Window.Input.Text.Length == 0)
        {
            Window.Unfocus();
            return;
        }

        var send = Window.Input.Submit(PlayerName, tab, nowMs, out var complaint);
        if (complaint is not null)
        {
            Bubbles.Say(SelfId, complaint, nowMs, ChatRules.FloodPenaltyMs);
        }

        if (send is not { } line)
        {
            Window.Input.Clear();
            return;
        }

        switch (line.Kind)
        {
            case HudChatSendKind.Spoken:
                Window.Log.Add(line.Shown, HudChatLog.OwnKind);
                Bubbles.Say(SelfId, line.Shown, nowMs, ChatRules.SelfBubbleMs);
                break;
            case HudChatSendKind.Party:
                Window.Log.Add(line.Shown, HudChatLog.PartyKind);
                break;
        }

        _ = Send?.Invoke(line.Text, line.Kind == HudChatSendKind.Party);
    }

    // The colour of a speaker's name in a bubble: players, the party and owned summons, monsters, NPCs; the hovered or
    // selected speaker is drawn brighter.
    public Color NameColor(ulong id, ulong hovered)
    {
        var (kind, owner) = id == SelfId ? (RemoteEntityKind.Player, 0u) : Lookup(id);
        if (id == hovered)
        {
            return kind == RemoteEntityKind.Monster && owner == 0 ? HoveredMonster : HoveredName;
        }

        return kind switch
        {
            RemoteEntityKind.Monster => owner != 0 ? PartyName : MonsterName,
            RemoteEntityKind.Npc => NpcName,
            _ => PlayerNameColor
        };
    }
}
