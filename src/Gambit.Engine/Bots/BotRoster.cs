namespace Gambit.Engine.Bots;

/// <summary>The built-in bots, weakest to strongest (names run A → M).</summary>
public static class BotRoster
{
    private const int AllMoves = int.MaxValue;

    public static IReadOnlyList<BotProfile> Bots { get; } = Build();

    public static BotProfile Get(string id) =>
        Bots.FirstOrDefault(b => b.Id == id) ?? Bots[0];

    private static List<BotProfile> Build() =>
    [
        new()
        {
            Id = "acorn", BookDepth = 0, Name = "Acorn", Rating = 250, Monogram = "A", Color = "#7CB342",
            Tagline = "Just learned how the horsey moves.",
            Bio = "Acorn is brand new to chess and mostly moves whatever looks fun. A perfect first opponent.",
            MaxDepth = 1, Candidates = AllMoves, Temperature = 35, EvalNoise = 35, OversightChance = 0.1575,
            ThinkTimeMs = 200, MinMoveDelayMs = 500, MaxMoveDelayMs = 1500,
            Greetings = ["Hi! Is the castle the one that moves sideways?", "Let's play! I like the pointy-hat pieces."],
            WinLines = ["Wait… I won? Yay!"], LossLines = ["Good game! You're really good!"], DrawLines = ["A tie! That's like winning, right?"],
        },
        new()
        {
            Id = "bramble", BookDepth = 2, Name = "Bramble", Rating = 400, Monogram = "B", Color = "#26A69A",
            Tagline = "Knows the rules. Mostly.",
            Bio = "Bramble grabs anything that isn't nailed down — and sometimes things that are.",
            MaxDepth = 1, Candidates = AllMoves, Temperature = 20, EvalNoise = 20, OversightChance = 0.1075,
            ThinkTimeMs = 250, MinMoveDelayMs = 500, MaxMoveDelayMs = 1400,
            Greetings = ["Ooh, a new challenger!", "I love capturing things. Fair warning."],
            WinLines = ["Grabbed my way to victory!"], LossLines = ["You got me. Rematch?"], DrawLines = ["Huh, nobody wins?"],
        },
        new()
        {
            Id = "clover", BookDepth = 4, Name = "Clover", Rating = 600, Monogram = "C", Color = "#29B6F6",
            Tagline = "Lucky, cheerful, a bit careless.",
            Bio = "Clover spots one-move threats but forgets to check what the opponent can do next.",
            MaxDepth = 2, Candidates = AllMoves, Temperature = 35, EvalNoise = 35, OversightChance = 0.075,
            ThinkTimeMs = 300, MinMoveDelayMs = 450, MaxMoveDelayMs = 1300,
            Greetings = ["Feeling lucky today!", "Good luck — I'll need some too."],
            WinLines = ["Lucky me!"], LossLines = ["Well played!"], DrawLines = ["Split the clover leaves!"],
        },
        new()
        {
            Id = "dash", BookDepth = 4, Name = "Dash", Rating = 800, Monogram = "D", Color = "#FFA726",
            Tagline = "Plays fast. Thinks later.",
            Bio = "Dash loves quick attacks and early queen adventures. Punish the rush!",
            Style = BotStyle.Aggressive,
            MaxDepth = 2, Candidates = AllMoves, Temperature = 25, EvalNoise = 25, OversightChance = 0.08,
            ThinkTimeMs = 300, MinMoveDelayMs = 300, MaxMoveDelayMs = 900,
            Greetings = ["Ready, set, go!", "Let's make this quick!"],
            WinLines = ["Speed wins!"], LossLines = ["Too slow… I mean, too fast. GG!"], DrawLines = ["A photo finish."],
        },
        new()
        {
            Id = "ember", BookDepth = 6, Name = "Ember", Rating = 1000, Monogram = "E", Color = "#EF5350",
            Tagline = "Sparks fly when Ember attacks.",
            Bio = "Ember knows the basic tactics and loves a direct attack on the king.",
            Style = BotStyle.Aggressive,
            MaxDepth = 3, MaxNodes = 40_000, Candidates = 12, Temperature = 35, EvalNoise = 35, OversightChance = 0.07,
            ThinkTimeMs = 400,
            Greetings = ["Hope you brought a fire extinguisher.", "Let's turn up the heat."],
            WinLines = ["Burned!"], LossLines = ["You put out my fire. Nice."], DrawLines = ["The fire fizzles out."],
        },
        new()
        {
            Id = "flint", BookDepth = 8, Name = "Flint", Rating = 1200, Monogram = "F", Color = "#8D6E63",
            Tagline = "Hard to crack.",
            Bio = "Flint plays solid, sensible chess and waits for you to over-extend.",
            Style = BotStyle.Solid,
            MaxDepth = 4, MaxNodes = 80_000, Candidates = 8, Temperature = 30, EvalNoise = 30, OversightChance = 0.055,
            ThinkTimeMs = 500,
            Greetings = ["Solid as a rock.", "Let's see who blinks first."],
            WinLines = ["Patience pays."], LossLines = ["You cracked me. Respect."], DrawLines = ["Rock meets rock."],
        },
        new()
        {
            Id = "gale", BookDepth = 8, Name = "Gale", Rating = 1400, Monogram = "G", Color = "#5C6BC0",
            Tagline = "A storm of tactics.",
            Bio = "Gale calculates a few moves ahead and punishes loose pieces.",
            Style = BotStyle.Tricky,
            MaxDepth = 5, MaxNodes = 150_000, Candidates = 6, Temperature = 55, EvalNoise = 55, OversightChance = 0.0375,
            ThinkTimeMs = 700,
            Greetings = ["The wind is picking up.", "Watch your loose pieces."],
            WinLines = ["Blown away!"], LossLines = ["You weathered the storm."], DrawLines = ["Calm after the storm."],
        },
        new()
        {
            Id = "harbor", BookDepth = 10, Name = "Harbor", Rating = 1600, Monogram = "H", Color = "#00897B",
            Tagline = "Steady positional play.",
            Bio = "Harbor understands structure, good pieces and when to trade. A real club player.",
            Style = BotStyle.Solid,
            MaxDepth = 6, MaxNodes = 300_000, Candidates = 4, Temperature = 35, EvalNoise = 35, OversightChance = 0.0575,
            ThinkTimeMs = 900,
            Greetings = ["Welcome aboard.", "Let's sail into a nice endgame."],
            WinLines = ["Safely in port."], LossLines = ["You navigated that well."], DrawLines = ["Anchored in a draw."],
        },
        new()
        {
            Id = "iris", BookDepth = 12, Name = "Iris", Rating = 1800, Monogram = "I", Color = "#AB47BC",
            Tagline = "Sees more than you think.",
            Bio = "Iris rarely blunders and finds clever tactical shots. Bring your best.",
            Style = BotStyle.Tricky,
            MaxDepth = 8, MaxNodes = 700_000, Candidates = 5, Temperature = 60, EvalNoise = 60, OversightChance = 0.025,
            ThinkTimeMs = 1200, UsesClock = true,
            Greetings = ["I've been expecting you.", "Let's see what you see."],
            WinLines = ["I saw it coming."], LossLines = ["I didn't see that. Impressive."], DrawLines = ["We see eye to eye."],
        },
        new()
        {
            Id = "jade", BookDepth = 14, Name = "Jade", Rating = 2000, Monogram = "J", Color = "#43A047",
            Tagline = "Expert. Precise. Patient.",
            Bio = "Jade plays expert-level chess with deep calculation and good technique.",
            MaxDepth = 10, MaxNodes = 1_500_000, Candidates = 5, Temperature = 40, EvalNoise = 60, OversightChance = 0.04,
            ThinkTimeMs = 1500, UsesClock = true,
            Greetings = ["A worthy game, I hope.", "Precision is everything."],
            WinLines = ["Precisely."], LossLines = ["Exceptional play."], DrawLines = ["A balanced result."],
        },
        new()
        {
            Id = "kestrel", BookDepth = 16, Name = "Kestrel", Rating = 2200, Monogram = "K", Color = "#F4511E",
            Tagline = "Hovers. Waits. Strikes.",
            Bio = "Kestrel is a master-level hunter that converts the smallest advantages.",
            Style = BotStyle.Aggressive,
            MaxDepth = 14, MaxNodes = 4_000_000, ThinkTimeMs = 1800, UsesClock = true,
            Greetings = ["Scanning the board…", "Every weakness will be found."],
            WinLines = ["Strike."], LossLines = ["You evaded me. Rare."], DrawLines = ["No opening found."],
        },
        new()
        {
            Id = "lumen", BookDepth = 20, Name = "Lumen", Rating = 2400, Monogram = "L", Color = "#FDD835",
            Tagline = "Illuminates every line.",
            Bio = "Lumen calculates deeply and plays near full engine strength.",
            ThinkTimeMs = 3000, UsesClock = true,
            Greetings = ["Let there be light.", "Every line, illuminated."],
            WinLines = ["Clarity."], LossLines = ["Brilliant. Truly."], DrawLines = ["Light and shadow, balanced."],
        },
        new()
        {
            Id = "monolith", BookDepth = 24, Name = "Monolith", Rating = 2600, IsMaxStrength = true, Monogram = "M", Color = "#455A64",
            Tagline = "Full strength. No mercy.",
            Bio = "The engine at full power with generous thinking time. Good luck.",
            ThinkTimeMs = 6000, UsesClock = true, MinMoveDelayMs = 0, MaxMoveDelayMs = 200,
            Greetings = ["…"], WinLines = ["Inevitable."], LossLines = ["Impossible."], DrawLines = ["Acceptable."],
        },
    ];
}
