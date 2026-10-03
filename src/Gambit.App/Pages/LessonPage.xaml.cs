using Gambit.App.Controls;
using Gambit.App.Helpers;
using Gambit.App.Services;
using Gambit.Core.Board;
using Gambit.Core.Lessons;
using Gambit.Core.Notation;
using Gambit.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace Gambit.App.Pages;

/// <summary>Opens the lesson page as an opening review: one due line per step.</summary>
public sealed record OpeningReviewRequest(IReadOnlyList<ReviewLine> Lines);

/// <summary>
/// Plays one lesson step by step: explanations, star-collecting games, move tasks and quizzes. Also
/// runs opening reviews (<see cref="OpeningReviewRequest"/>), recording each line as it is finished.
/// </summary>
public sealed partial class LessonPage : Page
{
    private Course? _course;
    private Lesson? _lesson;
    private int _stepIndex;
    private LessonStep? _step;
    private Position _pos = new();
    private int _moveIndex;
    private bool _stepDone;
    private int _mistakes;
    private int _generation;
    private readonly List<int> _remainingTargets = [];

    // Opening review: the lines, and how the current one is going (a restart doesn't wipe a slip).
    private IReadOnlyList<ReviewLine>? _review;
    private int _stepMistakes;
    private bool _stepHinted;
    private bool _stepRecorded;
    private int _cleanLines;

    public LessonPage()
    {
        InitializeComponent();
        ApplySettings();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _review = null;
        if (e.Parameter is string key && LessonCatalog.Find(key) is var (course, lesson))
        {
            _course = course;
            _lesson = lesson;
        }
        else if (e.Parameter is OpeningReviewRequest { Lines.Count: > 0 } request)
        {
            _review = request.Lines;
            _course = new Course("review", "Opening review", "", "", "", []);
            _lesson = new Lesson("review", "Opening review", "", [.. request.Lines.Select(l => l.Step)]) { Key = "review" };
            _cleanLines = 0;
        }
        else
        {
            return;
        }
        _mistakes = 0;
        ResetStepRecord();
        ApplySettings();
        ShowStep(0);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _generation++;
    }

    private void ShowStep(int index)
    {
        if (_lesson == null || _course == null) return;
        _generation++;
        _stepIndex = index;
        _step = _lesson.Steps[index];
        _moveIndex = 0;
        _stepDone = false;

        CourseTitle.Text = _course.Title;
        LessonTitle.Text = _review != null ? _review[index].Lesson.Title : _lesson.Title;
        StepProgress.Value = (double)index / _lesson.Steps.Count;
        StepCounter.Text = $"{index + 1} / {_lesson.Steps.Count}";
        StepText.Text = _step.Text;
        FeedbackBox.Visibility = Visibility.Collapsed;
        Choices.Children.Clear();

        _pos = Position.FromFen(_step.Fen);
        Board.Flipped = _step.Kind is StepKind.Moves or StepKind.Stars && _pos.SideToMove == Color.Black;
        Board.SetPosition(_pos);

        _remainingTargets.Clear();
        foreach (string t in _step.Targets) _remainingTargets.Add(Square.Parse(t));

        switch (_step.Kind)
        {
            case StepKind.Info:
                Board.Interaction = BoardInteraction.None;
                _stepDone = true;
                break;
            case StepKind.Stars:
                Board.Interaction = _pos.SideToMove == Color.White ? BoardInteraction.White : BoardInteraction.Black;
                break;
            case StepKind.Moves:
                Board.Interaction = _pos.SideToMove == Color.White ? BoardInteraction.White : BoardInteraction.Black;
                break;
            case StepKind.Quiz:
                Board.Interaction = BoardInteraction.None;
                BuildChoices();
                break;
        }

        HintButton.Visibility = _step.Kind is StepKind.Moves or StepKind.Stars ? Visibility.Visible : Visibility.Collapsed;
        RestartButton.Visibility = _step.Kind is StepKind.Moves or StepKind.Stars ? Visibility.Visible : Visibility.Collapsed;
        UpdateMarkers();
        UpdateContinue();
        FocusStep();
    }

    /// <summary>
    /// Keeps keyboard focus where the next action is: the board or the answers while a step is open,
    /// Continue once it's done. (Left alone, focus falls from the disabled Continue button to
    /// "All lessons", and the next Enter leaves the lesson.)
    /// </summary>
    private void FocusStep()
    {
        if (_stepDone) ContinueButton.Focus(FocusState.Programmatic);
        else if (_step?.Kind == StepKind.Quiz && Choices.Children.FirstOrDefault() is Control first) first.Focus(FocusState.Programmatic);
        else Board.Focus(FocusState.Programmatic);
    }

    private void UpdateMarkers()
    {
        if (_step == null) return;
        var markers = new List<BoardMarker>();
        var arrowColor = Ui.ParseColor("#4A90E2", 200);
        var highlight = Ui.ParseColor("#FFB300", 200);
        if (!_stepDone || _step.Kind == StepKind.Info)
        {
            foreach (string a in _step.Arrows)
                markers.Add(BoardMarker.Arrow(Square.Parse(a.AsSpan(0, 2)), Square.Parse(a.AsSpan(2, 2)), arrowColor));
            foreach (string h in _step.Highlights) markers.Add(BoardMarker.Square(Square.Parse(h), highlight));
        }
        foreach (int t in _remainingTargets) markers.Add(BoardMarker.Square(t, Ui.ParseColor("#FFC107", 235)));
        Board.SetMarkers(markers);
    }

    private void UpdateContinue()
    {
        if (_lesson == null) return;
        bool last = _stepIndex == _lesson.Steps.Count - 1;
        ContinueButton.IsEnabled = _stepDone;
        ContinueText.Text = _review != null ? (last ? "Finish review" : "Next line")
            : !last ? "Continue" : LessonCatalog.Next(_lesson.Key) == null ? "Finish" : "Finish lesson";
    }

    // ------------------------------------------------------------------ interactions

    private void Board_MoveRequested(object? sender, BoardMoveEventArgs e)
    {
        if (_step == null || _stepDone) return;
        switch (_step.Kind)
        {
            case StepKind.Stars:
                HandleStarsMove(e.Move);
                break;
            case StepKind.Moves:
                HandleLineMove(e.Move);
                break;
        }
    }

    private void HandleStarsMove(Move move)
    {
        Color mover = _pos.SideToMove;
        _pos.MakeMove(move);
        bool hit = _remainingTargets.Remove(move.To);
        SoundService.Play(hit ? GameSound.Capture : GameSound.Move);

        // The other side never moves in star games: hand the turn straight back.
        string[] fen = _pos.ToFen().Split(' ');
        fen[1] = mover == Color.White ? "w" : "b";
        fen[2] = "-";
        fen[3] = "-";
        _pos = Position.FromFen(string.Join(' ', fen));
        Board.SetPosition(_pos, move, animate: true);

        if (_remainingTargets.Count == 0)
        {
            CompleteStep(_step!.Success ?? "All stars collected!");
            Board.Interaction = BoardInteraction.None;
        }
        UpdateMarkers();
    }

    private void HandleLineMove(Move move)
    {
        LessonStep step = _step!;
        bool goalMet = step.Goal switch
        {
            "mate" => GivesMate(move),
            "check" => GivesCheck(move),
            _ => false,
        };
        Move expected = _moveIndex < step.Moves.Count && San.TryParse(_pos, step.Moves[_moveIndex], out Move m) ? m : Move.None;
        bool correct = goalMet || move == expected;

        if (!correct)
        {
            _mistakes++;
            _stepMistakes++;
            SoundService.Play(GameSound.Illegal);
            ShowFeedback(false, "Not quite — try again.");
            return;
        }

        FeedbackBox.Visibility = Visibility.Collapsed;
        PlayOnBoard(move);
        _moveIndex++;
        if (goalMet || _moveIndex >= step.Moves.Count)
        {
            Board.Interaction = BoardInteraction.None;
            CompleteStep(_review != null ? RecordReview() : step.Success ?? "Well done!");
            return;
        }

        // Scripted reply.
        Board.Interaction = BoardInteraction.None;
        int gen = _generation;
        Delay.Run(DispatcherQueue, TimeSpan.FromMilliseconds(500), () =>
        {
            if (gen != _generation || _step != step) return;
            if (San.TryParse(_pos, step.Moves[_moveIndex], out Move reply))
            {
                PlayOnBoard(reply);
                _moveIndex++;
            }
            Board.Interaction = _pos.SideToMove == Color.White ? BoardInteraction.White : BoardInteraction.Black;
        });
    }

    private void BuildChoices()
    {
        if (_step == null) return;
        for (int i = 0; i < _step.Choices.Count; i++)
        {
            int choice = i;
            var button = new Button
            {
                Content = new TextBlock { Text = _step.Choices[i], TextWrapping = TextWrapping.Wrap },
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(14, 8, 14, 8),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, _step.Choices[i]);
            button.Click += (_, _) =>
            {
                if (_stepDone) return;
                if (choice == _step.Answer)
                {
                    SoundService.Play(GameSound.Promote);
                    CompleteStep(_step.Success ?? "Correct!");
                }
                else
                {
                    _mistakes++;
                    SoundService.Play(GameSound.Illegal);
                    ShowFeedback(false, "Not quite — have another look at the position.");
                }
            };
            Choices.Children.Add(button);
        }
    }

    /// <summary>Saves the finished review line's result; returns the feedback to show.</summary>
    private string RecordReview()
    {
        bool clean = _stepMistakes == 0 && !_stepHinted;
        if (_review == null || _stepRecorded) return clean ? "Correct!" : "Line complete.";
        _stepRecorded = true;
        if (clean) _cleanLines++;

        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        ReviewLine line = _review[_stepIndex];
        Dictionary<string, ReviewState> reviews = App.Profile.Profile.Reviews;
        ReviewState state = OpeningReview.Record(reviews.GetValueOrDefault(line.Id), clean, today);
        reviews[line.Id] = state;
        App.Profile.Save();
        return clean
            ? $"Perfect. This line comes back {OpeningReview.When(state.Due, today)}."
            : "Line complete. It comes back tomorrow, to make it stick.";
    }

    private void ResetStepRecord()
    {
        _stepMistakes = 0;
        _stepHinted = false;
        _stepRecorded = false;
    }

    private void CompleteStep(string message)
    {
        _stepDone = true;
        ShowFeedback(true, message);
        UpdateMarkers();
        UpdateContinue();
        FocusStep();
    }

    private void ShowFeedback(bool good, string text)
    {
        FeedbackBox.Visibility = Visibility.Visible;
        FeedbackBox.Background = new SolidColorBrush(Ui.ParseColor(good ? "#2E7D32" : "#C62828"));
        FeedbackIcon.Glyph = good ? "" : "";
        FeedbackText.Text = text;
    }

    private void PlayOnBoard(Move move)
    {
        string san = San.Format(_pos, move);
        _pos.MakeMove(move);
        SoundService.Play(san.EndsWith('+') || san.EndsWith('#') ? GameSound.Check
            : move.IsCastle ? GameSound.Castle : move.IsCapture ? GameSound.Capture : GameSound.Move);
        Board.SetPosition(_pos, move, animate: true);
    }

    private bool GivesMate(Move move)
    {
        var p = _pos.Clone();
        p.MakeMove(move);
        return p.InCheck && !MoveGenerator.HasLegalMove(p);
    }

    private bool GivesCheck(Move move)
    {
        var p = _pos.Clone();
        p.MakeMove(move);
        return p.InCheck;
    }

    // ------------------------------------------------------------------ buttons

    private void Hint_Click(object sender, RoutedEventArgs e)
    {
        if (_step == null || _stepDone) return;
        _stepHinted = true;
        var green = Ui.ParseColor("#81B64C", 220);
        if (_step.Kind == StepKind.Moves && _moveIndex < _step.Moves.Count && San.TryParse(_pos, _step.Moves[_moveIndex], out Move m))
        {
            Board.SetMarkers([BoardMarker.Square(m.From, green), BoardMarker.Arrow(m.From, m.To, green)]);
        }
        ShowFeedback(true, _step.Hint ?? (_step.Kind == StepKind.Stars ? "Collect the stars in any order." : "Follow the arrow."));
        FeedbackBox.Background = new SolidColorBrush(Ui.ParseColor("#5C6BC0"));
        FeedbackIcon.Glyph = "";
    }

    private void Restart_Click(object sender, RoutedEventArgs e) => ShowStep(_stepIndex);

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (_lesson == null || !_stepDone) return;
        if (_stepIndex + 1 < _lesson.Steps.Count)
        {
            ResetStepRecord();
            ShowStep(_stepIndex + 1);
            return;
        }
        if (_review != null) FinishReview();
        else FinishLesson();
    }

    private async void FinishLesson()
    {
        if (_lesson == null) return;
        int stars = _mistakes == 0 ? 3 : _mistakes <= 2 ? 2 : 1;
        var lessons = App.Profile.Profile.Lessons;
        if (!lessons.TryGetValue(_lesson.Key, out LessonResult? result) || result.Stars < stars)
            lessons[_lesson.Key] = new LessonResult { Stars = stars };
        App.Profile.Save();
        SoundService.Play(GameSound.Win);
        AchievementService.Instance.CheckProfile();

        var next = LessonCatalog.Next(_lesson.Key);
        var dialog = new ContentDialog
        {
            Title = "Lesson complete!",
            Content = $"{new string('★', stars)}{new string('☆', 3 - stars)}   {(stars == 3 ? "Perfect — no mistakes." : $"{_mistakes} mistake{(_mistakes == 1 ? "" : "s")}.")}",
            PrimaryButtonText = next != null ? $"Next: {next.Value.Lesson.Title}" : "",
            CloseButtonText = "All lessons",
            DefaultButton = next != null ? ContentDialogButton.Primary : ContentDialogButton.Close,
        };
        ContentDialogResult r = await Dialogs.ShowAsync(dialog, XamlRoot);
        if (r == ContentDialogResult.Primary && next != null) App.Window.Navigate(typeof(LessonPage), next.Value.Lesson.Key, "learn");
        else App.Window.Navigate(typeof(LearnPage), null, "learn");
    }

    private async void FinishReview()
    {
        if (_review == null) return;
        SoundService.Play(GameSound.Win);
        AchievementService.Instance.CheckProfile();
        int total = _review.Count;
        string summary = (total, _cleanLines) switch
        {
            (1, 1) => "No slips. The line comes back a little later next time.",
            (1, _) => "There was a slip, so the line comes back tomorrow.",
            (_, 0) => $"Each of the {total} lines had a slip, so they all come back tomorrow.",
            _ when _cleanLines == total => $"All {total} lines without a slip. Each comes back a little later next time.",
            _ => $"{_cleanLines} of {total} lines without a slip. The others come back tomorrow.",
        };
        var dialog = new ContentDialog
        {
            Title = "Review complete",
            Content = summary,
            CloseButtonText = "Back to Learn",
            DefaultButton = ContentDialogButton.Close,
        };
        await Dialogs.ShowAsync(dialog, XamlRoot);
        App.Window.Navigate(typeof(LearnPage), null, "learn");
    }

    private void Back_Click(object sender, RoutedEventArgs e) => App.Window.Navigate(typeof(LearnPage), null, "learn");

    private void ApplySettings() => Board.ApplyUserSettings();
}
