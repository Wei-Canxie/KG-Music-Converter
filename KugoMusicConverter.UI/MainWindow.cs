using System;
using System.Collections.ObjectModel;
using Microsoft.UI;
using Microsoft.UI.Text;
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

    private static Windows.UI.Color _themeColor = ColorHelper.FromArgb(255, 0xFF, 0x66, 0xAB);
    private static SolidColorBrush _themeBrush = new(_themeColor);

    private readonly ObservableCollection<FileEntry> _files = new();

    public static void SetThemeColor(byte r, byte g, byte b)
    {
        _themeColor = ColorHelper.FromArgb(255, r, g, b);
        _themeBrush = new SolidColorBrush(_themeColor);
    }

    public static SolidColorBrush ThemeBrushRef => _themeBrush;
    public static Windows.UI.Color ThemeColorRef => _themeColor;

    public MainWindow()
    {
        Title = "Kugo Music Converter — 酷狗加密音频解密工具箱";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 750));
        BuildUI();
    }

    private void BuildUI()
    {
        var root = new Grid { Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0x1E, 0x1E, 0x2A)) };

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

        // 默认显示转换页面
        _contentHost.Content = new ConvertControl(this);
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            _contentHost!.Content = tag switch
            {
                "convert" => new ConvertControl(this),
                "about" => new AboutControl(this),
                _ => new ConvertControl(this)
            };
        }
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
