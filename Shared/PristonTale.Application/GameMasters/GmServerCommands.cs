using PristonTale.Application.Chat;
using static PristonTale.Application.GameMasters.GmInformationCommands;

namespace PristonTale.Application.GameMasters;

// Server-wide commands: notices, the player limit, the shutdown and the refused-login list.
internal static class GmServerCommands
{
    // "/>text" tells every player "Notice> text" now; "/>>text" sets the standing notice every player in the world
    // receives once with the next heartbeat.
    public static Task Notice(GmCommandContext context)
    {
        var line = context.Line;
        if (line.Length > 2 && line[2] == '>')
        {
            context.Commands.Directory.SetNotice(line[3..]);
            context.Note("standing notice set");
        }
        else
        {
            ServerNotices.Broadcast(context.Commands.Directory, line[2..]);
            context.Note("notice sent");
        }

        return Task.CompletedTask;
    }

    public static Task UserLimit(GmCommandContext context)
    {
        var word = context.Word();
        var server = context.Commands.Server;
        if (word.Length > 0)
        {
            server.UserLimit = Math.Max(0, ChatCommandText.ToNumber(word));
            context.Reply(Format("Maximum users have been set to {0}", server.UserLimit));
        }
        else
        {
            context.Reply(Format("Current maximum users setting is {0}", server.UserLimit));
        }

        return Task.CompletedTask;
    }

    public static async Task ReloadSettings(GmCommandContext context)
    {
        if (context.Commands.Settings is not { } settings)
        {
            context.Reply("The settings table is not available");
            return;
        }

        var current = await settings.ReloadAsync();
        context.Reply(Format("Settings reloaded ({0}, {1} problems)", current.ServerName, settings.Problems.Count));
        foreach (var problem in settings.Problems.Take(5))
        {
            context.Reply(problem);
        }
    }

    // "/setting name" shows a setting; "/setting name value" writes and applies it.
    public static async Task Setting(GmCommandContext context)
    {
        var name = context.Word();
        var value = context.Rest.Trim();
        if (context.Commands.Settings is not { } settings
            || Settings.ServerSettingCatalog.All.FirstOrDefault(setting => setting.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is not { } definition)
        {
            context.Reply(Format("Unknown setting {0}", name));
            return;
        }

        if (value.Length == 0)
        {
            context.Reply(Format("{0} = {1}: {2}", definition.Name, settings.ValueOf(definition), definition.Description));
            return;
        }

        context.Reply(await settings.SetAsync(definition.Name, value)
            ? Format("{0} = {1}", definition.Name, value)
            : Format("{0} refused for {1}", value, definition.Name));
    }

    // Starts the countdown: notices, then a reminder every half minute (every five seconds in the last minute), then every
    // client is told to save and quit and the server stops.
    public static Task Shutdown(GmCommandContext context)
    {
        context.Note(context.Commands.Server.BeginShutdown() ? "shutdown started" : "shutdown already running");
        return Task.CompletedTask;
    }

    public static Task ShowBlocked(GmCommandContext context)
    {
        var filter = context.Word();
        var shown = 0;
        foreach (var blocked in context.Commands.Server.BlockedLogins())
        {
            if (filter.Length == 0 || blocked.Account.Equals(filter, StringComparison.OrdinalIgnoreCase))
            {
                context.Reply(Format("> ID ( {0} ) / ( {1} )Min.", blocked.Account, blocked.RemainingMs / 60_000));
                shown++;
            }
        }

        context.Reply(Format("> Total {0} Users", shown));
        return Task.CompletedTask;
    }

    public static Task ResetBlocked(GmCommandContext context)
    {
        context.Commands.Server.ClearBlockedLogins();
        context.Reply("> Reset BlackUser OK!");
        return Task.CompletedTask;
    }

    public static Task FreeBlocked(GmCommandContext context)
    {
        context.Reply(context.Commands.Server.ReleaseBlockedLogin(context.Word()) ? "> Free BlackUser OK!" : "> Non-Blackuser, Check again.");
        return Task.CompletedTask;
    }
}

// Notices the server sends to everybody: from a game master, or from the server itself (the shutdown countdown).
public static class ServerNotices
{
    public static void Broadcast(ChatDirectory directory, string text) =>
        directory.Broadcast(new ChatLine(ChatKind.Notice, 0, Simulation.Social.ChatRules.ClipToCapacity("Notice> " + text)));

    // A line that already carries its own prefix (the shutdown countdown).
    public static void BroadcastRaw(ChatDirectory directory, string text) =>
        directory.Broadcast(new ChatLine(ChatKind.Notice, 0, Simulation.Social.ChatRules.ClipToCapacity(text)));
}
