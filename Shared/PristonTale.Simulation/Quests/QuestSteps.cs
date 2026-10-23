namespace PristonTale.Simulation.Quests;

// Shared step helpers: dialogs, the one-quest-at-a-time rule and completion bookkeeping.
public static class QuestSteps
{
    public static QuestPrompt Close(QuestCode code, int state, string document) => new(code, state, document, QuestFollowup.Close)
    {
        Title = QuestTexts.QuestTitle
    };

    public static QuestPrompt Proceed(QuestCode code, int state, string document) => new(code, state, document, QuestFollowup.Proceed)
    {
        Title = QuestTexts.QuestTitle
    };

    public static QuestPrompt Confirm(QuestCode code, int state, string document, (string First, string Second) lines, string detail = "") =>
        new(code, state, document, QuestFollowup.Confirm)
        {
            Title = QuestTexts.QuestTitle,
            ConfirmLine1 = lines.First,
            ConfirmLine2 = lines.Second,
            ConfirmDetail = detail
        };

    // Another quest is running: the step cannot start or move on.
    public static bool Blocked(QuestContext context, QuestCode code)
    {
        var active = context.Journal.Active;
        return !active.IsIdle && active.Code != code;
    }

    // The dialog a follow-up answers must still describe the journal: the running quest at that state, or an offer
    // (state 0) while no quest runs.
    public static bool Matches(QuestContext context, QuestCode code, int state)
    {
        var active = context.Journal.Active;
        return active.Code == code ? active.State == state : active.IsIdle && state == 0;
    }

    public static bool Stale(QuestContext context) => context.Fail(QuestStatus.Stale, "the quest has moved on; talk to the NPC again");

    // Ends the running quest: it joins the finished history and the journal goes idle.
    public static void Finish(QuestContext context, QuestCode code)
    {
        context.Journal.Active = ActiveQuest.Idle;
        context.Journal.Finish(code);
        context.Notice(QuestNoticeKind.Fanfare, "");
    }

    // A hunt the quest timer icon follows; the fanfare plays as the icon appears.
    public static ActiveQuest StartHunt(QuestContext context, ActiveQuest quest, int target, int count, QuestTimerKind timer, long timerMs = 0)
    {
        context.Notice(QuestNoticeKind.Fanfare, "");
        context.Notice(QuestNoticeKind.Event, QuestTexts.Hunt(count, QuestMonsters.NameOf(target)));
        return quest.Hunting(target, count) with { Timer = timer, TimerMs = timerMs };
    }
}
