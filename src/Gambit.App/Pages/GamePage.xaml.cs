using Gambit.App.Controls;
using Gambit.App.Helpers;
using Gambit.App.Services;
using Gambit.Core.Board;
using Gambit.Core.Sessions;
using Gambit.Online.Client;
using Gambit.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace Gambit.App.Pages;

/// <summary>
/// Shows one game: board, players, clocks, moves and actions. The game flow — starting and resuming
/// games, following the session, history browsing, premoves, which actions are available, results —
/// lives in <see cref="GameViewModel"/> (unit-tested); this page draws its state and shows the
/// dialogs and toasts it asks for. It never asks whether the opponent is a bot or a person online.
/// </summary>
public sealed partial class GamePage : Page
{
    private static GamePage? _instance;

    private readonly GameViewModel _vm = new(new GameHost());
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private ContentDialog? _resultDialog;

    public GamePage()
    {
        InitializeComponent();
        _instance = this;
        _clockTimer.Tick += (_, _) =>
        {
            _vm.TickClock();
            UpdateClocks();
        };
        _toastTimer.Tick += (_, _) =>
        {
            Toast.IsOpen = false;
            _toastTimer.Stop();
        };
        Board.BoardSizeChanged += (_, size) => TopBar.Width = BottomBar.Width = size;

        _vm.GameStarted += Vm_GameStarted;
        _vm.Changed += (_, _) => RefreshAll();
        _vm.BoardChanged += Vm_BoardChanged;
        _vm.MovesChanged += (_, _) => RefreshMoveList();
        _vm.Notice += (_, notice) => ShowToast(notice);
        _vm.ChatReceived += (_, text) =>
        {
            ChatText.Text = text;
            ChatBubble.Visibility = Visibility.Visible;
        };
        _vm.DrawOfferReceived += Vm_DrawOfferReceived;
        _vm.GameOver += Vm_GameOver;
        _vm.OpponentMoved += (_, _) =>
        {
            if (!Board.HasPremove) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (Board.TakePremove() is (int from, int to)) _vm.PlayPremove(from, to);
            });
        };

        // The shortcuts belong to the whole page: without this, WinUI shows the first one's key ("Left")
        // as a tooltip wherever the pointer rests, e.g. over the board.
        KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        AddAccelerator(VirtualKey.Left, () => _vm.StepPly(-1));
        AddAccelerator(VirtualKey.Right, () => _vm.StepPly(+1));
        AddAccelerator(VirtualKey.Home, () => _vm.ShowPly(0));
        AddAccelerator(VirtualKey.End, () => _vm.ShowPly(int.MaxValue));
        AddAccelerator(VirtualKey.F, FlipBoard);

        App.Settings.Current.PropertyChanged += (_, _) => ApplySettings();
        ApplySettings();
    }

    /// <summary>True while an unfinished game of the user's exists (the Play tab returns to it).</summary>
    public static bool HasActiveGame => _instance?._vm.Session != null ? _instance._vm.HasUnfinishedGame : ActiveGameStore.Exists;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is GameSetup setup)
        {
            _vm.StartLocal(setup);
        }
        else if (e.Parameter is RemoteGameSession remote)
        {
            if (!ReferenceEquals(remote, _vm.Session)) _vm.StartOnline(remote);
        }
        else if (_vm.Session == null)
        {
            if (ActiveGameStore.Load() is SavedGame saved) _vm.Resume(saved);
            else DispatcherQueue.TryEnqueue(() => App.Window.NavigateTo("play"));
        }
    }

    // ------------------------------------------------------------------ view model events

    private void Vm_GameStarted(object? sender, EventArgs e)
    {
        ChatBubble.Visibility = Visibility.Collapsed;
        Toast.IsOpen = false;
        Board.ClearPremove();
        Board.SetMarkers([]);
        Board.Flipped = _vm.StartFlipped;
        if (_vm.Position is Position pos) Board.SetPosition(pos, _vm.LastMove);
        SetupPlayerBars();
        SetupOpponentCard();
        RefreshMoveList();
        if (_vm.Session?.Clock != null) _clockTimer.Start();
        else _clockTimer.Stop();
    }

    private void Vm_BoardChanged(object? sender, BoardUpdate update)
    {
        if (update.ClearPremove) Board.ClearPremove();
        if (update.ClearMarkers) Board.SetMarkers([]);
        if (_vm.Position is Position pos) Board.SetPosition(pos, _vm.LastMove, animate: update.Animate);
        MoveList.Highlight(_vm.DisplayedPly);
    }

    private async void Vm_DrawOfferReceived(object? sender, EventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Draw offered",
            Content = $"{_vm.OpponentName} offers a draw.",
            PrimaryButtonText = "Accept draw",
            CloseButtonText = "Decline",
            DefaultButton = ContentDialogButton.Close,
        };
        bool accept = await Dialogs.ShowAsync(dialog, XamlRoot) == ContentDialogResult.Primary;
        _vm.RespondToDraw(accept);
    }

    private void Vm_GameOver(object? sender, GameOverSummary summary)
    {
        Board.ClearPremove();
        _clockTimer.Stop();
        _ = ShowGameOverDialogAsync(summary);
    }

    // ------------------------------------------------------------------ rendering

    private Color BottomColor => Board.Flipped ? Color.Black : Color.White;

    private void RefreshAll()
    {
        UpdateInteraction();
        UpdateCaptured();
        UpdateClocks();
        UpdateThinking();
        StatusText.Text = _vm.Status;
        OpeningText.Text = _vm.OpeningName;
        UpdateButtons();
    }

    private void RefreshMoveList()
    {
        if (_vm.Session is IGameSession session) MoveList.SetMoves(session.Game.Moves, _vm.DisplayedPly);
    }

    private void UpdateInteraction()
    {
        Board.Interaction = _vm.Input switch
        {
            MoveInput.White => BoardInteraction.White,
            MoveInput.Black => BoardInteraction.Black,
            MoveInput.Both => BoardInteraction.Both,
            _ => BoardInteraction.None,
        };
        Board.AllowPremoves = _vm.AllowPremoves;
    }

    private void SetupPlayerBars()
    {
        ConfigureBar(BottomBar, BottomColor);
        ConfigureBar(TopBar, BottomColor.Opposite());
    }

    private void ConfigureBar(PlayerBar bar, Color side)
    {
        if (_vm.Session == null) return;
        PlayerBadge badge = _vm.Badge(side);
        bar.SetPlayer(badge.Name, badge.Rating, badge.Monogram, badge.ColorHex);
    }

    private void SetupOpponentCard()
    {
        OpponentCard card = _vm.Opponent;
        OpponentAvatar.Children.Clear();
        OpponentAvatar.Children.Add(Ui.Avatar(card.Monogram, card.ColorHex, 56));
        OpponentName.Text = card.Name;
        OpponentTagline.Text = card.Tagline;
    }

    private void UpdateCaptured()
    {
        Captures white = _vm.CapturesBy(Color.White), black = _vm.CapturesBy(Color.Black);
        PlayerBar whiteBar = BottomColor == Color.White ? BottomBar : TopBar;
        PlayerBar blackBar = BottomColor == Color.White ? TopBar : BottomBar;
        whiteBar.SetCaptured(white.Pieces, white.Advantage, Board.PieceSet);
        blackBar.SetCaptured(black.Pieces, black.Advantage, Board.PieceSet);
    }

    private void UpdateClocks()
    {
        Color bottom = BottomColor;
        BottomBar.SetClock(_vm.Remaining(bottom), _vm.IsClockRunning(bottom));
        TopBar.SetClock(_vm.Remaining(bottom.Opposite()), _vm.IsClockRunning(bottom.Opposite()));
    }

    private void UpdateThinking()
    {
        bool thinking = _vm.Session?.IsOpponentThinking == true;
        Color toMove = _vm.Session?.Game.SideToMove ?? Color.White;
        BottomBar.SetThinking(thinking && toMove == BottomColor);
        TopBar.SetThinking(thinking && toMove != BottomColor);
    }

    private void UpdateButtons()
    {
        TakebackButton.IsEnabled = _vm.CanTakeback;
        DrawButton.IsEnabled = _vm.CanOfferDraw;
        ResignButton.IsEnabled = _vm.CanResign;
        DrawButton.Visibility = ResignButton.Visibility = _vm.ShowDrawAndResign ? Visibility.Visible : Visibility.Collapsed;
        TakebackButton.Visibility = _vm.ShowTakeback ? Visibility.Visible : Visibility.Collapsed;
        LayoutGameActions();
        PostGamePanel.Visibility = _vm.ShowPostGame ? Visibility.Visible : Visibility.Collapsed;
        HintButton.IsEnabled = _vm.CanHint;
        FirstButton.IsEnabled = PrevButton.IsEnabled = _vm.CanGoBack;
        NextButton.IsEnabled = LastButton.IsEnabled = _vm.CanGoForward;

        RematchText.Text = _vm.RematchLabel ?? "Rematch";
        RematchButton.IsEnabled = _vm.CanRematch;
        RematchButton.Visibility = _vm.ShowRematch ? Visibility.Visible : Visibility.Collapsed;
        if (_resultDialog != null && _vm.IsRematchRequested) _resultDialog.SecondaryButtonText = "Accept rematch";
    }

    /// <summary>
    /// Gives each visible game action an equal column (no gap where Undo is hidden online). The row
    /// disappears once the game is over: the post-game buttons take its place.
    /// </summary>
    private void LayoutGameActions()
    {
        Button[] visible = [.. new[] { TakebackButton, DrawButton, ResignButton }.Where(b => b.Visibility == Visibility.Visible)];
        GameActions.Visibility = _vm.ShowGameActions && visible.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        bool laidOut = GameActions.ColumnDefinitions.Count == visible.Length
            && visible.Select((b, i) => Grid.GetColumn(b) == i).All(ok => ok);
        if (laidOut) return;
        GameActions.ColumnDefinitions.Clear();
        for (int i = 0; i < visible.Length; i++)
        {
            GameActions.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(visible[i], i);
        }
    }

    private void ApplySettings()
    {
        Board.ApplyUserSettings();
        UpdateInteraction();
    }

    // ------------------------------------------------------------------ dialogs + toasts

    private async Task ShowGameOverDialogAsync(GameOverSummary summary)
    {
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock { Text = summary.Description, TextWrapping = TextWrapping.Wrap });
        foreach (string line in summary.Details)
            content.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });

        var dialog = new ContentDialog
        {
            Title = summary.Title,
            Content = content,
            PrimaryButtonText = "Game review",
            SecondaryButtonText = summary.RematchLabel ?? "", // watching: an empty text hides the button
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary,
        };

        _resultDialog = dialog;
        ContentDialogResult result;
        try
        {
            result = await Dialogs.ShowAsync(dialog, XamlRoot);
        }
        finally
        {
            _resultDialog = null;
        }
        if (result == ContentDialogResult.Primary) Review_Click(this, new RoutedEventArgs());
        else if (result == ContentDialogResult.Secondary) _vm.Rematch();
    }

    private void ShowToast(GameNotice notice)
    {
        Toast.Title = notice.Title;
        Toast.Message = notice.Message;
        Toast.Severity = notice.Kind == NoticeKind.Success ? InfoBarSeverity.Success : InfoBarSeverity.Informational;
        if (notice is { ActionLabel: string label, Action: Action run })
        {
            var button = new Button { Content = label };
            button.Click += (_, _) =>
            {
                Toast.IsOpen = false;
                run();
            };
            Toast.ActionButton = button;
        }
        else
        {
            Toast.ActionButton = null;
        }
        Toast.IsOpen = true;
        _toastTimer.Stop();
        if (notice.Action == null) _toastTimer.Start(); // questions stay until answered or dismissed
    }

    // ------------------------------------------------------------------ input

    private void Board_MoveRequested(object? sender, BoardMoveEventArgs e) => _vm.SubmitMove(e.Move);

    private void MoveList_PlySelected(object? sender, int ply) => _vm.ShowPly(ply);

    private void First_Click(object sender, RoutedEventArgs e) => _vm.ShowPly(0);
    private void Prev_Click(object sender, RoutedEventArgs e) => _vm.StepPly(-1);
    private void Next_Click(object sender, RoutedEventArgs e) => _vm.StepPly(+1);
    private void Last_Click(object sender, RoutedEventArgs e) => _vm.ShowPly(int.MaxValue);
    private void Flip_Click(object sender, RoutedEventArgs e) => FlipBoard();

    private void FlipBoard()
    {
        Board.Flipped = !Board.Flipped;
        SetupPlayerBars();
        RefreshAll();
    }

    private void Takeback_Click(object sender, RoutedEventArgs e) => _vm.Takeback();

    private void Draw_Click(object sender, RoutedEventArgs e) => _vm.OfferDraw();

    private async void Resign_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsLive) return;
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
        _vm.Resign();
    }

    private async void Hint_Click(object sender, RoutedEventArgs e)
    {
        HintButton.IsEnabled = false;
        try
        {
            if (await _vm.FindHintAsync() is Move best)
            {
                var green = Ui.ParseColor("#81B64C", 220);
                Board.SetMarkers([BoardMarker.Square(best.From, green), BoardMarker.Arrow(best.From, best.To, green)]);
            }
        }
        finally
        {
            HintButton.IsEnabled = _vm.CanHint;
        }
    }

    private void Rematch_Click(object sender, RoutedEventArgs e) => _vm.Rematch();

    private void NewGame_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Setup?.IsOnline == true) App.Window.Navigate(typeof(OnlinePage), null, "online");
        else App.Window.NavigateTo("play", PlayPage.ChooseParameter);
    }

    private void Review_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.CanReview || _vm.Session is not IGameSession session) return;
        var request = new ReviewRequest(session.Game, session.White.Name, session.Black.Name, _vm.ReviewPerspective);
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
