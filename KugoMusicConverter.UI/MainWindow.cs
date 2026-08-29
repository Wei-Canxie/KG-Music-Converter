using System;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using WinRT.Interop;

namespace KugoMusicConverter;

internal sealed class MainWindow : Window
{
    // ── Win32 Interop ──
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x80000;
    private const int LWA_ALPHA = 0x2;

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 0x2;

    private ConversionEngine? _engine;
    private CancellationTokenSource? _cts;

    private NavigationView? _nav;
    private ContentControl? _contentHost;
    private Grid? _rootGrid;
    private Image? _bgImage;
    private Border? _titleBar;
    private TextBlock? _titleText;
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

    public static void SetThemeColor(byte r, byte g, byte b)
    {
        ThemeManager.Instance.AccentColor = ColorHelper.FromArgb(255, r, g, b);
    }

    public MainWindow()
    {
        Title = "Kugo Music Converter — 酷狗加密音频解密工具箱";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 750));

        var settings = Settings.Load();
        ThemeManager.Instance.AccentColor = ColorHelper.FromArgb(255, settings.ThemeR, settings.ThemeG, settings.ThemeB);
        ThemeManager.Instance.Mode = settings.Theme;

        // 自定义标题栏
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(null);

        BuildUI();
        ApplyAllSettings(settings);
    }

    private void BuildUI()
    {
        _rootGrid = new Grid();

        // 背景图（独立不透明度层）
        _bgImage = new Image
        {
            Stretch = Stretch.UniformToFill,
            Opacity = Settings.Load().BackgroundImageOpacity,
        };
        _rootGrid.Children.Add(_bgImage);

        // 主内容层
        var mainLayer = new Grid();
        mainLayer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32, GridUnitType.Pixel) }); // 标题栏
        mainLayer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 导航
        mainLayer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 内容

        // 自定义标题栏
        _titleBar = new Border
        {
            Background = GetTitleBarBrush(Settings.Load().WindowOpacity),
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _titleBar.AddHandler(
            Microsoft.UI.Xaml.UIElement.PointerPressedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler(TitleBar_PointerPressed),
            true);

        var titlePanel = new Grid();
        titlePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titlePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _titleText = new TextBlock
        {
            Text = "Kugo Music Converter — 酷狗加密音频解密工具箱",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeManager.Instance.Text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
        };
        Grid.SetColumn(_titleText, 0);
        titlePanel.Children.Add(_titleText);

        // 标题栏按钮
        var titleButtons = new StackPanel { Orientation = Orientation.Horizontal };
        var btnMin = new Button { Content = "🗕", FontSize = 12, Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(10, 0, 10, 0), Width = 40 };
        var btnMax = new Button { Content = "🗖", FontSize = 12, Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(10, 0, 10, 0), Width = 40 };
        var btnClose = new Button { Content = "✕", FontSize = 12, Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(10, 0, 10, 0), Width = 40 };
        btnMin.Click += (_, _) => AppWindow.Hide();
        btnMax.Click += (_, _) => { if (AppWindow.Presenter is OverlappedPresenter p) p.Maximize(); };
        btnClose.Click += (_, _) => Close();
        titleButtons.Children.Add(btnMin);
        titleButtons.Children.Add(btnMax);
        titleButtons.Children.Add(btnClose);
        Grid.SetColumn(titleButtons, 1);
        titlePanel.Children.Add(titleButtons);

        _titleBar.Child = titlePanel;
        Grid.SetRow(_titleBar, 0);
        mainLayer.Children.Add(_titleBar);

        // 导航
        _nav = new NavigationView
        {
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            IsSettingsVisible = false,
            PaneDisplayMode = NavigationViewPaneDisplayMode.Top,
        };
        _nav.MenuItems.Add(new NavigationViewItem { Content = "转换", Icon = new SymbolIcon(Symbol.Sync), Tag = "convert" });
        _nav.MenuItems.Add(new NavigationViewItem { Content = "设置", Icon = new SymbolIcon(Symbol.Setting), Tag = "settings" });
        _nav.MenuItems.Add(new NavigationViewItem { Content = "关于", Icon = new SymbolIcon(Symbol.OutlineStar), Tag = "about" });
        _nav.SelectionChanged += Nav_SelectionChanged;
        Grid.SetRow(_nav, 1);
        mainLayer.Children.Add(_nav);

        // 内容
        _contentHost = new ContentControl
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        Grid.SetRow(_contentHost, 2);
        mainLayer.Children.Add(_contentHost);

        _rootGrid.Children.Add(mainLayer);
        Content = _rootGrid;

        _contentHost.Content = new ConvertControl(this);
    }

    private void TitleBar_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            ReleaseCapture();
            SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }
        catch { }
    }

    private SolidColorBrush GetTitleBarBrush(double opacity)
    {
        // 标题栏透明度：<=90% 时 +10% 以造成差异效果
        double titleOpacity = opacity <= 0.9 ? Math.Min(1.0, opacity + 0.1) : opacity;
        var color = ThemeManager.Instance.Text;
        return new SolidColorBrush(ColorHelper.FromArgb((byte)(titleOpacity * 255), 0, 0, 0))
        {
            Opacity = titleOpacity,
        };
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
        ThemeManager.Instance.AccentColor = ColorHelper.FromArgb(255, settings.ThemeR, settings.ThemeG, settings.ThemeB);
        ThemeManager.Instance.Mode = settings.Theme;

        ApplyBlurMode(settings.Blur, settings.Theme);
        ApplyBackgroundImage(settings.BackgroundImagePath);
        ApplyOpacity(settings.WindowOpacity, settings.PanelOpacity);
    }

    private void ApplyBlurMode(BlurMode blur, ThemeMode theme)
    {
        try
        {
            bool isDark = theme != ThemeMode.Light;
            var settings = Settings.Load();
            if (blur == BlurMode.Mica)
            {
                SystemBackdrop = new MicaBackdrop { Kind = isDark ? MicaKind.Base : MicaKind.BaseAlt };
                if (_rootGrid != null) _rootGrid.Background = new SolidColorBrush(Colors.Transparent);
                ApplyMicaIntensity(settings.BlurIntensity);
            }
            else if (blur == BlurMode.Acrylic)
            {
                try
                {
                    SystemBackdrop = new DesktopAcrylicBackdrop();
                    if (_rootGrid != null) _rootGrid.Background = new SolidColorBrush(Colors.Transparent);
                }
                catch
                {
                    SystemBackdrop = null;
                }
            }
            else
            {
                SystemBackdrop = null;
                if (_rootGrid != null) _rootGrid.Background = ThemeManager.Instance.Background;
            }
        }
        catch
        {
            SystemBackdrop = null;
        }
    }

    private void ApplyMicaIntensity(double intensity)
    {
        if (_rootGrid == null) return;
        // 移除旧的覆盖层
        var oldOverlay = _rootGrid.Children.FirstOrDefault(c => c is Border b && b.Name == "MicaOverlay");
        if (oldOverlay != null) _rootGrid.Children.Remove(oldOverlay);

        if (intensity >= 1.0) return; // 无覆盖

        var overlay = new Border
        {
            Name = "MicaOverlay",
            Background = new SolidColorBrush(Colors.Black),
            Opacity = 1.0 - intensity,
        };
        _rootGrid.Children.Insert(1, overlay);
    }

    private void ApplyBackgroundImage(string? path)
    {
        if (_bgImage == null) return;

        if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
        {
            try
            {
                var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                using var stream = System.IO.File.OpenRead(path);
                bitmap.SetSource(stream.AsRandomAccessStream());
                _bgImage.Source = bitmap;
                _bgImage.Opacity = Settings.Load().BackgroundImageOpacity;
            }
            catch
            {
                _bgImage.Source = null;
            }
        }
        else
        {
            _bgImage.Source = null;
        }
    }

    internal void ApplyOpacity(double windowOpacity, double panelOpacity)
    {
        // 窗口整体不透明度：Win32 SetLayeredWindowAttributes
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            if ((exStyle & WS_EX_LAYERED) == 0)
            {
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_LAYERED);
            }
            byte alpha = (byte)(windowOpacity * 255);
            SetLayeredWindowAttributes(hwnd, 0, alpha, LWA_ALPHA);
        }
        catch { }

        // 标题栏不透明度
        if (_titleBar != null)
        {
            _titleBar.Background = GetTitleBarBrush(windowOpacity);
        }

        // Panel 不透明度
        ThemeManager.Instance.PanelOpacity = panelOpacity;
    }

    internal void SetBackgroundImageOpacity(double opacity)
    {
        if (_bgImage != null) _bgImage.Opacity = opacity;
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
