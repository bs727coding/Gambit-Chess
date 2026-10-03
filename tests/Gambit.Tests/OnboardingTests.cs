using Gambit.Engine.Bots;
using Gambit.ViewModels;

namespace Gambit.Tests;

public class OnboardingTests
{
    [Fact]
    public void Each_level_starts_higher_than_the_last()
    {
        IReadOnlyList<ExperienceOption> options = Onboarding.Options;
        Assert.Equal(Enum.GetValues<ExperienceLevel>().Length, options.Count);
        Assert.All(options, o => Assert.Equal(o.BotId, o.Bot.Id)); // BotRoster.Get falls back to the first bot for unknown ids
        for (int i = 1; i < options.Count; i++)
        {
            Assert.True(options[i].Bot.Rating > options[i - 1].Bot.Rating, options[i].Title);
            Assert.True(options[i].PuzzleRating > options[i - 1].PuzzleRating, options[i].Title);
        }
        Assert.Equal(BotRoster.Bots[0], Onboarding.For(ExperienceLevel.NewToChess).Bot);
    }

    [Fact]
    public void Only_new_profiles_see_the_welcome_screen()
    {
        Assert.True(Onboarding.IsNeeded(onboarded: false, gamesPlayed: 0, puzzleAttempts: 0, lessonsDone: 0));
        Assert.False(Onboarding.IsNeeded(onboarded: true, gamesPlayed: 0, puzzleAttempts: 0, lessonsDone: 0));
        // Profiles from before the welcome screen existed already have history.
        Assert.False(Onboarding.IsNeeded(onboarded: false, gamesPlayed: 3, puzzleAttempts: 0, lessonsDone: 0));
        Assert.False(Onboarding.IsNeeded(onboarded: false, gamesPlayed: 0, puzzleAttempts: 5, lessonsDone: 0));
        Assert.False(Onboarding.IsNeeded(onboarded: false, gamesPlayed: 0, puzzleAttempts: 0, lessonsDone: 1));
    }

    [Fact]
    public void Next_bot_starts_at_the_players_level_and_moves_up()
    {
        Assert.Equal("acorn", Onboarding.NextBot(null, _ => false).Id);
        Assert.Equal("ember", Onboarding.NextBot(ExperienceLevel.Casual, _ => false).Id);
        Assert.Equal("flint", Onboarding.NextBot(ExperienceLevel.Casual, id => id == "ember").Id);
        Assert.Equal("ember", Onboarding.NextBot(ExperienceLevel.Casual, id => id == "acorn").Id);
        Assert.Equal(BotRoster.Bots[^1].Id, Onboarding.NextBot(ExperienceLevel.Strong, _ => true).Id);
    }

    [Fact]
    public void Summary_names_the_bot_and_the_puzzle_rating()
    {
        foreach (ExperienceOption o in Onboarding.Options)
        {
            string text = Onboarding.Summary(o);
            Assert.Contains(o.Bot.Name, text);
            Assert.Contains(o.PuzzleRating.ToString(), text);
        }
    }
}
