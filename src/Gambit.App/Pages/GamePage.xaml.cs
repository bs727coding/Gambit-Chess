using Gambit.App.Controls;
using Gambit.App.Helpers;
using Gambit.App.Services;
using Gambit.App.Theming;
using Gambit.Core.Board;
using Gambit.Core.Games;
using Gambit.Core.Openings;
using Gambit.Core.Sessions;
using Gambit.Engine.Bots;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace Gambit.App.Pages;

/// <summary>
/// Plays one game through an <see cref="IGameSession"/>. The page never asks whether the opponent is
/// a bot: it renders session state and forwards the user's moves. (Online games will reuse it.)
/// </summary>
public sealed partial class GamePage : Page
{
    private static GamePage? _instance;

    private LocalGameSession? _session;
    private GameSetup? _setup;
    private int _viewPly = -1; // -1 = following the live position
    private bool _recorded;
    private bool _lowTimeWarned;
    private string _appliedTheme = "";
    private string _appliedPieces = "";
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4) };

    public GamePage()
    {
        InitializeComponent();
        _instance = this;
        _clockTimer.Tick += (_, _) => UpdateClocks();
        _toastTimer.Tick += (_, _) =>
        {
            Toast.IsOpen = false;
            _toastTimer.Stop();
        };
        Board.BoardSizeChanged += (_, size) => TopBar.Width = BottomBar.Width = size;

        AddAccelerator(VirtualKey.Left, () => StepPly(-1));
        AddAccelerator(VirtualKey.Right, () => StepPly(+1));
        AddAccelerator(VirtualKey.Home, () => ShowPly(0));
        AddAccelerator(VirtualKey.End, () => ShowPly(int.MaxValue));
        AddAccelerator(VirtualKey.F, FlipBoard);

        App.Settings.Current.PropertyChanged += (_, _) => ApplySettings();
        ApplySettings();
    }

    /// <summary>True while an unfinished game exists (the Play tab returns to it).</summary>
    public static bool HasActiveGame => _instance?._session is { } s && !s.Game.IsOver;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is GameSetup setup) StartGame(setup);
        else if (_session == null) DispatcherQueue.TryEnqueue(() => App.Window.NavigateTo("play"));
    }

    // ------------------------------------------------------------------ game lifecycle

    private void StartGame(GameSetup setup)
    {
        EndSession();
        _setup = setup;
        _recorded = false;
        _viewPly = -1;
        PostGamePanel.Visibility = Visibility.Collapsed;
        ChatBubble.Visibility = Visibility.Collapsed;
        Toast.IsOpen = false;

        var game = new Game(setup.StartFen);
        var human = new PlayerInfo(App.Profile.Profile.Name, PlayerKind.LocalHuman) { Id = "me" };
        IMoveProvider? whiteBot = null, blackBot = null;
        PlayerInfo white, black;

        if (setup.Bot is BotProfile bp)
        {
            var bot = new BotMoveProvider(bp);
            var botInfo = new PlayerInfo(bp.Name, PlayerKind.Bot, bp.Rating) { Id = bp.Id, Subtitle = bp.Tagline, AvatarKey = bp.Id };
            if (setup.HumanColor == Color.White)
            {
                white = human;
                black = botInfo;
                blackBot = bot;
            }
            else
            {
                white = botInfo;
                black = human;
                whiteBot = bot;
            }
        }
        else
        {
            white = new PlayerInfo("White", PlayerKind.LocalHuman) { Id = "white" };
            black = new PlayerInfo("Black", PlayerKind.LocalHuman) { Id = "black" };
        }

        game.Tags["Event"] = setup.IsHotSeat ? "Pass and play" : "Casual game vs computer";
        game.Tags["Site"] = $"{AppInfo.DisplayName} for Windows";
        game.Tags["Date"] = DateTime.Now.ToString("yyyy.MM.dd");
        game.Tags["White"] = white.Name;
        game.Tags["Black"] = black.Name;
        if (white.Rating is int wr) game.Tags["WhiteElo"] = wr.ToString();
        if (black.Rating is int br) game.Tags["BlackElo"] = br.ToString();
        game.Tags["TimeControl"] = setup.TimeControl.PgnTag;

        _session = new LocalGameSession(game, white, black, whiteBot, blackBot, setup.TimeControl, setup.AllowTakebacks);
        _session.MovePlayed += Session_MovePlayed;
        _session.GameEnded += Session_GameEnded;
        _session.StateReset += Session_StateReset;
        _session.ThinkingChanged += Session_ThinkingChanged;
        _session.ChatReceived += Session_ChatReceived;
        _session.DrawOfferAnswered += Session_DrawOfferAnswered;

        Board.Flipped = !setup.IsHotSeat && setup.HumanColor == Color.Black;
        Board.SetPosition(game.Position);
        SetupPlayerBars();
        SetupOpponentCard();
        RefreshMoveList();

        _lowTimeWarned = false;
        SoundService.Play(GameSound.Start);
        _session.Start();
        if (_session.Clock != null) _clockTimer.Start();
        else _clockTimer.Stop();
        RefreshAll();
    }

    private void EndSession()
    {
        if (_session == null) return;
        _session.MovePlayed -= Session_MovePlayed;
        _session.GameEnded -= Session_GameEnded;
        _session.StateReset -= Session_StateReset;
        _session.ThinkingChanged -= Session_ThinkingChanged;
        _session.ChatReceived -= Session_ChatReceived;
        _session.DrawOfferAnswered -= Session_DrawOfferAnswered;
        _session.Dispose();
        _session = null;
        _clockTimer.Stop();
    }

    // ------------------------------------------------------------------ session events

    private void Session_MovePlayed(object? sender, MovePlayedEventArgs e)
    {
        _viewPly = -1;
        SoundService.PlayFor(e.Move);
        Board.SetPosition(_session!.Game.Position, e.Move.Move, animate: true);
        RefreshMoveList();
        RefreshAll();
    }

    private void Session_GameEnded(object? sender, GameEndedEventArgs e)
    {
        RecordResult();
        _clockTimer.Stop();
        SoundService.Play(_setup?.IsHotSeat == false && _session?.Game.Winner == _setup.HumanColor ? GameSound.Win : GameSound.End);
        PostGamePanel.Visibility = Visibility.Visible;
        RefreshAll();
        _ = ShowGameOverDialogAsync(e);
    }

    private void Session_StateReset(object? sender, EventArgs e)
    {
        _viewPly = -1;
        Game g = _session!.Game;
        Board.SetPosition(g.Position, g.LastMove?.Move ?? Move.None);
        RefreshMoveList();
        RefreshAll();
    }

    private void Session_ThinkingChanged(object? sender, EventArgs e) => RefreshAll();

    private void Session_ChatReceived(object? sender, ChatEventArgs e)
    {
        ChatText.Text = e.Text;
        ChatBubble.Visibility = Visibility.Visible;
    }

    private void Session_DrawOfferAnswered(object? sender, bool accepted)
    {
        if (!accepted) ShowToast("Draw declined", $"{OpponentDisplayName()} wants to keep playing.", InfoBarSeverity.Informational);
    }

    // ------------------------------------------------------------------ board + history

    private void Board_MoveRequested(object? sender, BoardMoveEventArgs e)
    {
        if (_session == null || _viewPly >= 0) return;
        _session.TrySubmitMove(e.Move);
    }

    private void MoveList_PlySelected(object? sender, int ply) => ShowPly(ply);

    private void StepPly(int delta)
    {
        if (_session == null) return;
        int current = _viewPly < 0 ? _session.Game.Moves.Count : _viewPly;
        ShowPly(current + delta);
    }

    private void ShowPly(int ply)
    {
        if (_session == null) return;
        Game g = _session.Game;
        int count = g.Moves.Count;
        ply = Math.Clamp(ply, 0, count);
        int current = _viewPly < 0 ? count : _viewPly;
        if (ply == current) return;

        bool forwardOne = ply == current + 1;
        _viewPly = ply == count ? -1 : ply;
        Position pos = ply == count ? g.Position : g.PositionAt(ply);
        Move last = ply > 0 ? g.Moves[ply - 1].Move : Move.None;
        Board.SetPosition(pos, last, animate: forwardOne);
        MoveList.Highlight(ply);
        RefreshAll();
    }

    // ------------------------------------------------------------------ UI refresh

    private void RefreshAll()
    {
        UpdateInteraction();
        UpdateCaptured();
        UpdateClocks();
        UpdateThinking();
        UpdateStatus();
        UpdateButtons();
        UpdateOpening();
    }

    private void UpdateOpening()
    {
        if (_session == null) return;
        Game g = _session.Game;
        Opening? o = OpeningBook.IdentifyAt(g, _viewPly < 0 ? g.Moves.Count : _viewPly);
        OpeningText.Text = o == null ? "" : $"{o.Eco} · {o.Name}";
    }

    private void RefreshMoveList()
    {
        if (_session == null) return;
        Game g = _session.Game;
        MoveList.SetMoves(g.Moves, _viewPly < 0 ? g.Moves.Count : _viewPly);
    }

    private void UpdateInteraction()
    {
        if (_session == null || _session.Game.IsOver || _viewPly >= 0 || _setup == null)
        {
            Board.Interaction = BoardInteraction.None;
            return;
        }
        Board.Interaction = _setup.IsHotSeat ? BoardInteraction.Both
            : _setup.HumanColor == Color.White ? BoardInteraction.White : BoardInteraction.Black;
    }

    private Color BottomColor => Board.Flipped ? Color.Black : Color.White;

    private void SetupPlayerBars()
    {
        ConfigureBar(BottomBar, BottomColor);
        ConfigureBar(TopBar, BottomColor.Opposite());
    }

    private void ConfigureBar(PlayerBar bar, Color side)
    {
        if (_session == null) return;
        PlayerInfo p = side == Color.White ? _session.White : _session.Black;
        if (p.Kind == PlayerKind.Bot)
        {
            BotProfile bp = BotRoster.Get(p.Id);
            bar.SetPlayer(bp.Name, bp.RatingText, bp.Monogram, bp.Color);
        }
        else
        {
            string monogram = p.Name.Length > 0 ? p.Name[..1].ToUpperInvariant() : "?";
            bar.SetPlayer(p.Name, null, monogram, side == Color.White ? "#6B7A8F" : "#2F3B4C");
        }
    }

    private void SetupOpponentCard()
    {
        OpponentAvatar.Children.Clear();
        if (_setup?.Bot is BotProfile bp)
        {
            OpponentAvatar.Children.Add(Ui.Avatar(bp.Monogram, bp.Color, 56));
            OpponentName.Text = $"{bp.Name} ({bp.RatingText})";
            OpponentTagline.Text = bp.Tagline;
        }
        else
        {
            OpponentAvatar.Children.Add(Ui.Avatar("⇄", "#5C6BC0", 56));
            OpponentName.Text = "Pass and play";
            OpponentTagline.Text = "Two players, one device. Take turns moving.";
        }
    }

    private void UpdateCaptured()
    {
        Position pos = Board.Position;
        int[] start = [0, 8, 2, 2, 2, 1, 0];
        var byWhite = new List<Piece>();
        var byBlack = new List<Piece>();
        int whiteMaterial = 0, blackMaterial = 0;
        for (int t = 1; t <= 5; t++)
        {
            var type = (PieceType)t;
            int w = pos.Count(Color.White, type), b = pos.Count(Color.Black, type);
            whiteMaterial += w * type.NominalValue();
            blackMaterial += b * type.NominalValue();
            for (int i = b; i < start[t]; i++) byWhite.Add(type.Of(Color.Black));
            for (int i = w; i < start[t]; i++) byBlack.Add(type.Of(Color.White));
        }
        PlayerBar whiteBar = BottomColor == Color.White ? BottomBar : TopBar;
        PlayerBar blackBar = BottomColor == Color.White ? TopBar : BottomBar;
        whiteBar.SetCaptured(byWhite, whiteMaterial - blackMaterial, Board.PieceSet);
        blackBar.SetCaptured(byBlack, blackMaterial - whiteMaterial, Board.PieceSet);
    }

    private void UpdateClocks()
    {
        ChessClock? clock = _session?.Clock;
        if (clock == null)
        {
            TopBar.SetClock(null, false);
            BottomBar.SetClock(null, false);
            return;
        }
        Color bottom = BottomColor;
        if (!_lowTimeWarned && _setup is { IsHotSeat: false } && clock.Running == _setup.HumanColor && clock.Remaining(_setup.HumanColor) < TimeSpan.FromSeconds(10))
        {
            _lowTimeWarned = true;
            SoundService.Play(GameSound.LowTime);
        }
        BottomBar.SetClock(clock.Remaining(bottom), clock.Running == bottom);
        TopBar.SetClock(clock.Remaining(bottom.Opposite()), clock.Running == bottom.Opposite());
    }

    private void UpdateThinking()
    {
        bool thinking = _session?.IsOpponentThinking == true;
        Color toMove = _session?.Game.SideToMove ?? Color.White;
        BottomBar.SetThinking(thinking && toMove == BottomColor);
        TopBar.SetThinking(thinking && toMove != BottomColor);
    }

    private void UpdateStatus()
    {
        if (_session == null)
        {
            StatusText.Text = "";
            return;
        }
        Game g = _session.Game;
        if (g.IsOver) StatusText.Text = g.ResultDescription;
        else if (_viewPly >= 0) StatusText.Text = "Viewing an earlier position";
        else if (_session.IsOpponentThinking) StatusText.Text = $"{OpponentDisplayName()} is thinking…";
        else if (_setup?.IsHotSeat == true) StatusText.Text = $"{g.SideToMove.Name()} to move";
        else StatusText.Text = g.Position.InCheck ? "Check! Your move" : "Your move";
    }

    private void UpdateButtons()
    {
        bool live = _session != null && !_session.Game.IsOver;
        TakebackButton.IsEnabled = live && _session!.CanTakeback;
        DrawButton.IsEnabled = live && _session!.CanOfferDraw;
        ResignButton.IsEnabled = live && _session!.Game.Moves.Count > 0;
        TakebackButton.Visibility = _setup?.AllowTakebacks == false ? Visibility.Collapsed : Visibility.Visible;

        int count = _session?.Game.Moves.Count ?? 0;
        int ply = _viewPly < 0 ? count : _viewPly;
        FirstButton.IsEnabled = PrevButton.IsEnabled = ply > 0;
        NextButton.IsEnabled = LastButton.IsEnabled = ply < count;
    }

    private string OpponentDisplayName() => _setup?.Bot?.Name ?? "Your opponent";

    private void ApplySettings()
    {
        AppSettings s = App.Settings.Current;
        if (_appliedTheme != s.BoardTheme)
        {
            _appliedTheme = s.BoardTheme;
            Board.Theme = BoardThemes.Get(s.BoardTheme);
        }
        if (_appliedPieces != s.PieceSet)
        {
            _appliedPieces = s.PieceSet;
            Board.PieceSet = PieceSets.Get(s.PieceSet);
        }
        Board.ShowLegalMoves = s.ShowLegalMoves;
        Board.ShowCoordinates = s.ShowCoordinates;
        Board.HighlightLastMove = s.HighlightLastMove;
        Board.AnimateMoves = s.AnimateMoves;
        Board.AutoQueen = s.AutoQueen;
    }

    private void RecordResult()
    {
        if (_recorded || _session == null || _setup == null) return;
        _recorded = true;
        Game g = _session.Game;
        if (g.Moves.Count == 0) return;
        if (OpeningBook.Identify(g) is Opening opening)
        {
            g.Tags["ECO"] = opening.Eco;
            g.Tags["Opening"] = opening.Name;
        }
        App.Profile.RecordGame(g,
            opponent: _setup.Bot?.Name ?? "Pass and play",
            botId: _setup.Bot?.Id,
            opponentRating: _setup.Bot?.Rating,
            playerColor: _setup.IsHotSeat ? null : _setup.HumanColor,
            timeControl: _setup.TimeControl);
    }

    // ------------------------------------------------------------------ dialogs + toasts

    private async Task ShowGameOverDialogAsync(GameEndedEventArgs e)
    {
        if (_session == null || _setup == null) return;
        Game g = _session.Game;
        string title = _setup.IsHotSeat
            ? g.Winner switch { Color.White => "White wins", Color.Black => "Black wins", _ => "Draw" }
            : g.Winner == _setup.HumanColor ? "You won!" : g.Winner == null ? "Draw" : "You lost";

        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock { Text = e.Description + ".", TextWrapping = TextWrapping.Wrap });
        if (_setup.Bot is BotProfile bp)
        {
            BotRecord rec = App.Profile.RecordAgainst(bp.Id);
            content.Children.Add(new TextBlock
            {
                Text = $"Your record vs {bp.Name}: {rec.Wins} W · {rec.Losses} L · {rec.Draws} D",
                Opacity = 0.75,
            });
            if (g.Winner == _setup.HumanColor && rec.Wins == 1)
                content.Children.Add(new TextBlock { Text = $"First win against {bp.Name}! Try the next bot up.", TextWrapping = TextWrapping.Wrap });
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = "Game review",
            SecondaryButtonText = "Rematch",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary,
        };

        ContentDialogResult result = await Dialogs.ShowAsync(dialog, XamlRoot);
        if (result == ContentDialogResult.Primary) Review_Click(this, new RoutedEventArgs());
        else if (result == ContentDialogResult.Secondary) Rematch_Click(this, new RoutedEventArgs());
    }

    private void ShowToast(string title, string message, InfoBarSeverity severity)
    {
        Toast.Title = title;
        Toast.Message = message;
        Toast.Severity = severity;
        Toast.IsOpen = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // ------------------------------------------------------------------ buttons

    private void First_Click(object sender, RoutedEventArgs e) => ShowPly(0);
    private void Prev_Click(object sender, RoutedEventArgs e) => StepPly(-1);
    private void Next_Click(object sender, RoutedEventArgs e) => StepPly(+1);
    private void Last_Click(object sender, RoutedEventArgs e) => ShowPly(int.MaxValue);
    private void Flip_Click(object sender, RoutedEventArgs e) => FlipBoard();

    private void FlipBoard()
    {
        Board.Flipped = !Board.Flipped;
        SetupPlayerBars();
        RefreshAll();
    }

    private void Takeback_Click(object sender, RoutedEventArgs e)
    {
        if (_session?.Takeback() == true) ShowToast("Move taken back", "Your last move was undone.", InfoBarSeverity.Informational);
    }

    private void Draw_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null) return;
        ShowToast("Draw offered", $"Waiting for {OpponentDisplayName()}…", InfoBarSeverity.Informational);
        _session.OfferDraw();
    }

    private async void Resign_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null || _session.Game.IsOver) return;
        if (App.Settings.Current.ConfirmResign)
        {
            var confirm = new ContentDialog
            {
                Title = "Resign this game?",
                Content = "The game will count as a loss.",
                PrimaryButtonText = "Resign",
                CloseButtonText = "Keep playing",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await Dialogs.ShowAsync(confirm, XamlRoot) != ContentDialogResult.Primary) return;
        }
        _session?.Resign();
    }

    private void Rematch_Click(object sender, RoutedEventArgs e)
    {
        if (_setup != null) StartGame(_setup);
    }

    private void NewGame_Click(object sender, RoutedEventArgs e) => App.Window.NavigateTo("play", PlayPage.ChooseParameter);

    private void Review_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null || _setup == null || _session.Game.Moves.Count == 0) return;
        var request = new ReviewRequest(_session.Game, _session.White.Name, _session.Black.Name, _setup.IsHotSeat ? Color.White : _setup.HumanColor);
        App.Window.Navigate(typeof(ReviewPage), request);
    }

    private void AddAccelerator(VirtualKey key, Action action)
    {
        var accelerator = new KeyboardAccelerator { Key = key };
        accelerator.Invoked += (_, args) =>
        {
            action();
            args.Handled = true;
        };
        KeyboardAccelerators.Add(accelerator);
    }
}
