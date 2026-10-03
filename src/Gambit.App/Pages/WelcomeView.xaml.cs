using Gambit.App.Services;
using Gambit.ViewModels;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Gambit.App.Pages;

/// <summary>
/// First launch: asks for a name and how much chess the player has played, then picks a first
/// opponent and a starting puzzle rating (see <see cref="Onboarding"/>). Shown once, over the app.
/// </summary>
public sealed partial class WelcomeView : UserControl
{
    private ExperienceOption? _choice;

    public WelcomeView()
    {
        InitializeComponent();
        string png = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.png");
        if (File.Exists(png)) Logo.Source = new BitmapImage(new Uri(png));

        NameBox.Text = App.Profile.Profile.Name;
        foreach (ExperienceOption option in Onboarding.Options) LevelList.Children.Add(OptionButton(option));
        UpdateSummary();

        Loaded += (_, _) =>
        {
            NameBox.Focus(FocusState.Programmatic);
            NameBox.SelectAll();
        };
    }

    /// <summary>Raised when the player finishes or skips; the argument is the section to show next.</summary>
    public event Action<string>? Finished;

    private RadioButton OptionButton(ExperienceOption option)
    {
        var text = new StackPanel { Margin = new Thickness(0, -2, 0, 0) };
        text.Children.Add(new TextBlock { Text = option.Title, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock
        {
            Text = option.Description,
            Style = (Style)Application.Current.Resources["SecondaryTextStyle"],
            TextWrapping = TextWrapping.Wrap,
        });
        var button = new RadioButton { Content = text, GroupName = "experience", Padding = new Thickness(8, 4, 0, 4) };
        AutomationProperties.SetName(button, $"{option.Title}: {option.Description}");
        button.Checked += (_, _) =>
        {
            _choice = option;
            UpdateSummary();
        };
        return button;
    }

    private void UpdateSummary()
    {
        StartButton.IsEnabled = _choice != null;
        SummaryText.Text = _choice == null
            ? "Pick the closest match. Every bot and every puzzle stays open to you either way."
            : Onboarding.Summary(_choice);
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_choice == null) return;
        App.Settings.Current.LastBotId = _choice.BotId;
        App.Profile.CompleteOnboarding(NameBox.Text, _choice);
        Log.Info($"Welcome screen finished: {_choice.Level}");
        Finished?.Invoke(_choice.Level == ExperienceLevel.NewToChess ? "learn" : "home");
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        App.Profile.CompleteOnboarding(NameBox.Text, null);
        Log.Info("Welcome screen skipped");
        Finished?.Invoke("home");
    }
}
