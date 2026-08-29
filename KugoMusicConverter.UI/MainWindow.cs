using System;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Threading;
using System.IO;

namespace KugoMusicConverter;

/// <summary>
/// WinUI3 主窗口 — 圆角控件 + 主题色 #ff66aa + 实时日志 + 进度条。
/// 布局: 顶部标题 / 中部输入选项 / 底部日志区 + 进度条。
/// </summary>
internal sealed class MainWindow : Window
{
    private ConversionEngine? _engine;
    private CancellationTokenSource? _cts;

    private TextBox? _logBox;
    private ProgressBar? _progressBar;
    private TextBlock? _progressLabel;
    private Button? _startButton;
    private Button? _cancelButton;
    private TextBlock? _statusText;
    private CheckBox? _skipCopyCheck;
    private CheckBox? _skipConvertCheck;
    private TextBox? _projectDirBox;

    // 主题色
    private static readonly Windows.UI.Color ThemeColor = ColorHelper.FromArgb(255, 0xFF, 0x66, 0xAB);
    private static readonly SolidColorBrush ThemeBrush = new(ThemeColor);
    private static readonly SolidColorBrush ThemeLightBrush = new(ColorHelper.FromArgb(255, 0xFF, 0x85, 0xBC));
    private static readonly SolidColorBrush ThemeDarkBrush = new(ColorHelper.FromArgb(255, 0xCC, 0x33, 0x77));
    private static readonly SolidColorBrush BackgroundBrush = new(ColorHelper.FromArgb(255, 0x1E, 0x1E, 0x2A));
    private static readonly SolidColorBrush SurfaceBrush = new(ColorHelper.FromArgb(255, 0x2A, 0x2A, 0x3A));
    private static readonly SolidColorBrush TextBrush = new(ColorHelper.FromArgb(255, 0xE0, 0xE0, 0xE8));
    private static readonly SolidColorBrush SubTextBrush = new(ColorHelper.FromArgb(255, 0x90, 0x90, 0xA0));

    public MainWindow()
    {
        Title = "Kugo Music Converter — 酷狗加密音频解密工具箱";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(900, 680));

        // 半透明背景材质
        SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };

        BuildUI();
    }

    private void BuildUI()
    {
        // 根容器
        var root = new Grid
        {
            Background = BackgroundBrush,
            Padding = new Thickness(28, 24, 28, 24),
            RowSpacing = 16,
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 标题
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 项目目录
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 选项
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 日志
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 进度
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 按钮

        // ── 标题 ──
        var titlePanel = new StackPanel { Spacing = 4 };
        var title = new TextBlock
        {
            Text = "Kugo Music Converter",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = ThemeBrush,
        };
        var subtitle = new TextBlock
        {
            Text = "酷狗音乐加密音频解密 / 转换工具箱",
            FontSize = 13,
            Foreground = SubTextBrush,
        };
        titlePanel.Children.Add(title);
        titlePanel.Children.Add(subtitle);
        Grid.SetRow(titlePanel, 0);
        root.Children.Add(titlePanel);

        // ── 项目目录 ──
        var dirPanel = new Grid { ColumnSpacing = 8 };
        dirPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        dirPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dirPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dirLabel = new TextBlock
        {
            Text = "项目目录:",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = TextBrush,
            FontSize = 13,
        };
        Grid.SetColumn(dirLabel, 0);
        dirPanel.Children.Add(dirLabel);

        _projectDirBox = new TextBox
        {
            Text = GetDefaultProjectDir(),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Background = SurfaceBrush,
            Foreground = TextBrush,
            BorderBrush = ThemeDarkBrush,
            CornerRadius = new CornerRadius(8),
        };
        Grid.SetColumn(_projectDirBox, 1);
        dirPanel.Children.Add(_projectDirBox);

        var browseBtn = new Button
        {
            Content = "浏览…",
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Background = SurfaceBrush,
            Foreground = TextBrush,
            BorderBrush = ThemeDarkBrush,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 6, 14, 6),
        };
        browseBtn.Click += OnBrowse;
        Grid.SetColumn(browseBtn, 2);
        dirPanel.Children.Add(browseBtn);

        Grid.SetRow(dirPanel, 1);
        root.Children.Add(dirPanel);

        // ── 选项 ──
        var optionsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };

        _skipCopyCheck = new CheckBox
        {
            Content = "跳过复制（文件已在项目目录）",
            FontSize = 13,
            Foreground = TextBrush,
            CornerRadius = new CornerRadius(4),
        };
        optionsPanel.Children.Add(_skipCopyCheck);

        _skipConvertCheck = new CheckBox
        {
            Content = "跳过转 MP3（仅解密）",
            FontSize = 13,
            Foreground = TextBrush,
            CornerRadius = new CornerRadius(4),
        };
        optionsPanel.Children.Add(_skipConvertCheck);

        Grid.SetRow(optionsPanel, 2);
        root.Children.Add(optionsPanel);

        // ── 日志区 ──
        var logBorder = new Border
        {
            Background = SurfaceBrush,
            BorderBrush = ThemeDarkBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(4),
        };

        _logBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            FontSize = 12,
            Foreground = TextBrush,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Text = "",
        };
        ScrollViewer.SetVerticalScrollBarVisibility(_logBox, ScrollBarVisibility.Auto);
        logBorder.Child = _logBox;
        Grid.SetRow(logBorder, 3);
        root.Children.Add(logBorder);

        // ── 进度条 ──
        var progressPanel = new StackPanel { Spacing = 6 };
        _progressLabel = new TextBlock
        {
            Text = "就绪",
            FontSize = 12,
            Foreground = SubTextBrush,
        };
        progressPanel.Children.Add(_progressLabel);

        _progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Height = 6,
            Foreground = ThemeBrush,
            Background = SurfaceBrush,
            CornerRadius = new CornerRadius(3),
        };
        progressPanel.Children.Add(_progressBar);
        Grid.SetRow(progressPanel, 4);
        root.Children.Add(progressPanel);

        // ── 按钮 ──
        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right };

        _cancelButton = new Button
        {
            Content = "取消",
            FontSize = 14,
            IsEnabled = false,
            Background = SurfaceBrush,
            Foreground = TextBrush,
            BorderBrush = ThemeDarkBrush,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(28, 10, 28, 10),
        };
        _cancelButton.Click += OnCancel;
        buttonPanel.Children.Add(_cancelButton);

        _startButton = new Button
        {
            Content = "开始转换",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Background = ThemeBrush,
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(32, 10, 32, 10),
        };
        _startButton.Click += OnStart;
        buttonPanel.Children.Add(_startButton);

        Grid.SetRow(buttonPanel, 5);
        root.Children.Add(buttonPanel);

        Content = root;
    }

    private string GetDefaultProjectDir()
    {
        // 默认指向项目根目录（exe 所在目录）
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (!string.IsNullOrEmpty(exeDir)) return exeDir;
        return AppContext.BaseDirectory;
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        // 用简单的输入对话框替代 WinUI3 文件夹选择器（避免额外依赖）
        var dialog = new ContentDialog
        {
            Title = "选择项目目录",
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "请输入包含 input/ 和 kgg-dec.exe 的项目根目录路径：", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,12) },
                    new TextBox { Text = _projectDirBox?.Text ?? "", FontSize = 13 },
                }
            },
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            XamlRoot = Content.XamlRoot,
        };
        // 简化：直接让用户在文本框里编辑
        _projectDirBox?.Focus(FocusState.Programmatic);
    }

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        var dir = _projectDirBox?.Text.Trim();
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            AppendLog("✗ 项目目录不存在，请检查路径");
            return;
        }

        _startButton!.IsEnabled = false;
        _cancelButton!.IsEnabled = true;
        _logBox!.Text = "";
        _progressBar!.Value = 0;

        _cts = new CancellationTokenSource();
        _engine = new ConversionEngine(dir);
        _engine.Log += AppendLog;
        _engine.ReportProgress += (pct, step) =>
        {
            _progressBar!.Value = pct;
            _progressLabel!.Text = step;
        };
        _engine.Completed += (ok) =>
        {
            _startButton!.IsEnabled = true;
            _cancelButton!.IsEnabled = false;
            _progressLabel!.Text = ok ? "✅ 完成" : "⚠️ 未完成";
        };

        AppendLog($"项目目录: {dir}");
        AppendLog("开始转换…\n");

        try
        {
            await _engine.RunAsync(
                skipCopy: _skipCopyCheck?.IsChecked ?? false,
                skipConvert: _skipConvertCheck?.IsChecked ?? false,
                _cts.Token);
        }
        catch (Exception ex)
        {
            AppendLog($"✗ 异常: {ex.Message}");
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        AppendLog("⛔ 正在取消…");
    }

    private void AppendLog(string message)
    {
        if (_logBox == null) return;
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var line = $"[{timestamp}] {message}\n";

        // 在 UI 线程追加
        _logBox.Text += line;
        // 自动滚动到底部
        var sv = FindVisualChild<ScrollViewer>(_logBox);
        sv?.ChangeView(null, sv.ScrollableHeight, null);
    }

    private static T? FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            var child = VisualTreeHelper.GetChild(obj, i);
            if (child is T result) return result;
            var descendant = FindVisualChild<T>(child);
            if (descendant != null) return descendant;
        }
        return null;
    }
}