using Gambit.Core.Games;

namespace Gambit.ViewModels;

/// <summary>The time controls offered when starting a game (Play page, "Play from here").</summary>
public static class TimeControlChoices
{
    /// <summary>Settings key, label and clock of each choice.</summary>
    public static IReadOnlyList<(string Key, string Label, TimeControl Control)> All { get; } =
    [
        ("unlimited", "Unlimited — no clock", TimeControl.Unlimited),
        ("1+0", "1 min · Bullet", TimeControl.Minutes(1)),
        ("3+2", "3 | 2 · Blitz", TimeControl.Minutes(3, 2)),
        ("5+0", "5 min · Blitz", TimeControl.Minutes(5)),
        ("10+0", "10 min · Rapid", TimeControl.Minutes(10)),
        ("15+10", "15 | 10 · Rapid", TimeControl.Minutes(15, 10)),
        ("30+0", "30 min · Classical", TimeControl.Minutes(30)),
    ];

    /// <summary>The position of a settings key in <see cref="All"/>, or 0 (unlimited) when unknown.</summary>
    public static int IndexOf(string? key)
    {
        for (int i = 0; i < All.Count; i++)
            if (All[i].Key == key) return i;
        return 0;
    }
}
