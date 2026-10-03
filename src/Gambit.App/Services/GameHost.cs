using Gambit.Core.Games;
using Gambit.ViewModels;

namespace Gambit.App.Services;

/// <summary>Connects <see cref="GameViewModel"/> to the app: the profile, achievements, the saved game and sounds.</summary>
public sealed class GameHost : IGameHost
{
    public string AppName => AppInfo.DisplayName;

    public string PlayerName => App.Profile.Profile.Name;

    public bool PremovesEnabled => App.Settings.Current.Premoves;

    public void SaveUnfinished(SavedGame game) => ActiveGameStore.Save(game);

    public void ClearUnfinished() => ActiveGameStore.Clear();

    public void RecordFinished(FinishedGame f)
    {
        App.Profile.RecordGame(f.Game, f.Opponent, f.Bot?.Id, f.OpponentRating, f.PlayerColor, f.TimeControl);
        AchievementService.Instance.OnGameFinished(f.Game, f.PlayerColor, f.Bot, f.TimeControl);
    }

    public (int Wins, int Losses, int Draws) RecordAgainst(string botId)
    {
        BotRecord record = App.Profile.RecordAgainst(botId);
        return (record.Wins, record.Losses, record.Draws);
    }

    public void PlayMoveSound(GameMove move) => SoundService.PlayFor(move);

    public void Play(GameCue cue) => SoundService.Play(cue switch
    {
        GameCue.Start => GameSound.Start,
        GameCue.Win => GameSound.Win,
        GameCue.LowTime => GameSound.LowTime,
        _ => GameSound.End,
    });
}
