using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using WinRT.Interop;
using Windows.Foundation;

namespace KGMusicConverter;

/// <summary>
/// 工具外壳：32px 自绘标题栏 + <see cref="NavigationView"/>（LeftCompact 展开/收起），
/// 页面在 C# 里现场构建，外观设置走"草稿 + 应用/取消更改"模型。
///
/// 窗口从不直接拥有它编辑的设置：它在一份草稿（<c>_draft</c>）上工作，
/// 只有用户按下"应用"时才写回运行时实例（<c>_live</c>）与磁盘；
/// <c>_applied</c> 是"取消更改"要回滚到的快照。
/// </summary>
internal sealed class MainWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x80000;
    private const int LWA_ALPHA = 0x2;
    private const double TitleBarHeight = 32;
    private const double CompactPaneLength = 48;
    private const double OpenPaneLength = 200;
    private const double PaneAnimationMs = 120;

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

    // ── 设置：运行时真身 / 草稿 / 快照 ──
    private readonly Settings _live;
    private Settings _draft;
    private Settings _applied;

    // ── 运行状态（属于应用而不是某个页面，页面重建不丢） ──
    private ConversionEngine? _engine;
    private CancellationTokenSource? _cts;
    private readonly ObservableCollection<FileEntry> _files = new();
    private readonly List<string> _logLines = new();
    private readonly object _logLock = new();
    private readonly Dictionary<string, double> _scrollCache = new();
    private bool _isRunning;

    // ── 视觉元素 ──
    private NavigationView? _nav;
    private Grid? _rootGrid;
    private Image? _bgImage;
    private Border? _titleBar;
    private TextBlock? _titleText;
    private Border? _applyBar;
    private TextBlock? _applyBarText;
    private ToolPage? _currentPage;
    private string _currentTag = "convert";
    private InboxWatcher? _inboxWatcher;
    private FrameworkElement? _clippedPane;
    private bool _paneAnimationHooked;

    // ── 转换页控件（页面构建时由 ConvertControl 挂上） ──
    private TextBox? _logBox;
    private ProgressBar? _progressBar;
    private TextBlock? _progressLabel;
    private Button? _startButton;
    private Button? _cancelButton;
    private ListView? _queueList;
    private CheckBox? _skipCopyCheck;
    private CheckBox? _convertMp3Check;
    private CheckBox? _convertWavCheck;
    private CheckBox? _convertFlacCheck;
    private CheckBox? _deleteSourceCheck;
    private CheckBox? _unifiedOutputCheck;
    private TextBox? _unifiedOutputBox;
    private Button? _browseOutputButton;
    private TextBlock? _kggWarning;
    private Button? _clearCompletedButton;
    private Button? _formatToolButton;

    private double _blurRadius;
    private Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap? _originalBgImage;

    /// <summary>运行状态 / 日志 / 队列发生变化，页面据此刷新显示。</summary>
    internal event Action? RunStateChanged;

    public MainWindow()
    {
        Title = "KG Music Converter — 酷狗加密音频解密工具箱";
        // 转换页现在有 6 个运行选项 + 格式整理工具按钮，750 高会把按钮挤到折叠区外
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 900));

        _live = Settings.Load();
        _draft = _live.Clone();
        _applied = _live.Clone();

        ThemeManager.Instance.AccentColor = ColorHelper.FromArgb(255, _live.ThemeR, _live.ThemeG, _live.ThemeB);
        ThemeManager.Instance.Mode = _live.Theme;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(null);

        // 应用专属工作区：引擎在这里，中间产物也在这里（音乐文件夹不再被污染）
        Workspace.EnsureCreated();
        Workspace.SeedEngines();

        // 上次若是被强杀（进程已不在但会话标记还在），遗留的临时文件直接清掉，不打扰用户
        int recoveredTempFiles = 0;
        if (Workspace.WasPreviousSessionKilled())
        {
            var (removed, _) = Workspace.CleanTempFiles();
            recoveredTempFiles = removed;
            AppLog.Log($"上次会话未正常结束，已清理遗留临时文件 {removed} 个");
        }
        Workspace.MarkSessionStarted();

        BuildUI();
        ApplyAppearance(_live, rebuildPage: false);

        StartInboxWatcher();





        // 启动横幅：让日志栏一开始就有上下文，也顺手报出引擎是否就位
        AppendLog("KG Music Converter 启动");
        AppendLog($"  工作区: {Workspace.Root}");
        AppendLog($"  收件箱: {Workspace.Inbox}");
        AppendLog($"  成品目录: {Workspace.Output}");

        if (recoveredTempFiles > 0)
        {
            AppendLog($"  ℹ 上次未正常退出，已自动清理遗留临时文件 {recoveredTempFiles} 个");
        }

        var missingEngines = Workspace.MissingEngines();
        if (missingEngines.Count == 0)
        {
            AppendLog("  解密引擎: 就绪");
        }
        else
        {
            AppendLog($"  ⚠ 缺少解密引擎: {string.Join("、", missingEngines)}（请放到程序目录，程序会自动复制进工作区）");
        }

        AppWindow.Closing += (_, _) =>
        {
            // 一次性工具：关窗即退出（不做托盘驻留），退出前把临时文件清干净
            try
            {
                _cts?.Cancel();              // 正在转换时先停手，别跟清理抢文件
                _inboxWatcher?.Dispose();

                var (removed, failed) = Workspace.CleanTempFiles();
                AppLog.Log(failed > 0
                    ? $"窗口关闭：清理临时文件 {removed} 个（{failed} 个被占用，留给下次启动）"
                    : $"窗口关闭：清理临时文件 {removed} 个");

                // 全清干净才撤掉会话标记；否则留着让下次启动兜底清理
                if (failed == 0) Workspace.MarkSessionEnded();
            }
            catch (Exception ex)
            {
                AppLog.Log($"关机清理失败: {ex.Message}");
            }
        };
    }

    /// <summary>收件箱热文件夹：丢进去的文件自动入队。</summary>
    private void StartInboxWatcher()
    {
        try
        {
            _inboxWatcher = new InboxWatcher(paths =>
            {
                if (paths.Count == 0) return;
                AddFiles(paths);
                AppendLog($"📥 收件箱新增 {paths.Count} 个文件，已加入队列\n");
            });

            // 程序没开着的时候丢进去的文件，启动时补捞一次
            var existing = Workspace.ScanInbox();
            if (existing.Count > 0)
            {
                AddFiles(existing);
                AppendLog($"📥 收件箱已有 {existing.Count} 个文件，已加入队列\n");
            }
        }
        catch (Exception ex)
        {
            AppLog.Log($"StartInboxWatcher failed: {ex}");
        }
    }

    /// <summary>在资源管理器里打开收件箱。</summary>
    internal void OpenInbox() => OpenFolder(Workspace.Inbox);

    /// <summary>在资源管理器里打开工作区。</summary>
    internal void OpenWorkspace() => OpenFolder(Workspace.Root);

    /// <summary>在资源管理器里打开成品目录（收件箱文件的成品落在这里）。</summary>
    internal void OpenOutputFolder() => OpenFolder(Workspace.Output);

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppLog.Log($"OpenFolder({path}) failed: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────── 外壳

    private void BuildUI()
    {
        _rootGrid = new Grid();

        _bgImage = new Image
        {
            Stretch = Stretch.UniformToFill,
            Opacity = _live.BackgroundImageOpacity,
        };
        _rootGrid.Children.Add(_bgImage);

        var mainLayer = new Grid();
        mainLayer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TitleBarHeight, GridUnitType.Pixel) });
        mainLayer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        BuildTitleBar();
        Grid.SetRow(_titleBar!, 0);
        mainLayer.Children.Add(_titleBar!);

        BuildNavigation();
        Grid.SetRow(_nav!, 1);
        mainLayer.Children.Add(_nav!);

        BuildApplyBar();
        Grid.SetRow(_applyBar!, 1);
        mainLayer.Children.Add(_applyBar!);

        _rootGrid.Children.Add(mainLayer);
        Content = _rootGrid;

        Navigate("convert", record: false);
    }

    private void BuildTitleBar()
    {
        _titleBar = new Border
        {
            Background = GetTitleBarBrush(_live),
            Height = TitleBarHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _titleBar.AddHandler(
            UIElement.PointerPressedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler(TitleBar_PointerPressed),
            true);

        var titlePanel = new Grid();
        titlePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titlePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _titleText = new TextBlock
        {
            Text = "KG Music Converter — 酷狗加密音频解密工具箱",
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
    }

    private void BuildNavigation()
    {
        _nav = new NavigationView
        {
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            IsSettingsVisible = false,
            PaneDisplayMode = NavigationViewPaneDisplayMode.LeftCompact,
            OpenPaneLength = OpenPaneLength,
            CompactPaneLength = CompactPaneLength,
            IsPaneOpen = false,
        };
        _nav.MenuItems.Add(new NavigationViewItem { Content = "转换", Icon = new SymbolIcon(Symbol.Sync), Tag = "convert" });
        _nav.MenuItems.Add(new NavigationViewItem { Content = "设置", Icon = new SymbolIcon(Symbol.Setting), Tag = "settings" });
        _nav.MenuItems.Add(new NavigationViewItem { Content = "关于", Icon = new SymbolIcon(Symbol.OutlineStar), Tag = "about" });
        _nav.SelectionChanged += Nav_SelectionChanged;
        _nav.Loaded += (_, _) =>
        {
            // 只在没有选中项时兜底，别覆盖已经导航过的页面
            if (_nav.SelectedItem is null)
            {
                try { _nav.SelectedItem = _nav.MenuItems[0]; }
                catch (Exception ex) { AppLog.Log($"Set default nav item failed: {ex.Message}"); }
            }
            SyncSidebarBackground();
            HookPaneAnimation();
        };
    }

    /// <summary>右下角浮动的"应用 / 取消更改"卡片（不占布局行）。</summary>
    private void BuildApplyBar()
    {
        var applyButton = new Button
        {
            Content = "应用",
            MinWidth = 96,
            Style = Application.Current.Resources["AccentButtonStyle"] as Style,
        };
        var cancelButton = new Button { Content = "取消更改", MinWidth = 96 };

        applyButton.Click += (_, _) => ApplyDraft();
        cancelButton.Click += (_, _) => CancelDraft();

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row.Children.Add(cancelButton);
        row.Children.Add(applyButton);

        _applyBarText = new TextBlock
        {
            Text = "有未应用的更改",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        row.Children.Insert(0, _applyBarText);

        _applyBar = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 0, 24, 24),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Visibility = Visibility.Collapsed,
            Child = row,
        };
        UpdateApplyBarTheme();
    }

    // ─────────────────────────────────────────────────────── 页面生命周期

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            Navigate(tag);
        }
    }

    /// <summary>
    /// 切换页面：先把旧页面的滚动位置记下来并注销它的事件处理器，
    /// 再构建新页。页面不缓存——重建才能保证控件状态总是最新的。
    /// </summary>
    private void Navigate(string tag, bool record = true)
    {
        try
        {
            if (record && _currentPage is not null)
            {
                SaveScrollOffset(_currentTag);
                _currentPage.Dispose();
            }
            else if (!record && _currentPage is not null)
            {
                _currentPage.Dispose();
            }

            _currentTag = tag;
            _currentPage = BuildPage(tag);
            if (_nav is not null) _nav.Content = _currentPage;

            RestoreScrollOffset(tag);
        }
        catch (Exception ex)
        {
            AppLog.Log($"Navigate({tag}) failed: {ex}");
        }
    }

    private ToolPage BuildPage(string tag) => tag switch
    {
        "settings" => new SettingsControl(this),
        "about" => new AboutControl(this),
        _ => new ConvertControl(this),
    };

    private void SaveScrollOffset(string tag)
    {
        var offset = _currentPage?.FindScrollOffset();
        if (offset is > 0) _scrollCache[tag] = offset.Value;
    }

    private void RestoreScrollOffset(string tag)
    {
        if (!_scrollCache.TryGetValue(tag, out var offset) || offset <= 0) return;
        var page = _currentPage;
        if (page is null) return;

        // 布局完成后再滚动，否则 ScrollableHeight 还是 0
        page.Loaded += (_, _) =>
        {
            try { page.ScrollToOffset(offset); }
            catch (Exception ex) { AppLog.Log($"Restore scroll failed: {ex.Message}"); }
        };
    }

    internal double GetScrollOffset(string tag) =>
        _scrollCache.TryGetValue(tag, out var value) ? value : 0;

    internal void SetScrollOffset(string tag, double offset) => _scrollCache[tag] = offset;

    /// <summary>重建当前页（主题 / 主题色变化后需要，控件颜色才会跟着换）。</summary>
    internal void RebuildCurrentPage() => Navigate(_currentTag, record: false);

    // ─────────────────────────────────────────────────────── 草稿 / 应用 / 取消

    internal Settings Draft => _draft;

    internal Settings Live => _live;

    /// <summary>
    /// 设置页改了草稿：只浮出"应用 / 取消更改"卡片。
    ///
    /// 刻意<b>不做</b>即时预览——外观只在按下"应用"之后才真正改变窗口，
    /// 所以这里不碰 ThemeManager / 不透明度 / 背景，也不重建页面。
    /// </summary>
    internal void MarkDirty()
    {
        if (_applyBar is not null) _applyBar.Visibility = Visibility.Visible;
    }

    /// <summary>应用：草稿 → 运行时实例 → 落盘 → 快照 → 应用外观 → 重建页面。</summary>
    internal void ApplyDraft()
    {
        try
        {
            _live.CopyFrom(_draft);
            _live.Save();
            _applied = _live.Clone();
            _draft = _live.Clone();

            ApplyAppearance(_live, rebuildPage: true);
            HideApplyBar();
            AppLog.Log("Settings applied");
        }
        catch (Exception ex)
        {
            AppLog.Log($"ApplyDraft failed: {ex}");
        }
    }

    /// <summary>
    /// 取消更改：从快照回滚草稿并重建页面。
    /// 窗口外观一直停留在"已应用"状态，所以不需要重新套一遍外观。
    /// </summary>
    internal void CancelDraft()
    {
        try
        {
            _draft = _applied.Clone();
            RebuildCurrentPage();
            HideApplyBar();
            AppLog.Log("Settings changes reverted");
        }
        catch (Exception ex)
        {
            AppLog.Log($"CancelDraft failed: {ex}");
        }
    }

    private void HideApplyBar()
    {
        if (_applyBar is not null) _applyBar.Visibility = Visibility.Collapsed;
    }

    // ─────────────────────────────────────────────────────── 外观

    /// <summary>
    /// 把设置套到窗口上：主题 → 背景材质 → 不透明度 → 标题栏 → 侧边栏。
    /// 只在"应用"更改时调用（<paramref name="rebuildPage"/> 为真 → 重建当前页，
    /// 已创建控件上的画刷才会换成新主题的颜色）。
    /// </summary>
    internal void ApplyAppearance(Settings settings, bool rebuildPage)
    {
        try
        {
            ThemeManager.Instance.AccentColor = ColorHelper.FromArgb(255, settings.ThemeR, settings.ThemeG, settings.ThemeB);
            ThemeManager.Instance.Mode = settings.Theme;

            // root 的 RequestedTheme 决定 NavigationView 菜单文字/图标颜色
            if (_rootGrid is not null)
            {
                _rootGrid.RequestedTheme = settings.Theme switch
                {
                    ThemeMode.Light => ElementTheme.Light,
                    ThemeMode.Dark => ElementTheme.Dark,
                    _ => ElementTheme.Default,
                };
            }

            ApplyBackground(settings);
            ApplyOpacity(settings);

            if (_titleBar is not null) _titleBar.Background = GetTitleBarBrush(settings);
            if (_titleText is not null) _titleText.Foreground = GetTitleBarForeground();

            UpdateApplyBarTheme();
            SyncSidebarBackground();

            if (rebuildPage) Navigate(_currentTag, record: false);
        }
        catch (Exception ex)
        {
            AppLog.Log($"ApplyAppearance failed: {ex}");
        }
    }

    /// <summary>云母 / 亚克力激活时，背景材质自己拥有窗口表面。</summary>
    private static bool IsMaterialActive(Settings settings) => settings.Blur != BlurMode.None;

    private void ApplyBackground(Settings settings)
    {
        try
        {
            if (IsMaterialActive(settings))
            {
                SystemBackdrop = settings.Blur == BlurMode.Acrylic
                    ? new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop()
                    : new Microsoft.UI.Xaml.Media.MicaBackdrop();
                if (_rootGrid is not null) _rootGrid.Background = new SolidColorBrush(Colors.Transparent);
            }
            else
            {
                SystemBackdrop = null;
                if (_rootGrid is not null) _rootGrid.Background = ThemeManager.Instance.Background;
            }
        }
        catch (Exception ex)
        {
            AppLog.Log($"ApplyBackground failed: {ex.Message}");
            SystemBackdrop = null;
        }

        ApplyBackgroundImage(settings);
    }

    private void ApplyBackgroundImage(Settings settings)
    {
        if (_bgImage is null) return;

        // 云母 / 亚克力模式下背景图会直接盖住材质，等于把材质白设了 —— 不显示
        if (IsMaterialActive(settings))
        {
            _bgImage.Source = null;
            return;
        }

        var path = settings.BackgroundImagePath;
        _blurRadius = settings.BlurRadius;

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            _bgImage.Source = null;
            _originalBgImage = null;
            return;
        }

        try
        {
            var original = LoadImageToWriteableBitmap(path);
            if (original is null)
            {
                _bgImage.Source = null;
                _originalBgImage = null;
                return;
            }

            _originalBgImage = original;
            _bgImage.Source = GaussianBlurHelper.BlurIfNeeded(original, (int)settings.BlurRadius);
            _bgImage.Opacity = settings.BackgroundImageOpacity;
        }
        catch (Exception ex)
        {
            AppLog.Log($"ApplyBackgroundImage failed: {ex.Message}");
            _bgImage.Source = null;
            _originalBgImage = null;
        }
    }

    private static Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap? LoadImageToWriteableBitmap(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
            bitmap.SetSource(stream.AsRandomAccessStream());
            var wb = new Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap((int)bitmap.PixelWidth, (int)bitmap.PixelHeight);
            using var fileStream = File.OpenRead(path);
            wb.SetSource(fileStream.AsRandomAccessStream());
            return wb;
        }
        catch (Exception ex)
        {
            AppLog.Log($"LoadImageToWriteableBitmap failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 窗口不透明度：WinUI 3 窗口没有 Opacity 属性，alpha 只能挂在 Win32 窗口上。
    /// 材质模式下写死不透明——否则会把材质一起淡掉。
    /// </summary>
    internal void ApplyOpacity(Settings settings)
    {
        double opacity = IsMaterialActive(settings) ? 1.0 : settings.WindowOpacity;

        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            if ((exStyle & WS_EX_LAYERED) == 0)
            {
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_LAYERED);
            }
            SetLayeredWindowAttributes(hwnd, 0, (byte)Math.Clamp(opacity * 255, 25, 255), LWA_ALPHA);
        }
        catch (Exception ex)
        {
            AppLog.Log($"ApplyOpacity failed: {ex.Message}");
        }

        if (_titleBar is not null) _titleBar.Background = GetTitleBarBrush(settings);
    }

    private bool IsDarkTheme() => ThemeManager.Instance.IsDark;

    private Brush GetTitleBarForeground() =>
        IsDarkTheme() ? new SolidColorBrush(Colors.White) : new SolidColorBrush(Colors.Black);

    private Brush GetTitleBarBrush(Settings settings)
    {
        // 材质模式与 100% 不透明度下标题栏实心；否则比主体略高一点 alpha，
        // 保证标题文字始终可读。
        double opacity = IsMaterialActive(settings) ? 1.0 : settings.WindowOpacity;
        double titleOpacity = opacity <= 0.9 ? Math.Min(1.0, opacity + 0.1) : opacity;

        var color = IsDarkTheme()
            ? ColorHelper.FromArgb((byte)(titleOpacity * 255), 0x2D, 0x2D, 0x2D)
            : ColorHelper.FromArgb((byte)(titleOpacity * 255), 0xF3, 0xF3, 0xF3);
        return new SolidColorBrush(color);
    }

    private void UpdateApplyBarTheme()
    {
        if (_applyBar is null) return;

        var isDark = IsDarkTheme();
        _applyBar.Background = new SolidColorBrush(isDark
            ? ColorHelper.FromArgb(0xF0, 0x2D, 0x2D, 0x2D)
            : ColorHelper.FromArgb(0xF0, 0xF3, 0xF3, 0xF3));
        _applyBar.BorderThickness = new Thickness(1);
        _applyBar.BorderBrush = new SolidColorBrush(isDark
            ? ColorHelper.FromArgb(0x40, 0xFF, 0xFF, 0xFF)
            : ColorHelper.FromArgb(0x30, 0x00, 0x00, 0x00));
        if (_applyBarText is not null)
        {
            _applyBarText.Foreground = isDark
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Colors.Black);
        }
    }

    private void TitleBar_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            ReleaseCapture();
            SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            AppLog.Log($"Title bar drag failed: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────── 侧边栏

    private void SyncSidebarBackground()
    {
        try
        {
            var isDark = IsDarkTheme();
            if (_nav is not null)
            {
                _nav.RequestedTheme = isDark ? ElementTheme.Dark : ElementTheme.Light;
            }

            var splitView = FindSplitView(_nav);
            if (splitView?.Pane is not FrameworkElement pane) return;

            var background = new SolidColorBrush(isDark
                ? ColorHelper.FromArgb(255, 0x2D, 0x2D, 0x2D)
                : Colors.White);

            switch (pane)
            {
                case Panel panel: panel.Background = background; break;
                case Border border:
                    border.Background = background;
                    border.CornerRadius = new CornerRadius(0, 12, 12, 0);
                    break;
                default:
                    AppLog.Log($"SyncSidebar: unexpected pane type {pane.GetType().Name}");
                    break;
            }

            // 模板给 pane 留了 3px 边距 + 1px 宿主边框，会在侧边栏上下留出发丝缝。
            // 抹平 pane 及其到 SplitView 之间的祖先，只保留 1px 宿主内缩。
            pane.Margin = new Thickness(0);
            FlattenPaneAncestors(pane, splitView);

            ApplyRoundedPaneClip(pane);
        }
        catch (Exception ex)
        {
            AppLog.Log($"SyncSidebarBackground failed: {ex.Message}");
        }
    }

    private static void FlattenPaneAncestors(DependencyObject pane, DependencyObject? stopAt)
    {
        try
        {
            var parent = VisualTreeHelper.GetParent(pane);
            while (parent is not null && parent != stopAt)
            {
                switch (parent)
                {
                    case Border border:
                        border.Margin = new Thickness(0);
                        border.Padding = new Thickness(0);
                        border.BorderThickness = new Thickness(0);
                        border.Background = new SolidColorBrush(Colors.Transparent);
                        break;
                    case Panel panel:
                        panel.Margin = new Thickness(0);
                        panel.Background = new SolidColorBrush(Colors.Transparent);
                        break;
                    case ContentPresenter presenter:
                        presenter.Margin = new Thickness(0);
                        break;
                }

                parent = VisualTreeHelper.GetParent(parent);
            }
        }
        catch (Exception ex)
        {
            AppLog.Log($"FlattenPaneAncestors failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 圆角裁剪只能来自 Composition 层（XAML 的 RectangleGeometry 没有 CornerRadius）。
    /// 同一个 pane 只裁一次——重复裁会不断叠加 SizeChanged 订阅。
    /// </summary>
    private void ApplyRoundedPaneClip(FrameworkElement pane)
    {
        if (ReferenceEquals(_clippedPane, pane)) return;

        try
        {
            _clippedPane = pane;

            var visual = ElementCompositionPreview.GetElementVisual(pane);
            var clip = visual.Compositor.CreateRectangleClip();
            clip.TopLeftRadius = Vector2.Zero;
            clip.BottomLeftRadius = Vector2.Zero;
            clip.TopRightRadius = new Vector2(12, 12);
            clip.BottomRightRadius = new Vector2(12, 12);

            SyncClipBounds(clip, pane);
            visual.Clip = clip;

            pane.SizeChanged += (_, _) =>
            {
                try { SyncClipBounds(clip, pane); }
                catch (Exception ex) { AppLog.Log($"Pane clip resize failed: {ex.Message}"); }
            };
        }
        catch (Exception ex)
        {
            AppLog.Log($"ApplyRoundedPaneClip failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 裁剪盒保持 pane 的真实尺寸。零尺寸的裁剪会把整条侧边栏藏掉，
    /// 视觉状态切换时会闪几帧空白——所以非正值直接跳过，保留上一份好数据。
    /// </summary>
    private static void SyncClipBounds(Microsoft.UI.Composition.RectangleClip clip, FrameworkElement pane)
    {
        double width = pane.ActualWidth, height = pane.ActualHeight;
        if (!(width > 0) || !(height > 0)) return;

        clip.Left = 0f;
        clip.Top = 0f;
        clip.Right = (float)width;
        clip.Bottom = (float)height;
    }

    /// <summary>
    /// 接上侧边栏收起动画。模板自带的收起只有 120ms 且同时把 pane 压成 48px，
    /// 滑动过程完全看不见；所以这里不跟模板抢属性，只动画 pane 自身宽度。
    /// </summary>
    private void HookPaneAnimation()
    {
        if (_paneAnimationHooked || _nav is null) return;
        _paneAnimationHooked = true;

        try
        {
            _nav.RegisterPropertyChangedCallback(
                NavigationView.IsPaneOpenProperty,
                (_, _) => OnPaneOpenChanged());
        }
        catch (Exception ex)
        {
            AppLog.Log($"HookPaneAnimation failed: {ex.Message}");
        }
    }

    private void OnPaneOpenChanged()
    {
        if (_nav is null) return;

        try
        {
            var splitView = FindSplitView(_nav);
            if (splitView?.Pane is not FrameworkElement pane) return;

            if (_nav.IsPaneOpen)
            {
                // 展开：把钉住的宽度还给模板，让它自己滑开
                pane.ClearValue(FrameworkElement.WidthProperty);
                return;
            }

            double from = pane.ActualWidth;
            if (!(from > CompactPaneLength)) return;

            // 先钉住当前宽度作为动画起点（SplitView 平时不设 Width，是 NaN）
            pane.Width = from;

            var animation = new DoubleAnimationUsingKeyFrames
            {
                Duration = new Duration(TimeSpan.FromMilliseconds(PaneAnimationMs)),
                EnableDependentAnimation = true,
            };
            animation.KeyFrames.Add(new SplineDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(PaneAnimationMs)),
                Value = CompactPaneLength,
                KeySpline = new KeySpline
                {
                    ControlPoint1 = new Point(0.1, 0.9),
                    ControlPoint2 = new Point(0.2, 1.0),
                },
            });

            Storyboard.SetTarget(animation, pane);
            Storyboard.SetTargetProperty(animation, "Width");

            var storyboard = new Storyboard();
            storyboard.Children.Add(animation);
            storyboard.Completed += (_, _) =>
            {
                try { pane.ClearValue(FrameworkElement.WidthProperty); }
                catch (Exception ex) { AppLog.Log($"Pane animation cleanup failed: {ex.Message}"); }
            };
            storyboard.Begin();
        }
        catch (Exception ex)
        {
            AppLog.Log($"Pane close animation failed: {ex.Message}");
        }
    }

    private static SplitView? FindSplitView(DependencyObject? parent)
    {
        if (parent is null) return null;

        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is SplitView splitView) return splitView;

            var nested = FindSplitView(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    // ─────────────────────────────────────────────────────── 运行状态（属于应用）

    internal ObservableCollection<FileEntry> Files => _files;
    internal bool IsRunning => _isRunning;
    /// <summary>
    /// 日志栏的完整文本（切页重建时用来还原内容）。
    ///
    /// <c>_logLines</c> 里存的是不带换行的裸行，运行时追加走 <see cref="AppendLog"/> 的
    /// 拼接，这里必须用同一个 <see cref="LogFormat.EntrySeparator"/>，
    /// 否则切页回来所有行会粘成一坨（连普通换行都没有）。
    /// </summary>
    internal string LogText
    {
        get
        {
            lock (_logLock)
            {
                if (_logLines.Count == 0) return string.Empty;
                return string.Join(LogFormat.EntrySeparator, _logLines) + LogFormat.EntrySeparator;
            }
        }
    }
    internal string? ProgressLabelText { get; private set; }
    internal double ProgressValue { get; private set; }
    internal ConversionEngine? Engine => _engine;
    internal CancellationTokenSource? Cts => _cts;

    /// <summary>
    /// 往界面日志栏追加内容。
    ///
    /// 每一行都带 V2rayN 风格前缀：<c>2026/09/13 13:21:13.684611 [Info] [123456] 正文</c>
    /// —— 时间戳（6 位小数）/ 级别 / 会话号。同一行也落一份到诊断文件。
    /// 引擎回调来自后台线程，UI 部分统一走 RunOnUi。
    /// </summary>
    internal void AppendLog(string message)
    {
        var formattedLines = new List<string>();

        foreach (var line in LogFormat.SplitLines(message))
        {
            var formatted = LogFormat.Line(line, LogFormat.InferLevel(line));
            lock (_logLock) _logLines.Add(formatted);
            AppLog.WriteFormatted(formatted);
            formattedLines.Add(formatted);
        }

        if (formattedLines.Count == 0) return;

        // 每条目后留一个空行：阶段标题、进度行各自成段，扫读时不再糊成一片
        //（分隔符与 LogText 重建路径共用，见 LogFormat.EntrySeparator）
        var text = string.Join(LogFormat.EntrySeparator, formattedLines) + LogFormat.EntrySeparator;

        RunOnUi(() =>
        {
            if (_logBox is null) return;
            _logBox.Text += text;
            try
            {
                var viewer = FindVisualChild<ScrollViewer>(_logBox);
                viewer?.ChangeView(null, viewer.ScrollableHeight, null);
            }
            catch (Exception ex)
            {
                AppLog.Log($"Log autoscroll failed: {ex.Message}");
            }
        });

        NotifyRunState();
    }

    internal void ClearLog()
    {
        lock (_logLock) _logLines.Clear();
        RunOnUi(() => { if (_logBox is not null) _logBox.Text = ""; });
    }

    /// <summary>
    /// 引擎的日志/进度/完成回调都来自后台线程（阶段跑在 Task.Run 里），
    /// 触碰任何 UI 控件都必须回到 UI 线程 —— 否则 WinUI 抛
    /// 0x8001010E (RPC_E_WRONG_THREAD)，异常会顺着回调把整个阶段带崩。
    /// </summary>
    internal void RunOnUi(Action action)
    {
        var queue = DispatcherQueue;
        if (queue is null || queue.HasThreadAccess)
        {
            action();
            return;
        }

        queue.TryEnqueue(() =>
        {
            try { action(); }
            catch (Exception ex) { AppLog.Log($"UI callback failed: {ex}"); }
        });
    }

    private void NotifyRunState() => RunOnUi(() => RunStateChanged?.Invoke());

    internal void AddFiles(IEnumerable<string> paths)
    {
        var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".kgg", ".kgm", ".kgma", ".vpr", ".flac" };

        foreach (var path in paths)
        {
            try
            {
                if (!File.Exists(path)) continue;
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (!supported.Contains(ext)) continue;
                if (_files.Any(f => string.Equals(f.SourcePath, path, StringComparison.OrdinalIgnoreCase))) continue;

                _files.Add(new FileEntry
                {
                    SourcePath = path,
                    FileName = Path.GetFileName(path),
                    SourceDirectory = Path.GetDirectoryName(path) ?? "",
                    BaseName = Path.GetFileNameWithoutExtension(path),
                    Extension = ext,
                    Status = FileStatus.Pending,
                });
            }
            catch (Exception ex)
            {
                AppLog.Log($"AddFiles({path}) failed: {ex.Message}");
            }
        }

        NotifyRunState();
    }

    internal void RemoveCompletedFiles()
    {
        var finished = _files
            .Where(f => f.Status is FileStatus.Completed or FileStatus.Failed)
            .ToList();
        foreach (var entry in finished) _files.Remove(entry);
        NotifyRunState();
    }

    /// <summary>
    /// 启动转换。引擎归窗口所有，页面只是视图——所以切到别的页
    /// 或者页面被重建都不会让运行中的进度更新丢失。
    /// </summary>
    internal async void StartConversion()
    {
        if (_isRunning || _files.Count == 0) return;

        var options = new ConversionOptions
        {
            ConvertMp3 = _live.ConvertMp3,
            ConvertWav = _live.ConvertWav,
            ConvertFlac = _live.ConvertFlac,
            DeleteSourceFile = _live.DeleteSourceFile,
            UseUnifiedOutput = _live.UseUnifiedOutput,
            UnifiedOutputDir = _live.UnifiedOutputDir?.Trim() ?? "",
        };
        bool useUnified = options.UseUnifiedOutput;
        string unifiedDir = options.UnifiedOutputDir;

        // 引擎与中间产物都在应用工作区里，先确保引擎就位
        Workspace.EnsureCreated();
        Workspace.SeedEngines();

        var missing = Workspace.MissingEngines()
            .Where(name =>
                (name != "ffmpeg.exe" || options.AnyConvert) &&
                (name != "kgg-dec.exe" || _files.Any(f => f.Extension == ".kgg")))
            .ToList();

        if (missing.Count > 0)
        {
            AppendLog($"✗ 缺少解密引擎：{string.Join("、", missing)}\n");
            AppendLog($"  请把引擎放到程序目录，程序会自动复制进工作区：\n    {AppContext.BaseDirectory}\n");
            AppendLog($"  或直接放进工作区：\n    {Workspace.Root}\n\n");
            return;
        }

        if (useUnified && string.IsNullOrEmpty(unifiedDir))
        {
            AppendLog("✗ 请指定统一输出目录\n");
            return;
        }
        if (useUnified && !Directory.Exists(unifiedDir))
        {
            try { Directory.CreateDirectory(unifiedDir); }
            catch (Exception ex)
            {
                AppendLog($"✗ 无法创建输出目录: {ex.Message}\n");
                return;
            }
        }

        ClearLog();
        LogFormat.BeginSession();   // 新会话号：本次转换的所有日志共享
        _isRunning = true;
        ProgressValue = 0;
        ProgressLabelText = "开始…";
        NotifyRunState();

        _cts = new CancellationTokenSource();
        bool cancelled = false;
        var engine = new ConversionEngine(Workspace.Root, Workspace.Output, Workspace.Inbox);
        _engine = engine;

        engine.Log += AppendLog;
        engine.FileStatusChanged += _ => NotifyRunState();
        engine.ReportProgress += (pct, step) =>
        {
            ProgressValue = pct;
            ProgressLabelText = step;
            NotifyRunState();
        };
        engine.Completed += ok =>
        {
            _isRunning = false;
            ProgressLabelText = ok ? "✅ 完成" : "⚠️ 未完成";
            NotifyRunState();
        };

        engine.SetFiles(_files);

        AppendLog($"工作目录: {Workspace.Root}\n");
        AppendLog($"成品输出: {(useUnified ? unifiedDir : "拖入的文件 → 源目录；收件箱的文件 → 成品目录")}\n");
        AppendLog($"转码格式: {(options.AnyConvert ? string.Join(" / ", options.Targets.Select(AudioFormats.Display)) : "不转码（只解密）")}\n");
        AppendLog($"共 {_files.Count} 个文件，开始转换…\n\n");

        try
        {
            await engine.RunAsync(options, _cts.Token);
        }
        catch (Exception ex)
        {
            AppendLog($"✗ 异常: {ex.Message}\n");
        }
        finally
        {
            cancelled = _cts.IsCancellationRequested;
            _isRunning = false;
            NotifyRunState();
        }

        // 用户主动取消时不问：产物可能只做了一半，先别急着动源文件
        if (!cancelled) await PromptDeleteSourcesAsync();
    }

    /// <summary>
    /// 转换完成后询问是否删除源文件。
    ///
    /// 只在真有源文件可删时才弹 —— 全部失败、或源文件本来就不在了，不必打扰。
    /// 勾了"删除源文件"只意味着默认按钮是"删除"，仍然要点一下才算数，不会静默删除。
    /// </summary>
    private async Task PromptDeleteSourcesAsync()
    {
        var candidates = _files
            .Where(f => f.Status == FileStatus.Completed
                        && !string.IsNullOrEmpty(f.SourcePath)
                        && File.Exists(f.SourcePath))
            .ToList();

        if (candidates.Count == 0) return;
        if (Content?.XamlRoot is not { } xamlRoot) return;

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "转换完成",
            Content = new TextBlock
            {
                Text = $"这 {candidates.Count} 个文件的源文件要一起删掉吗？\n\n"
                     + "删除只是放进回收站，随时可以还原；解密/转码出来的成品不受影响。",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = $"删除 {candidates.Count} 个源文件",
            CloseButtonText = "保留源文件",
            DefaultButton = _live.DeleteSourceFile ? ContentDialogButton.Primary : ContentDialogButton.Close,
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            AppendLog("源文件已保留\n");
            return;
        }

        int deleted = _engine?.DeleteSources(candidates) ?? 0;
        AppendLog($"🗑 已删除 {deleted} 个源文件（放入回收站）\n");
    }

    /// <summary>打开格式整理工具：选目录 → 统一转码 / 批量删除。</summary>
    internal async Task ShowFormatToolAsync()
    {
        if (Content?.XamlRoot is null)
        {
            // 窗口还没完成激活时拿不到 XamlRoot，弹不出来 —— 别静默失败，留个记录
            AppendLog("⚠ 窗口尚未就绪，格式整理工具打不开，请稍后再点一次\n");
            return;
        }

        await FormatTool.ShowAsync(this);
    }

    internal void CancelConversion()
    {
        _cts?.Cancel();
        AppendLog("⛔ 正在取消…\n");
    }

    private static T? FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            var child = VisualTreeHelper.GetChild(obj, i);
            if (child is T result) return result;

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null) return descendant;
        }
        return null;
    }

    // ─────────────────────────────────────────────────────── 转换页控件绑定

    internal TextBox? LogBox { get => _logBox; set => _logBox = value; }
    internal ProgressBar? ProgressBar { get => _progressBar; set => _progressBar = value; }
    internal TextBlock? ProgressLabel { get => _progressLabel; set => _progressLabel = value; }
    internal Button? StartButton { get => _startButton; set => _startButton = value; }
    internal Button? CancelButton { get => _cancelButton; set => _cancelButton = value; }
    internal ListView? QueueList { get => _queueList; set => _queueList = value; }
    internal CheckBox? SkipCopyCheck { get => _skipCopyCheck; set => _skipCopyCheck = value; }
    internal CheckBox? ConvertMp3Check { get => _convertMp3Check; set => _convertMp3Check = value; }
    internal CheckBox? ConvertWavCheck { get => _convertWavCheck; set => _convertWavCheck = value; }
    internal CheckBox? ConvertFlacCheck { get => _convertFlacCheck; set => _convertFlacCheck = value; }
    internal CheckBox? DeleteSourceCheck { get => _deleteSourceCheck; set => _deleteSourceCheck = value; }
    internal Button? FormatToolButton { get => _formatToolButton; set => _formatToolButton = value; }
    internal CheckBox? UnifiedOutputCheck { get => _unifiedOutputCheck; set => _unifiedOutputCheck = value; }
    internal TextBox? UnifiedOutputBox { get => _unifiedOutputBox; set => _unifiedOutputBox = value; }
    internal Button? BrowseOutputButton { get => _browseOutputButton; set => _browseOutputButton = value; }
    internal TextBlock? KggWarning { get => _kggWarning; set => _kggWarning = value; }
    internal Button? ClearCompletedButton { get => _clearCompletedButton; set => _clearCompletedButton = value; }

    /// <summary>转换页选项是"即时生效"的运行选项（V2rayN 风格），不属于草稿模型。</summary>
    internal void SaveRunOptions()
    {
        _live.SkipCopy = _skipCopyCheck?.IsChecked ?? false;
        _live.ConvertMp3 = _convertMp3Check?.IsChecked ?? false;
        _live.ConvertWav = _convertWavCheck?.IsChecked ?? false;
        _live.ConvertFlac = _convertFlacCheck?.IsChecked ?? false;
        _live.DeleteSourceFile = _deleteSourceCheck?.IsChecked ?? false;
        _live.UseUnifiedOutput = _unifiedOutputCheck?.IsChecked ?? false;
        _live.UnifiedOutputDir = _unifiedOutputBox?.Text?.Trim() ?? "";

        // 同步草稿与快照，避免下次"取消更改"把这些运行选项一起回滚。
        // 逐个字段抄而不是整体 Clone：设置页的草稿里可能还有未应用的编辑。
        void Sync(Settings s)
        {
            s.SkipCopy = _live.SkipCopy;
            s.ConvertMp3 = _live.ConvertMp3;
            s.ConvertWav = _live.ConvertWav;
            s.ConvertFlac = _live.ConvertFlac;
            s.DeleteSourceFile = _live.DeleteSourceFile;
            s.UseUnifiedOutput = _live.UseUnifiedOutput;
            s.UnifiedOutputDir = _live.UnifiedOutputDir;
        }
        Sync(_draft);
        Sync(_applied);

        _live.Save();
    }
}
