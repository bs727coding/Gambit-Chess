using Gambit.App.Controls;
using Gambit.App.Helpers;
using Gambit.App.Services;
using Gambit.App.Theming;
using Gambit.Core.Board;
using Gambit.Core.Games;
using Gambit.Core.Notation;
using Gambit.Core.Puzzles;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace Gambit.App.Pages;

public sealed record PuzzleRequest(PuzzleMode Mode, string? Theme = null);

/// <summary>Solves puzzles: rated, daily, by theme, and Puzzle Rush (3 min, 5 min, survival).</summary>
public sealed partial class PuzzleSolvePage : Page
{
    private PuzzleRequest _request = new(PuzzleMode.Rated);
    private Puzzle? _puzzle;
    private Game? _game;
    private int _index;          // index in Puzzle.Moves of the next expected move
    private bool _failed;
    private bool _finished;
    private bool _recorded;
    private int _hintLevel;
    private int _generation;     // guards delayed actions against puzzle changes

    // Rush state
    private List<Puzzle> _ladder = [];
    private int _rushIndex;
    private int _rushScore;
    private int _strikes;
    private DateTime _rushEnd;
    private bool _rushOver;
    private readonly DispatcherTimer _rushTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    public PuzzleSolvePage()
    {
        InitializeComponent();
        _rushTimer.Tick += (_, _) => UpdateRushTimer();
        ApplySettings();
    }

    private bool IsRush => _request.Mode is PuzzleMode.Rush3 or PuzzleMode.Rush5 or PuzzleMode.Survival;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _request = e.Parameter as PuzzleRequest ?? new PuzzleRequest(PuzzleMode.Rated);
        StartMode();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _rushTimer.Stop();
        _generation++;
    }

    private void StartMode()
    {
        ApplySettings();
        _rushOver = false;
        RushPanel.Visibility = IsRush ? Visibility.Visible : Visibility.Collapsed;
        RatingCard.Visibility = IsRush ? Visibility.Collapsed : Visibility.Visible;
        HintButton.Visibility = SolutionButton.Visibility = RetryButton.Visibility = IsRush ? Visibility.Collapsed : Visibility.Visible;

        (ModeTitle.Text, ModeSubtitle.Text) = _request.Mode switch
        {
            PuzzleMode.Daily => ("Daily puzzle", DateTime.Now.ToString("dddd, MMMM d")),
            PuzzleMode.Theme => (PuzzleThemes.Name(_request.Theme ?? ""), "Theme practice — your rating still counts"),
            PuzzleMode.Rush3 => ("Puzzle Rush · 3 min", "Solve as many as you can. Three strikes and you're out."),
            PuzzleMode.Rush5 => ("Puzzle Rush · 5 min", "Solve as many as you can. Three strikes and you're out."),
            PuzzleMode.Survival => ("Puzzle Survival", "No clock — keep going until your third mistake."),
            _ => ("Rated puzzles", "Puzzles adapt to your rating."),
        };

        if (IsRush)
        {
            _ladder = PuzzleService.Instance.RushLadder();
            _rushIndex = 0;
            _rushScore = 0;
            _strikes = 0;
            _rushEnd = DateTime.UtcNow + (_request.Mode == PuzzleMode.Rush5 ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(3));
            if (_request.Mode != PuzzleMode.Survival) _rushTimer.Start();
            UpdateRushPanel();
        }
        LoadNext();
    }

    private void LoadNext()
    {
        Puzzle? next = _request.Mode switch
        {
            PuzzleMode.Daily => PuzzleService.Instance.Daily(DateOnly.FromDateTime(DateTime.Now)),
            PuzzleMode.Theme => PuzzleService.Instance.NextRated(_request.Theme),
            _ when IsRush => _rushIndex < _ladder.Count ? _ladder[_rushIndex++] : null,
            _ => PuzzleService.Instance.NextRated(),
        };
        if (next == null)
        {
            SetStatus("", "#808080", "No puzzles available", "The puzzle collection is empty or exhausted.");
            return;
        }
        Load(next);
    }

    private void Load(Puzzle puzzle)
    {
        int gen = ++_generation;
        _puzzle = puzzle;
        _game = new Game(puzzle.Fen) { AutoDrawRules = false };
        _index = 0;
        _failed = false;
        _finished = false;
        _recorded = false;
        _hintLevel = 0;

        Board.Flipped = puzzle.SolverColor == Color.Black;
        Board.Interaction = BoardInteraction.None;
        Board.SetMarkers([]);
        Board.SetPosition(_game.Position);
        UpdateRatingCard(reveal: false, delta: null);
        NextButton.Visibility = IsRush || _request.Mode == PuzzleMode.Daily ? Visibility.Collapsed : Visibility.Visible;
        SetNextButton(skip: true);
        SetStatus("", "#5C6BC0", "Watch the opponent's move…", "");

        After(550, gen, () =>
        {
            PlayMove(_puzzle!.Moves[0], animate: true);
            _index = 1;
            Board.Interaction = puzzle.SolverColor == Color.White ? BoardInteraction.White : BoardInteraction.Black;
            SetStatus("", puzzle.SolverColor == Color.White ? "#E8E8E8" : "#303030",
                $"Your turn", $"Find the best move for {puzzle.SolverColor.Name()}.", darkGlyph: puzzle.SolverColor == Color.White);
        });
    }

    private void Board_MoveRequested(object? sender, BoardMoveEventArgs e)
    {
        if (_puzzle == null || _game == null || _finished || _index >= _puzzle.Moves.Count || _index % 2 == 0) return;
        string expected = _puzzle.Moves[_index];
        bool correct = e.Move.ToUci() == expected || DeliversMate(e.Move);

        if (!correct)
        {
            SoundService.Play(GameSound.Illegal);
            Board.SetMarkers([BoardMarker.Square(e.Move.To, Ui.ParseColor("#E5484D", 220))]);
            Fail();
            if (IsRush)
            {
                _strikes++;
                UpdateRushPanel();
                _finished = true;
                Board.Interaction = BoardInteraction.None;
                int gen = _generation;
                if (_strikes >= 3) After(700, gen, EndRush);
                else After(900, gen, LoadNext);
            }
            else
            {
                SetStatus("", "#E5484D", "That's not it", "Try again, or press Solution to see the answer.");
            }
            return;
        }

        Board.SetMarkers([]);
        PlayMove(e.Move.ToUci(), animate: !e.Dragged);
        _index++;
        if (_index >= _puzzle.Moves.Count || DeliversMateAlready())
        {
            Solved();
            return;
        }

        SetStatus("", "#81B64C", "Correct!", "Keep going…");
        Board.Interaction = BoardInteraction.None;
        int g = _generation;
        After(450, g, () =>
        {
            PlayMove(_puzzle.Moves[_index], animate: true);
            _index++;
            Board.Interaction = _puzzle.SolverColor == Color.White ? BoardInteraction.White : BoardInteraction.Black;
            SetStatus("", "#81B64C", "Correct! Keep going", $"Find the next move for {_puzzle.SolverColor.Name()}.");
        });
    }

    private void Solved()
    {
        if (_puzzle == null) return;
        _finished = true;
        Board.Interaction = BoardInteraction.None;
        SoundService.Play(GameSound.Promote);

        if (IsRush)
        {
            if (!_failed) _rushScore++;
            UpdateRushPanel();
            After(350, _generation, LoadNext);
            return;
        }

        int? delta = null;
        if (!_recorded)
        {
            _recorded = true;
            delta = Record(true);
        }
        SetStatus("", "#81B64C", _failed ? "Solved — after a slip" : "Puzzle solved!", _failed ? "No rating gain this time." : "Nicely done.");
        UpdateRatingCard(reveal: true, delta);
        NextButton.Visibility = _request.Mode == PuzzleMode.Daily ? Visibility.Collapsed : Visibility.Visible;
        SetNextButton(skip: false);
    }

    private void Fail()
    {
        if (_failed || _puzzle == null) return;
        _failed = true;
        if (IsRush || _recorded) return;
        _recorded = true;
        int? delta = Record(false);
        UpdateRatingCard(reveal: false, delta);
    }

    /// <summary>Records the outcome for the current mode; returns the rating change for rated modes.</summary>
    private int? Record(bool solved)
    {
        if (_puzzle == null) return null;
        switch (_request.Mode)
        {
            case PuzzleMode.Rated:
            case PuzzleMode.Theme:
                return PuzzleService.Instance.RecordRated(_puzzle, solved);
            case PuzzleMode.Daily:
                PuzzleService.Instance.RecordDaily(DateOnly.FromDateTime(DateTime.Now), solved);
                PuzzleService.Instance.RecordUnrated(_puzzle, solved);
                return null;
            default:
                PuzzleService.Instance.RecordUnrated(_puzzle, solved);
                return null;
        }
    }

    /// <summary>"Skip" is a quiet button while solving; afterwards it becomes the accented "Next puzzle".</summary>
    private void SetNextButton(bool skip)
    {
        NextText.Text = skip ? "Skip" : "Next puzzle";
        NextButton.Style = (Style)Application.Current.Resources[skip ? "DefaultButtonStyle" : "AccentButtonStyle"];
    }

    // ------------------------------------------------------------------ buttons

    private void Hint_Click(object sender, RoutedEventArgs e)
    {
        if (_puzzle == null || _game == null || _finished || _index % 2 == 0 || _index >= _puzzle.Moves.Count) return;
        Move expected = Uci.Parse(_game.Position, _puzzle.Moves[_index]);
        if (expected.IsNone) return;
        var green = Ui.ParseColor("#81B64C", 220);
        _hintLevel++;
        Board.SetMarkers(_hintLevel == 1
            ? [BoardMarker.Square(expected.From, green)]
            : [BoardMarker.Square(expected.From, green), BoardMarker.Arrow(expected.From, expected.To, green)]);
        Fail();
        SetStatus("", "#F7C045", _hintLevel == 1 ? "Hint: this piece moves" : "Hint: play this move", "Hints count as a miss for your rating.");
    }

    private async void Solution_Click(object sender, RoutedEventArgs e)
    {
        if (_puzzle == null || _game == null || _finished || _index == 0) return;
        Fail();
        _finished = true;
        Board.Interaction = BoardInteraction.None;
        Board.SetMarkers([]);
        int gen = _generation;
        SetStatus("", "#5C8BB0", "Solution", string.Join(" ", SolutionSan()));
        while (_index < _puzzle.Moves.Count)
        {
            await Task.Delay(550);
            if (gen != _generation) return;
            PlayMove(_puzzle.Moves[_index], animate: true);
            _index++;
        }
        UpdateRatingCard(reveal: true, null);
        SetNextButton(skip: false);
        NextButton.Visibility = _request.Mode == PuzzleMode.Daily ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        if (_puzzle == null) return;
        bool recorded = _recorded, failed = _failed;
        Load(_puzzle);
        // A retry never changes the rating again.
        _recorded = recorded || failed;
        _failed = failed;
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (!_finished && !_recorded && _puzzle != null && _index > 0 && _request.Mode is PuzzleMode.Rated or PuzzleMode.Theme)
            Fail(); // skipping a rated puzzle counts as a miss
        LoadNext();
    }

    private void Analysis_Click(object sender, RoutedEventArgs e)
    {
        if (_puzzle == null) return;
        App.Window.Navigate(typeof(AnalysisPage), new AnalysisRequest(Fen: _puzzle.StartPosition().ToFen()), "analysis");
    }

    // ------------------------------------------------------------------ rush

    private void UpdateRushTimer()
    {
        TimeSpan left = _rushEnd - DateTime.UtcNow;
        if (left <= TimeSpan.Zero)
        {
            RushTimer.Text = "0:00";
            EndRush();
            return;
        }
        RushTimer.Text = $"{(int)left.TotalMinutes}:{left.Seconds:00}";
    }

    private void UpdateRushPanel()
    {
        RushScore.Text = $"Score {_rushScore}";
        RushStrikes.Text = new string('✕', _strikes);
        if (_request.Mode == PuzzleMode.Survival) RushTimer.Text = $"{_rushScore}";
    }

    private async void EndRush()
    {
        if (_rushOver) return;
        _rushOver = true;
        _rushTimer.Stop();
        _finished = true;
        _generation++;
        Board.Interaction = BoardInteraction.None;
        bool best = PuzzleService.Instance.RecordRush(_request.Mode, _rushScore);
        SoundService.Play(GameSound.End);
        SetStatus("", "#5C6BC0", $"Rush over — score {_rushScore}", best ? "New personal best!" : "Try again to beat your best.");

        var dialog = new ContentDialog
        {
            Title = best ? "New personal best!" : "Puzzle Rush over",
            Content = $"You solved {_rushScore} puzzle{(_rushScore == 1 ? "" : "s")}.",
            PrimaryButtonText = "Play again",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await Dialogs.ShowAsync(dialog, XamlRoot) == ContentDialogResult.Primary) StartMode();
    }

    // ------------------------------------------------------------------ helpers

    private void PlayMove(string uci, bool animate)
    {
        if (_game == null) return;
        Move m = Uci.Parse(_game.Position, uci);
        if (m.IsNone) return;
        GameMove gm = _game.Play(m);
        SoundService.PlayFor(gm);
        Board.SetPosition(_game.Position, m, animate);
    }

    private bool DeliversMate(Move move)
    {
        if (_game == null) return false;
        var pos = _game.Position.Clone();
        pos.MakeMove(move);
        return pos.InCheck && !MoveGenerator.HasLegalMove(pos);
    }

    private bool DeliversMateAlready() => _game != null && _game.Position.InCheck && !MoveGenerator.HasLegalMove(_game.Position);

    private List<string> SolutionSan()
    {
        if (_puzzle == null || _game == null) return [];
        var pos = _game.Position.Clone();
        var list = new List<string>();
        for (int i = _index; i < _puzzle.Moves.Count; i++)
        {
            Move m = Uci.Parse(pos, _puzzle.Moves[i]);
            if (m.IsNone) break;
            list.Add(San.Format(pos, m));
            pos.MakeMove(m);
        }
        return list;
    }

    private void UpdateRatingCard(bool reveal, int? delta)
    {
        PuzzleProfile p = PuzzleService.Instance.Profile;
        RatingText.Text = $"{Math.Round(p.Rating):0}{(p.Deviation > 110 ? "?" : "")}";
        if (delta is int d)
        {
            RatingDelta.Text = d >= 0 ? $"+{d}" : $"{d}";
            RatingDelta.Foreground = new SolidColorBrush(d >= 0 ? Ui.ParseColor("#81B64C") : Ui.ParseColor("#E5484D"));
        }
        else if (!reveal)
        {
            RatingDelta.Text = "";
        }
        PuzzleRating.Text = reveal && _puzzle != null ? _puzzle.Rating.ToString() : "?";
        ThemesText.Text = reveal && _puzzle != null
            ? string.Join(" · ", _puzzle.Themes.Where(t => t is not ("crushing" or "advantage" or "oneMove" or "short" or "long")).Select(PuzzleThemes.Name))
            : "Themes are revealed after you solve it.";
    }

    private void SetStatus(string glyph, string hex, string title, string detail, bool darkGlyph = false)
    {
        StatusIcon.Glyph = glyph;
        StatusIcon.Foreground = new SolidColorBrush(darkGlyph ? Colors.Black : Colors.White);
        StatusBadge.Background = new SolidColorBrush(Ui.ParseColor(hex));
        StatusTitle.Text = title;
        StatusDetail.Text = detail;
    }

    private void After(int ms, int generation, Action action)
    {
        Delay.Run(DispatcherQueue, TimeSpan.FromMilliseconds(ms), () =>
        {
            if (generation == _generation) action();
        });
    }

    private void ApplySettings()
    {
        AppSettings s = App.Settings.Current;
        Board.Theme = BoardThemes.Get(s.BoardTheme);
        if (Board.PieceSet.Id != s.PieceSet) Board.PieceSet = PieceSets.Get(s.PieceSet);
        Board.ShowLegalMoves = s.ShowLegalMoves;
        Board.ShowCoordinates = s.ShowCoordinates;
        Board.HighlightLastMove = s.HighlightLastMove;
        Board.AnimateMoves = s.AnimateMoves;
        Board.AutoQueen = s.AutoQueen;
    }
}
