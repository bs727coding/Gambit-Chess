using Gambit.App.Controls;
using Gambit.App.Helpers;
using Gambit.App.Services;
using Gambit.App.Theming;
using Gambit.Core.Board;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Gambit.App.Pages;

public sealed partial class SettingsPage : Page
{
    private const string PreviewFen = "r1bqkb1r/pppp1ppp/2n2n2/4p2Q/2B1P3/8/PPPP1PPP/RNB1K1NR w KQkq - 4 4";
    private bool _loading = true;

    public SettingsPage()
    {
        InitializeComponent();
        AppSettings s = App.Settings.Current;

        ThemeBox.SelectedIndex = (int)s.Theme;
        foreach (PieceSet set in PieceSets.All) PieceBox.Items.Add(set.Name);
        PieceBox.SelectedIndex = Math.Max(0, PieceSets.All.ToList().FindIndex(p => p.Id == s.PieceSet));

        LegalSwitch.IsOn = s.ShowLegalMoves;
        CoordSwitch.IsOn = s.ShowCoordinates;
        LastMoveSwitch.IsOn = s.HighlightLastMove;
        AnimateSwitch.IsOn = s.AnimateMoves;
        QueenSwitch.IsOn = s.AutoQueen;
        PremoveSwitch.IsOn = s.Premoves;
        ResignSwitch.IsOn = s.ConfirmResign;
        SoundSwitch.IsOn = s.SoundEnabled;

        AboutTitle.Text = $"{AppInfo.DisplayName} {AppInfo.Version}";
        AboutDetails.Text = $"Native {AppInfo.Architecture} build · .NET {Environment.Version} · Windows App SDK · Engine: Gambit search (PVS, PeSTO evaluation)";

        BuildSwatches();
        var pos = Position.FromFen(PreviewFen);
        Preview.Interaction = BoardInteraction.Both;
        Preview.SetPosition(pos, new Move(Square.G8, Square.F6)); // Black's last move: ...Nf6??
        Preview.SetMarkers([BoardMarker.Arrow(Square.H5, Square.F7, Ui.ParseColor("#FFAA00", 200))]);
        ApplyPreview();
        _loading = false;
    }

    private void BuildSwatches()
    {
        ThemeSwatches.Children.Clear();
        foreach (BoardTheme theme in BoardThemes.All)
        {
            var mini = new Grid { Width = 64, Height = 64, CornerRadius = new CornerRadius(4) };
            for (int i = 0; i < 2; i++)
            {
                mini.RowDefinitions.Add(new RowDefinition());
                mini.ColumnDefinitions.Add(new ColumnDefinition());
            }
            for (int r = 0; r < 2; r++)
            {
                for (int c = 0; c < 2; c++)
                {
                    var rect = new Rectangle { Fill = new SolidColorBrush((r + c) % 2 == 0 ? theme.Light : theme.Dark) };
                    Grid.SetRow(rect, r);
                    Grid.SetColumn(rect, c);
                    mini.Children.Add(rect);
                }
            }

            bool selected = App.Settings.Current.BoardTheme == theme.Id;
            var frame = new Border
            {
                Child = mini,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(3),
                BorderBrush = selected ? Ui.AccentBrush : new SolidColorBrush(Colors.Transparent),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var label = new TextBlock { Text = theme.Name, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var stack = new StackPanel { Spacing = 4 };
            stack.Children.Add(frame);
            stack.Children.Add(label);

            var button = new Button
            {
                Content = stack,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4),
                Tag = theme.Id,
            };
            ToolTipService.SetToolTip(button, theme.Name);
            button.Click += (_, _) =>
            {
                App.Settings.Current.BoardTheme = theme.Id;
                BuildSwatches();
                ApplyPreview();
            };
            ThemeSwatches.Children.Add(button);
        }
    }

    private void ApplyPreview()
    {
        AppSettings s = App.Settings.Current;
        Preview.Theme = BoardThemes.Get(s.BoardTheme);
        if (Preview.PieceSet.Id != s.PieceSet) Preview.PieceSet = PieceSets.Get(s.PieceSet);
        Preview.ShowCoordinates = s.ShowCoordinates;
        Preview.ShowLegalMoves = s.ShowLegalMoves;
        Preview.HighlightLastMove = s.HighlightLastMove;
        Preview.ClearSelection();
    }

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeBox.SelectedIndex < 0) return;
        App.Settings.Current.Theme = (AppThemeMode)ThemeBox.SelectedIndex;
    }

    private void PieceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || PieceBox.SelectedIndex < 0) return;
        App.Settings.Current.PieceSet = PieceSets.All[PieceBox.SelectedIndex].Id;
        ApplyPreview();
    }

    private void Switch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        AppSettings s = App.Settings.Current;
        s.ShowLegalMoves = LegalSwitch.IsOn;
        s.ShowCoordinates = CoordSwitch.IsOn;
        s.HighlightLastMove = LastMoveSwitch.IsOn;
        s.AnimateMoves = AnimateSwitch.IsOn;
        s.AutoQueen = QueenSwitch.IsOn;
        s.Premoves = PremoveSwitch.IsOn;
        s.ConfirmResign = ResignSwitch.IsOn;
        bool soundWasOn = s.SoundEnabled;
        s.SoundEnabled = SoundSwitch.IsOn;
        if (!soundWasOn && s.SoundEnabled) SoundService.Play(GameSound.Move);
        ApplyPreview();
    }

    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{AppPaths.Root}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"Opening data folder failed: {ex.Message}");
        }
    }
}
