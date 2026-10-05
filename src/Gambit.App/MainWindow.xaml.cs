using Gambit.App.Pages;
using Gambit.App.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace Gambit.App;

public sealed partial class MainWindow : Window
{
    private static readonly Dictionary<string, Type> Pages = new()
    {
        ["home"] = typeof(HomePage),
        ["play"] = typeof(PlayPage),
        ["puzzles"] = typeof(PuzzlesPage),
        ["learn"] = typeof(LearnPage),
        ["online"] = typeof(OnlinePage),
        ["analysis"] = typeof(AnalysisPage),
        ["profile"] = typeof(ProfilePage),
        ["settings"] = typeof(SettingsPage),
    };

    private bool _suppressNavigation;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _accentTimer;
    private readonly Queue<AchievementDef> _achievementQueue = new();
    private bool _showingAchievement;

    public MainWindow()
    {
        InitializeComponent();
        // A test profile is labeled so it can't be mistaken for the real one (by the user or by
        // the tools/ scripts, which only drive windows titled "... (test profile)").
        Title = AppPaths.IsTestProfile ? $"{AppInfo.DisplayName} (test profile)" : AppInfo.DisplayName;
        TitleText.Text = AppPaths.IsTestProfile ? $"{AppInfo.DisplayName} — test profile" : AppInfo.DisplayName;
        ArchBadge.Text = AppInfo.Architecture;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        string icon = Path.Combine(AppContext.BaseDirectory, "Assets", "Gambit.ico");
        if (File.Exists(icon)) AppWindow.SetIcon(icon);
        string png = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.png");
        if (File.Exists(png)) TitleIcon.Source = new BitmapImage(new Uri(png));

        SizeAndCenter();
        ApplyTheme();
        _accentTimer = DispatcherQueue.CreateTimer();
        _accentTimer.Interval = TimeSpan.FromMilliseconds(250);
        _accentTimer.IsRepeating = false;
        _accentTimer.Tick += (_, _) => ApplyAccent();
        App.Settings.Current.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.Theme)) ApplyTheme();
            // The accent follows the board theme (debounced: a color picker changes it continuously).
            if (e.PropertyName is nameof(AppSettings.BoardTheme) or nameof(AppSettings.CustomDarkSquare) or nameof(AppSettings.CustomLightSquare))
            {
                _accentTimer.Stop();
                _accentTimer.Start();
            }
        };
        RootGrid.ActualThemeChanged += (_, _) => UpdateCaptionButtons();

        NavView.ItemInvoked += NavView_ItemInvoked;
        NavView.Loaded += (_, _) => OpenPaneOnHover();
        AchievementService.Instance.Unlocked += def => DispatcherQueue.TryEnqueue(() =>
        {
            _achievementQueue.Enqueue(def);
            if (!_showingAchievement) ShowNextAchievement();
        });
        NavView.SelectedItem = NavView.MenuItems[0];
        if (App.Profile.NeedsOnboarding) ShowWelcome();
    }

    /// <summary>First launch: the welcome screen covers the app until it is finished or skipped.</summary>
    private void ShowWelcome()
    {
        var welcome = new WelcomeView();
        Grid.SetRow(welcome, 1);
        RootGrid.Children.Add(welcome);
        NavView.Visibility = Visibility.Collapsed;
        welcome.Finished += next =>
        {
            RootGrid.Children.Remove(welcome);
            NavView.Visibility = Visibility.Visible;
            if (next == "home") ContentFrame.Navigate(typeof(HomePage), null, new EntranceNavigationTransitionInfo()); // shows the new name and suggestions
            else NavigateTo(next);
        };
    }

    /// <summary>
    /// The menu is an icon rail; resting the mouse on it for a moment opens it over the page with the
    /// labels, and it closes again when the mouse leaves (the button at the top still opens it too).
    /// </summary>
    private void OpenPaneOnHover()
    {
        if (Helpers.Ui.FindDescendant(NavView, "PaneContentGrid") is not FrameworkElement pane) return;
        Microsoft.UI.Dispatching.DispatcherQueueTimer timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(400);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => NavView.IsPaneOpen = true;
        pane.PointerEntered += (_, e) =>
        {
            if (!NavView.IsPaneOpen && e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse) timer.Start();
        };
        pane.PointerExited += (_, _) =>
        {
            timer.Stop();
            NavView.IsPaneOpen = false;
        };
    }

    /// <summary>Clicking the already-selected item returns to that section's main page (e.g. from a puzzle back to the hub).</summary>
    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (!ReferenceEquals(args.InvokedItemContainer, NavView.SelectedItem)) return;
        string tag = args.IsSettingsInvoked ? "settings" : args.InvokedItemContainer?.Tag as string ?? "";
        if (tag == "play" && GamePage.HasActiveGame) return;
        if (Pages.TryGetValue(tag, out Type? page) && ContentFrame.CurrentSourcePageType != page)
            ContentFrame.Navigate(page, null, new EntranceNavigationTransitionInfo());
    }

    /// <summary>Navigate the content frame (selects the matching nav item when there is one).</summary>
    public void Navigate(Type pageType, object? parameter = null, string? navTag = null)
    {
        if (navTag != null) SelectNavItem(navTag);
        if (ContentFrame.CurrentSourcePageType == pageType && parameter == null) return;
        ContentFrame.Navigate(pageType, parameter, new EntranceNavigationTransitionInfo());
    }

    public void NavigateTo(string tag, object? parameter = null)
    {
        if (Pages.TryGetValue(tag, out Type? page)) Navigate(page, parameter, tag);
    }

    private void SelectNavItem(string tag)
    {
        _suppressNavigation = true;
        try
        {
            if (tag == "settings")
            {
                NavView.SelectedItem = NavView.SettingsItem;
                return;
            }
            foreach (object item in NavView.MenuItems)
            {
                if (item is NavigationViewItem nvi && (string?)nvi.Tag == tag)
                {
                    NavView.SelectedItem = nvi;
                    break;
                }
            }
        }
        finally
        {
            _suppressNavigation = false;
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_suppressNavigation) return;
        string tag = args.IsSettingsSelected ? "settings" : (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "home";

        // "Play" returns to an unfinished game if there is one.
        if (tag == "play" && GamePage.HasActiveGame)
        {
            ContentFrame.Navigate(typeof(GamePage), null, new EntranceNavigationTransitionInfo());
            return;
        }

        if (Pages.TryGetValue(tag, out Type? page) && ContentFrame.CurrentSourcePageType != page)
            ContentFrame.Navigate(page, null, new EntranceNavigationTransitionInfo());
    }

    /// <summary>Shows queued achievement notifications one at a time (5 s each).</summary>
    private void ShowNextAchievement()
    {
        if (_achievementQueue.Count == 0)
        {
            _showingAchievement = false;
            AchievementToast.Visibility = Visibility.Collapsed;
            return;
        }
        _showingAchievement = true;
        AchievementDef def = _achievementQueue.Dequeue();
        AchievementIcon.Glyph = def.Glyph;
        AchievementTitle.Text = def.Title;
        AchievementText.Text = def.Description;
        AchievementBadge.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Helpers.Ui.ParseColor(def.Tier switch
        {
            AchievementTier.Gold => "#D4A017",
            AchievementTier.Silver => "#8E9AA6",
            _ => "#B87333",
        }));
        AchievementToast.Visibility = Visibility.Visible;
        SoundService.Play(GameSound.Win);
        Helpers.Delay.Run(DispatcherQueue, TimeSpan.FromSeconds(5), ShowNextAchievement);
    }

    /// <summary>Puts this window behind all others without activating it (test-profile runs).</summary>
    public void SendToBack()
    {
        IntPtr hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        const uint NoSize = 0x0001, NoMove = 0x0002, NoActivate = 0x0010;
        SetWindowPos(hwnd, new IntPtr(1) /* HWND_BOTTOM */, 0, 0, 0, 0, NoSize | NoMove | NoActivate);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    private void SizeAndCenter()
    {
        try
        {
            DisplayArea area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            RectInt32 work = area.WorkArea;
            int w = Math.Min(work.Width, Math.Max(1100, (int)(work.Width * 0.82)));
            int h = Math.Min(work.Height, Math.Max(760, (int)(work.Height * 0.86)));
            AppWindow.MoveAndResize(new RectInt32(work.X + (work.Width - w) / 2, work.Y + (work.Height - h) / 2, w, h));
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumWidth = 900;
                presenter.PreferredMinimumHeight = 640;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Window sizing failed: {ex.Message}");
        }
    }

    private void ApplyTheme()
    {
        RootGrid.RequestedTheme = App.Settings.Current.Theme switch
        {
            AppThemeMode.Light => ElementTheme.Light,
            AppThemeMode.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        UpdateCaptionButtons();
    }

    /// <summary>Takes the accent from the board theme and re-applies the theme so every brush picks it up.</summary>
    private void ApplyAccent()
    {
        Windows.UI.Color accent = Helpers.BoardSettings.CurrentTheme(App.Settings.Current).Accent;
        if (accent == Theming.AppAccent.Current) return;
        Theming.AppAccent.Apply(accent);
        ElementTheme requested = RootGrid.RequestedTheme;
        RootGrid.RequestedTheme = RootGrid.ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High, () =>
        {
            RootGrid.RequestedTheme = requested;
            UpdateCaptionButtons();
        });
    }

    private void UpdateCaptionButtons()
    {
        bool dark = RootGrid.ActualTheme == ElementTheme.Dark;
        AppWindowTitleBar tb = AppWindow.TitleBar;
        tb.ButtonBackgroundColor = Colors.Transparent;
        tb.ButtonInactiveBackgroundColor = Colors.Transparent;
        tb.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        tb.ButtonInactiveForegroundColor = dark ? ColorHelper.FromArgb(255, 160, 160, 160) : ColorHelper.FromArgb(255, 110, 110, 110);
        tb.ButtonHoverBackgroundColor = dark ? ColorHelper.FromArgb(24, 255, 255, 255) : ColorHelper.FromArgb(20, 0, 0, 0);
        tb.ButtonHoverForegroundColor = dark ? Colors.White : Colors.Black;
        tb.ButtonPressedBackgroundColor = dark ? ColorHelper.FromArgb(40, 255, 255, 255) : ColorHelper.FromArgb(36, 0, 0, 0);
    }
}
