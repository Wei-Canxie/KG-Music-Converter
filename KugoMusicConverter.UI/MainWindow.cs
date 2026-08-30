using System;
using System.Collections.ObjectModel;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using WinRT.Interop;

namespace KugoMusicConverter;

internal sealed class MainWindow : Window
{
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

    private double _blurRadius = 0;
    private string? _backgroundImagePath;
    private Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap? _originalBgImage;

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

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(null);

        BuildUI();
        ApplyAllSettings(settings);
    }

    private void BuildUI()
    {
        _rootGrid = new Grid();

        _bgImage = new Image
        {
            Stretch = Stretch.UniformToFill,
            Opacity = Settings.Load().BackgroundImageOpacity,
        };
        _rootGrid.Children.Add(_bgImage);

        var mainLayer = new Grid();
        mainLayer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32, GridUnitType.Pixel) });
        mainLayer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // ── 标题栏（32px，主题色跟随） ──
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
            Foreground = GetTitleBarForeground(),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
        };
        Grid.SetColumn(_titleText, 0);
        titlePanel.Children.Add(_titleText);

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

        // ── 侧边栏（LeftCompact 展开-收起式，默认收起） ──
        _nav = new NavigationView
        {
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            IsSettingsVisible = false,
            PaneDisplayMode = NavigationViewPaneDisplayMode.LeftCompact,
            OpenPaneLength = 200,
            CompactPaneLength = 48,
            IsPaneOpen = false,
        };
        _nav.MenuItems.Add(new NavigationViewItem { Content = "转换", Icon = new SymbolIcon(Symbol.Sync), Tag = "convert" });
        _nav.MenuItems.Add(new NavigationViewItem { Content = "设置", Icon = new SymbolIcon(Symbol.Setting), Tag = "settings" });
        _nav.MenuItems.Add(new NavigationViewItem { Content = "关于", Icon = new SymbolIcon(Symbol.OutlineStar), Tag = "about" });
        _nav.SelectionChanged += Nav_SelectionChanged;
        _nav.Loaded += (_, _) =>
        {
            try { _nav.SelectedItem = _nav.MenuItems[0]; }
            catch { }
            SyncSidebarBackground();
        };
        Grid.SetRow(_nav, 1);
        mainLayer.Children.Add(_nav);

        _rootGrid.Children.Add(mainLayer);
        Content = _rootGrid;

        _nav.Content = new ConvertControl(this);
    }

    // ── 侧边栏背景同步（OsuCursorWin3 模板：VisualTreeHelper 找 SplitView） ──

    private void SyncSidebarBackground()
    {
        try
        {
            var isDark = IsDarkTheme();
            if (_nav != null)
            {
                // 让菜单文字/图标颜色跟随主题（亮色→黑字）
                _nav.RequestedTheme = isDark ? ElementTheme.Dark : ElementTheme.Light;
            }

            var splitView = FindSplitViewPane(_nav);
            if (splitView?.Pane is not FrameworkElement pane) return;

            var bg = new SolidColorBrush(isDark
                ? ColorHelper.FromArgb(255, 0x2D, 0x2D, 0x2D)
                : Colors.White);

            if (pane is Panel panel)
            {
                panel.Background = bg;
            }
            else if (pane is Border border)
            {
                border.Background = bg;
                // 左侧直角贴窗边，右侧 12px 圆角
                border.CornerRadius = new CornerRadius(0, 12, 12, 0);
            }

            ApplyRoundedClip(pane);
        }
        catch { }
    }

    private static SplitView? FindSplitViewPane(DependencyObject? parent)
    {
        if (parent == null) return null;

        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is SplitView sv)
                return sv;

            var result = FindSplitViewPane(child);
            if (result != null) return result;
        }
        return null;
    }

    private static void ApplyRoundedClip(FrameworkElement pane)
    {
        try
        {
            var compositor = ElementCompositionPreview.GetElementVisual(pane).Compositor;
            var clip = compositor.CreateRectangleClip();
            clip.TopLeftRadius = new Vector2(0, 0);
            clip.TopRightRadius = new Vector2(12, 12);
            clip.BottomLeftRadius = new Vector2(0, 0);
            clip.BottomRightRadius = new Vector2(12, 12);
            SyncClipBounds(clip, pane);
            ElementCompositionPreview.GetElementVisual(pane).Clip = clip;

            pane.SizeChanged += (s, e) =>
            {
                try { SyncClipBounds(clip, pane); }
                catch { }
            };
        }
        catch { }
    }

    private static void SyncClipBounds(RectangleClip clip, FrameworkElement pane)
    {
        clip.Left = 0f;
        clip.Top = 0f;
        clip.Right = (float)pane.ActualWidth;
        clip.Bottom = (float)pane.ActualHeight;
    }

    private bool IsDarkTheme() => ThemeManager.Instance.IsDark;

    private Brush GetTitleBarForeground()
    {
        return IsDarkTheme() ? new SolidColorBrush(Colors.White) : new SolidColorBrush(Colors.Black);
    }

    private Brush GetTitleBarBrush(double opacity)
    {
        double titleOpacity = opacity <= 0.9 ? Math.Min(1.0, opacity + 0.1) : opacity;
        var isDark = IsDarkTheme();
        var color = isDark
            ? ColorHelper.FromArgb((byte)(titleOpacity * 255), 0x2D, 0x2D, 0x2D)
            : ColorHelper.FromArgb((byte)(titleOpacity * 255), 0xF3, 0xF3, 0xF3);
        return new SolidColorBrush(color);
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

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            _nav!.Content = BuildPage(tag);
        }
    }

    // ── 页面分发（OsuCursorWin3 模板：BuildPage(tag) 动态重建） ──

    private object BuildPage(string tag)
    {
        return tag switch
        {
            "convert" => new ConvertControl(this),
            "settings" => new SettingsControl(this),
            "about" => new AboutControl(this),
            _ => new ConvertControl(this)
        };
    }

    internal void ApplyAllSettings(Settings settings)
    {
        ThemeManager.Instance.AccentColor = ColorHelper.FromArgb(255, settings.ThemeR, settings.ThemeG, settings.ThemeB);
        ThemeManager.Instance.Mode = settings.Theme;

        // 关键：root 的 RequestedTheme 决定 NavigationView 菜单文字/图标颜色
        // （亮色主题下侧边栏字符才变黑）
        if (_rootGrid != null)
        {
            _rootGrid.RequestedTheme = settings.Theme switch
            {
                ThemeMode.Light => ElementTheme.Light,
                ThemeMode.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }

        ApplyBlurMode(settings.Blur, settings.Theme);
        ApplyBackgroundImage(settings.BackgroundImagePath);
        ApplyOpacity(settings.WindowOpacity, settings.PanelOpacity);

        if (_titleBar != null) _titleBar.Background = GetTitleBarBrush(settings.WindowOpacity);
        if (_titleText != null) _titleText.Foreground = GetTitleBarForeground();
        SyncSidebarBackground();
    }

    private void ApplyBlurMode(BlurMode blur, ThemeMode theme)
    {
        try
        {
            bool isDark = theme != ThemeMode.Light;
            var settings = Settings.Load();
            if (blur == BlurMode.Mica)
            {
                SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
                if (_rootGrid != null) _rootGrid.Background = new SolidColorBrush(Colors.Transparent);
            }
            else if (blur == BlurMode.Acrylic)
            {
                try
                {
                    SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
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

    internal void ApplyBlurRadius(double radius, BlurMode mode)
    {
        _blurRadius = radius;

        if (_bgImage != null && _originalBgImage != null)
        {
            if (radius > 0)
            {
                var blurred = GaussianBlurHelper.Blur(_originalBgImage, (int)radius);
                _bgImage.Source = blurred;
            }
            else
            {
                _bgImage.Source = _originalBgImage;
            }
            _bgImage.Opacity = Settings.Load().BackgroundImageOpacity;
        }
        else
        {
            ApplySystemBackdrop(mode);
        }
    }

    private void ApplySystemBackdrop(BlurMode mode)
    {
        if (_rootGrid == null) return;

        var oldSbe = _rootGrid.Children.FirstOrDefault(c => c is SystemBackdropElement);
        if (oldSbe != null) _rootGrid.Children.Remove(oldSbe);

        var oldOverlay = _rootGrid.Children.FirstOrDefault(c => c is Border b && b.Name == "BlurOverlay");
        if (oldOverlay != null) _rootGrid.Children.Remove(oldOverlay);

        if (mode == BlurMode.None) return;

        try
        {
            var sbe = new SystemBackdropElement
            {
                Name = "SystemBackdropElement",
                CornerRadius = new CornerRadius(0),
            };

            if (mode == BlurMode.Acrylic)
            {
                sbe.SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
            }
            else if (mode == BlurMode.Mica)
            {
                sbe.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
            }

            _rootGrid.Children.Insert(1, sbe);
        }
        catch
        {
            var overlay = new Border
            {
                Name = "BlurOverlay",
                Background = new SolidColorBrush(mode == BlurMode.Acrylic ? Colors.White : Colors.Black),
                Opacity = Math.Min(_blurRadius / 50.0, 0.8),
            };
            _rootGrid.Children.Insert(1, overlay);
        }
    }

    private void ApplyBackgroundImage(string? path)
    {
        if (_bgImage == null) return;

        _backgroundImagePath = path;

        if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
        {
            try
            {
                var wb = LoadImageToWriteableBitmap(path);
                if (wb != null)
                {
                    _originalBgImage = wb;
                    if (_blurRadius > 0)
                    {
                        var blurred = GaussianBlurHelper.Blur(wb, (int)_blurRadius);
                        _bgImage.Source = blurred;
                    }
                    else
                    {
                        _bgImage.Source = wb;
                    }
                    _bgImage.Opacity = Settings.Load().BackgroundImageOpacity;
                }
                else
                {
                    _bgImage.Source = null;
                    _originalBgImage = null;
                }
            }
            catch
            {
                _bgImage.Source = null;
                _originalBgImage = null;
            }
        }
        else
        {
            _bgImage.Source = null;
            _originalBgImage = null;
        }
    }

    private Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap? LoadImageToWriteableBitmap(string path)
    {
        try
        {
            using var stream = System.IO.File.OpenRead(path);
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
            bitmap.SetSource(stream.AsRandomAccessStream());
            var wb = new Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap((int)bitmap.PixelWidth, (int)bitmap.PixelHeight);
            using var fileStream = System.IO.File.OpenRead(path);
            wb.SetSource(fileStream.AsRandomAccessStream());
            return wb;
        }
        catch
        {
            return null;
        }
    }

    internal void ApplyOpacity(double windowOpacity, double panelOpacity)
    {
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

        if (_titleBar != null)
        {
            _titleBar.Background = GetTitleBarBrush(windowOpacity);
        }
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
