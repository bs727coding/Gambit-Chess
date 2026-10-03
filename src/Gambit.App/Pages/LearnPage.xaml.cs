using Gambit.App.Helpers;
using Gambit.App.Services;
using Gambit.Core.Lessons;
using Gambit.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace Gambit.App.Pages;

/// <summary>Course map: the opening review, then every course with its lessons, progress and stars.</summary>
public sealed partial class LearnPage : Page
{
    private string? _continueKey;
    private IReadOnlyList<ReviewLine> _due = [];

    public LearnPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Dictionary<string, LessonResult> progress = App.Profile.Profile.Lessons;
        var all = LessonCatalog.AllLessons.ToList();
        int done = all.Count(l => progress.ContainsKey(l.Key));
        Subtitle.Text = $"Basics, checkmates, tactics, openings and endgames · {done} of {all.Count} completed";

        Lesson? next = all.FirstOrDefault(l => !progress.ContainsKey(l.Key));
        _continueKey = next?.Key;
        ContinueButton.Visibility = next == null ? Visibility.Collapsed : Visibility.Visible;
        ContinueText.Text = done == 0 ? "Start learning" : $"Continue: {next?.Title}";

        CourseList.Children.Clear();
        foreach (Course course in LessonCatalog.Courses) CourseList.Children.Add(BuildCourse(course, progress));
        UpdateReview();
    }

    /// <summary>The opening review card: shown once an opening lesson is finished.</summary>
    private void UpdateReview()
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        IReadOnlyList<(ReviewLine Line, DateOnly Due)> schedule = App.Profile.ReviewSchedule();
        ReviewCard.Visibility = schedule.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (schedule.Count == 0) return;

        if (LessonCatalog.Courses.FirstOrDefault(c => c.Id == "openings") is Course openings) ReviewBadge.Background = Ui.Brush(openings.Color);
        _due = App.Profile.DueReviews();
        string lines = schedule.Count == 1 ? "1 line" : $"{schedule.Count} lines";
        if (_due.Count > 0)
        {
            ReviewText.Text = _due.Count == 1
                ? $"1 line from your opening lessons is due. Lines you know come back less and less often ({lines} in rotation)."
                : $"{_due.Count} lines from your opening lessons are due. Lines you know come back less and less often ({lines} in rotation).";
            ReviewButtonText.Text = _due.Count == 1 ? "Review 1 line" : $"Review {_due.Count} lines";
            ReviewButton.Visibility = Visibility.Visible;
        }
        else
        {
            ReviewText.Text = $"All caught up: {lines} in rotation. The next one is due {OpeningReview.When(schedule[0].Due, today)}.";
            ReviewButton.Visibility = Visibility.Collapsed;
        }
    }

    private void Review_Click(object sender, RoutedEventArgs e)
    {
        if (_due.Count > 0) App.Window.Navigate(typeof(LessonPage), new OpeningReviewRequest(_due), "learn");
    }

    private static FrameworkElement BuildCourse(Course course, Dictionary<string, LessonResult> progress)
    {
        int done = course.Lessons.Count(l => progress.ContainsKey(l.Key));
        var card = new Border
        {
            Style = (Style)Application.Current.Resources["CardStyle"],
            Padding = new Thickness(20),
        };
        var stack = new StackPanel { Spacing = 14 };

        var header = new Grid { ColumnSpacing = 16 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new Border
        {
            Width = 48,
            Height = 48,
            CornerRadius = new CornerRadius(24),
            Background = Ui.Brush(course.Color),
            Child = new FontIcon { Glyph = course.Glyph, FontSize = 20, Foreground = new SolidColorBrush(Colors.White) },
        });
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = course.Title, FontSize = 20, FontWeight = FontWeights.SemiBold });
        titles.Children.Add(new TextBlock { Text = course.Description, Opacity = 0.75, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(titles, 1);
        header.Children.Add(titles);
        var count = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Width = 140 };
        count.Children.Add(new TextBlock { Text = $"{done} / {course.Lessons.Count} lessons", HorizontalAlignment = HorizontalAlignment.Right, Opacity = 0.8 });
        count.Children.Add(new ProgressBar { Maximum = Math.Max(1, course.Lessons.Count), Value = done, Margin = new Thickness(0, 6, 0, 0) });
        Grid.SetColumn(count, 2);
        header.Children.Add(count);
        stack.Children.Add(header);

        var lessons = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 250, ItemHeight = 92 };
        int number = 1;
        foreach (Lesson lesson in course.Lessons)
        {
            progress.TryGetValue(lesson.Key, out LessonResult? result);
            var content = new Grid { ColumnSpacing = 12 };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var badgeText = new TextBlock
            {
                Text = result != null ? "✓" : number.ToString(),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            // Only override the color when completed; otherwise inherit the theme's text color.
            if (result != null) badgeText.Foreground = new SolidColorBrush(Colors.White);
            content.Children.Add(new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                VerticalAlignment = VerticalAlignment.Top,
                Background = result != null ? Ui.Brush("#2E7D32") : Ui.NeutralFill(50),
                Child = badgeText,
            });
            var text = new StackPanel { Spacing = 2 };
            text.Children.Add(new TextBlock { Text = lesson.Title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = lesson.Summary, FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis });
            if (result != null)
                text.Children.Add(new TextBlock { Text = new string('★', result.Stars) + new string('☆', 3 - result.Stars), Foreground = Ui.Brush("#F2B705"), FontSize = 13 });
            Grid.SetColumn(text, 1);
            content.Children.Add(text);

            var button = new Button
            {
                Content = content,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(12, 10, 12, 10),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"Lesson: {lesson.Title}");
            string key = lesson.Key;
            button.Click += (_, _) => App.Window.Navigate(typeof(LessonPage), key, "learn");
            lessons.Children.Add(button);
            number++;
        }
        stack.Children.Add(lessons);
        Ui.StretchTiles(lessons, 240);
        card.Child = stack;
        return card;
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (_continueKey != null) App.Window.Navigate(typeof(LessonPage), _continueKey, "learn");
    }
}
