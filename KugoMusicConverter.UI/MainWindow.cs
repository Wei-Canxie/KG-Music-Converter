using System;
using System.Collections.ObjectModel;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace KugoMusicConverter;

internal sealed class MainWindow : Window
{
    private ConversionEngine? _engine;
    private CancellationTokenSource? _cts;

    private NavigationView? _nav;
    private ContentControl? _contentHost;
    private TextBox? _logBox;
    private ProgressBar? _progressBar;
    private TextBlock? _progressLabel;
    private Button? _startButton;
    private Button? _cancelButton;
    private ListView? _queueList;
    private CheckBox? _skipCopyCheck;
    private CheckBox? _skipConvertCheck;
    private CheckBox? _unifiedOutputCheck;
    private TextBox? _unifiedOutputBox;
    private Button? _browseOutputButton;
    private TextBlock? _kggWarning;
    private Button? _clearCompletedButton;

    private readonly ObservableCollection<FileEntry> _files = new();

    public MainWindow()
    {
        Title = "Kugo Music Converter — 酷狗加密音频解密工具箱";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 750));

        // 加载设置
        var settings = Settings.Load();
        ThemeManager.Instance.AccentColor = ColorHelper.FromArgb(255, settings.ThemeR, settings.ThemeG, settings.ThemeB);
        ThemeManager.Instance.Mode = settings.Theme;

        BuildUI();
        ApplyAllSettings(settings);
    }

    private void BuildUI()
    {
        var root = new Grid { Background = ThemeManager.Instance.Background };

        _nav = new NavigationView
        {
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            IsSettingsVisible = false,
            PaneDisplayMode = NavigationViewPaneDisplayMode.Top,
        };
        _nav.MenuItems.Add(new NavigationViewItem
        {
            Content = "转换",
            Icon = new SymbolIcon(Symbol.Sync),
            Tag = "convert"
        });
        _nav.MenuItems.Add(new NavigationViewItem
        {
            Content = "设置",
            Icon = new SymbolIcon(Symbol.Setting),
            Tag = "settings"
        });
        _nav.MenuItems.Add(new NavigationViewItem
        {
            Content = "关于",
            Icon = new SymbolIcon(Symbol.OutlineStar),
            Tag = "about"
        });
        _nav.SelectionChanged += Nav_SelectionChanged;

        _contentHost = new ContentControl();
        Grid.SetRow(_contentHost, 1);
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        root.Children.Add(_nav);
        root.Children.Add(_contentHost);
        Content = root;

        _contentHost.Content = new ConvertControl(this);
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            _contentHost!.Content = tag switch
            {
                "convert" => new ConvertControl(this),
                "settings" => new SettingsControl(this),
                "about" => new AboutControl(this),
                _ => new ConvertControl(this)
            };
        }
    }

    internal void ApplyAllSettings(Settings settings)
    {
        // 应用主题色
        ThemeManager.Instance.AccentColor = ColorHelper.FromArgb(255, settings.ThemeR, settings.ThemeG, settings.ThemeB);
        ThemeManager.Instance.Mode = settings.Theme;

        // 应用窗口不透明度
        var root = Content as FrameworkElement;
        if (root != null) root.Opacity = settings.WindowOpacity;

        // 应用模糊模式
        try
        {
            bool isDark = settings.Theme != ThemeMode.Light;
            SystemBackdrop = settings.Blur switch
            {
                BlurMode.Mica => new MicaBackdrop { Kind = isDark ? MicaKind.Base : MicaKind.BaseAlt },
                _ => null
            };
        }
        catch
        {
            SystemBackdrop = null;
        }
    }

    internal void SetWindowOpacity(double opacity)
    {
        var root = Content as FrameworkElement;
        if (root != null) root.Opacity = opacity;
    }

    internal Frame? Frame => null;
    internal ObservableCollection<FileEntry> Files => _files;
    internal TextBox? LogBox { get => _logBox; set => _logBox = value; }
    internal ProgressBar? ProgressBar { get => _progressBar; set => _progressBar = value; }
    internal TextBlock? ProgressLabel { get => _progressLabel; set => _progressLabel = value; }
    internal Button? StartButton { get => _startButton; set => _startButton = value; }
    internal Button? CancelButton { get => _cancelButton; set => _cancelButton = value; }
    internal CheckBox? SkipCopyCheck { get => _skipCopyCheck; set => _skipCopyCheck = value; }
    internal CheckBox? SkipConvertCheck { get => _skipConvertCheck; set => _skipConvertCheck = value; }
    internal CheckBox? UnifiedOutputCheck { get => _unifiedOutputCheck; set => _unifiedOutputCheck = value; }
    internal TextBox? UnifiedOutputBox { get => _unifiedOutputBox; set => _unifiedOutputBox = value; }
    internal Button? BrowseOutputButton { get => _browseOutputButton; set => _browseOutputButton = value; }
    internal TextBlock? KggWarning { get => _kggWarning; set => _kggWarning = value; }
    internal Button? ClearCompletedButton { get => _clearCompletedButton; set => _clearCompletedButton = value; }
    internal ListView? QueueList { get => _queueList; set => _queueList = value; }
    internal ConversionEngine? Engine => _engine;
    internal CancellationTokenSource? Cts => _cts;

    internal void SetEngine(ConversionEngine engine, CancellationTokenSource cts)
    {
        _engine = engine;
        _cts = cts;
    }
}
