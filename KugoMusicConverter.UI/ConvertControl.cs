using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Threading;

namespace KugoMusicConverter;

internal sealed class ConvertControl : UserControl
{
    private MainWindow? _main;
    private ListView? _queueList;
    private TextBox? _logBox;
    private ProgressBar? _progressBar;
    private TextBlock? _progressLabel;
    private Button? _startButton;
    private Button? _cancelButton;
    private Button? _addFilesButton;
    private Button? _clearCompletedButton;
    private CheckBox? _skipCopyCheck;
    private CheckBox? _skipConvertCheck;
    private CheckBox? _unifiedOutputCheck;
    private TextBox? _unifiedOutputBox;
    private Button? _browseOutputButton;
    private TextBlock? _kggWarning;
    private TextBlock? _fileCountLabel;

    private static readonly SolidColorBrush NeedsManualBrush = new(ColorHelper.FromArgb(255, 0xFF, 0x98, 0x00));

    public ConvertControl(MainWindow main)
    {
        _main = main;
        BuildUI();

        _main.QueueList = _queueList;
        _main.LogBox = _logBox;
        _main.ProgressBar = _progressBar;
        _main.ProgressLabel = _progressLabel;
        _main.StartButton = _startButton;
        _main.CancelButton = _cancelButton;
        _main.SkipCopyCheck = _skipCopyCheck;
        _main.SkipConvertCheck = _skipConvertCheck;
        _main.UnifiedOutputCheck = _unifiedOutputCheck;
        _main.UnifiedOutputBox = _unifiedOutputBox;
        _main.BrowseOutputButton = _browseOutputButton;
        _main.KggWarning = _kggWarning;
        _main.ClearCompletedButton = _clearCompletedButton;

        _main.Files.CollectionChanged += (_, _) => UpdateFileCount();
    }

    private void BuildUI()
    {
        var tm = ThemeManager.Instance;

        // V2rayN Vertical 风格：左操作区 | 可拖动分割线 | 右侧全高日志
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(480, GridUnitType.Pixel), MinWidth = 360 });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });

        // ── 左列：操作区 ──
        var leftScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var contentPanel = new StackPanel
        {
            Spacing = 16,
            Padding = new Thickness(24, 16, 24, 16),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        // 标题
        var titlePanel = new StackPanel { Spacing = 4 };
        titlePanel.Children.Add(new TextBlock
        {
            Text = "转换队列",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = "拖入或添加文件，点击开始转换",
            FontSize = 13,
            Foreground = tm.SubText,
            TextWrapping = TextWrapping.Wrap,
        });
        contentPanel.Children.Add(titlePanel);

        // 文件计数
        _fileCountLabel = new TextBlock
        {
            Text = "共 0 个文件",
            FontSize = 12,
            Foreground = tm.SubText,
            TextWrapping = TextWrapping.Wrap,
        };
        contentPanel.Children.Add(_fileCountLabel);

        // 拖放区
        var dropZone = new Border
        {
            Background = new SolidColorBrush(ColorHelper.FromArgb(64, 255, 255, 255)),
            BorderBrush = tm.Accent,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(24),
            AllowDrop = true,
            Height = 72,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        dropZone.DragOver += DropZone_DragOver;
        dropZone.Drop += DropZone_Drop;
        dropZone.Child = new TextBlock
        {
            Text = "📁 拖入文件到此处，或点击下方按钮添加",
            FontSize = 14,
            Foreground = tm.SubText,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        contentPanel.Children.Add(dropZone);

        // KGG 警告
        _kggWarning = new TextBlock
        {
            Text = "",
            FontSize = 13,
            Foreground = NeedsManualBrush,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(4, 0, 4, 0),
        };
        contentPanel.Children.Add(_kggWarning);

        // 队列列表（轻量卡片）
        var queueScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 200,
        };
        _queueList = new ListView
        {
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = false,
            ItemsSource = _main?.Files,
            Background = new SolidColorBrush(Colors.Transparent),
        };
        queueScroll.Content = _queueList;
        contentPanel.Children.Add(queueScroll);

        // 选项
        var optionsPanel = new StackPanel { Spacing = 10 };

        _skipCopyCheck = new CheckBox
        {
            Content = WrapText("跳过复制（文件已在工作目录）"),
            FontSize = 13,
        };
        optionsPanel.Children.Add(_skipCopyCheck);

        _skipConvertCheck = new CheckBox
        {
            Content = WrapText("跳过转 MP3（仅解密）"),
            FontSize = 13,
        };
        optionsPanel.Children.Add(_skipConvertCheck);

        _unifiedOutputCheck = new CheckBox
        {
            Content = WrapText("统一输出到指定目录："),
            FontSize = 13,
        };
        _unifiedOutputCheck.Checked += (_, _) => UpdateUnifiedOutputState();
        _unifiedOutputCheck.Unchecked += (_, _) => UpdateUnifiedOutputState();
        optionsPanel.Children.Add(_unifiedOutputCheck);

        var outputDirPanel = new Grid { ColumnSpacing = 8, Margin = new Thickness(28, 0, 0, 0) };
        outputDirPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        outputDirPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _unifiedOutputBox = new TextBox
        {
            IsEnabled = false,
            FontSize = 13,
            CornerRadius = new CornerRadius(8),
        };
        Grid.SetColumn(_unifiedOutputBox, 0);
        outputDirPanel.Children.Add(_unifiedOutputBox);
        _browseOutputButton = new Button
        {
            Content = "浏览…",
            IsEnabled = false,
            FontSize = 13,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 6, 14, 6),
        };
        _browseOutputButton.Click += OnBrowseOutput;
        Grid.SetColumn(_browseOutputButton, 1);
        outputDirPanel.Children.Add(_browseOutputButton);
        optionsPanel.Children.Add(outputDirPanel);

        contentPanel.Children.Add(optionsPanel);

        // 进度条
        var progressPanel = new StackPanel { Spacing = 6 };
        _progressLabel = new TextBlock
        {
            Text = "就绪",
            FontSize = 12,
            Foreground = tm.SubText,
        };
        progressPanel.Children.Add(_progressLabel);
        _progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Height = 6,
            Foreground = tm.Accent,
            CornerRadius = new CornerRadius(3),
        };
        progressPanel.Children.Add(_progressBar);
        contentPanel.Children.Add(progressPanel);

        // 按钮（左对齐，Grid 均分保证窄列时也能放下）
        var buttonPanel = new Grid { ColumnSpacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
        for (int i = 0; i < 4; i++)
            buttonPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _addFilesButton = new Button
        {
            Content = "添加文件…",
            FontSize = 14,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 10, 8, 10),
        };
        _addFilesButton.Click += OnAddFiles;
        Grid.SetColumn(_addFilesButton, 0);
        buttonPanel.Children.Add(_addFilesButton);

        _clearCompletedButton = new Button
        {
            Content = "清除已完成",
            FontSize = 14,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 10, 8, 10),
        };
        _clearCompletedButton.Click += OnClearCompleted;
        Grid.SetColumn(_clearCompletedButton, 1);
        buttonPanel.Children.Add(_clearCompletedButton);

        _cancelButton = new Button
        {
            Content = "取消",
            FontSize = 14,
            IsEnabled = false,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 10, 8, 10),
        };
        _cancelButton.Click += OnCancel;
        Grid.SetColumn(_cancelButton, 2);
        buttonPanel.Children.Add(_cancelButton);

        _startButton = new Button
        {
            Content = "开始转换",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Background = tm.Accent,
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 10, 8, 10),
        };
        _startButton.Click += OnStart;
        Grid.SetColumn(_startButton, 3);
        buttonPanel.Children.Add(_startButton);

        contentPanel.Children.Add(buttonPanel);

        leftScroll.Content = contentPanel;
        Grid.SetColumn(leftScroll, 0);
        root.Children.Add(leftScroll);

        // ── 中列：可拖动分割线（手写 Pointer 拖动，调整左右列占比） ──
        // 用可见的半透明深色条：既是视觉分割线又是命中区（Grid 默认 Background=null 不命中）
        var splitter = new SplitterGrid
        {
            Width = 8,
            Background = new SolidColorBrush(ColorHelper.FromArgb(24, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        // 视觉细灰线（居中，不拦截命中）
        var splitterLine = new Border
        {
            Width = 1,
            Background = tm.Border,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 12, 0, 12),
            IsHitTestVisible = false,
        };
        splitter.Children.Add(splitterLine);
        // 拖动逻辑（不依赖 CapturePointer：移动/释放监听挂在 root 上更稳）
        var col0 = root.ColumnDefinitions[0];
        bool dragging = false;
        double dragStartX = 0, dragStartWidth = 0;

        splitter.PointerPressed += (s, e) =>
        {
            dragging = true;
            dragStartX = e.GetCurrentPoint(root).Position.X;
            dragStartWidth = col0.ActualWidth;
            e.Handled = true;
        };
        root.PointerMoved += (s, e) =>
        {
            if (!dragging) return;
            var delta = e.GetCurrentPoint(root).Position.X - dragStartX;
            var newWidth = Math.Clamp(dragStartWidth + delta, 360, root.ActualWidth - 300 - 6);
            col0.Width = new GridLength(newWidth, GridUnitType.Pixel);
            e.Handled = true;
        };
        root.PointerReleased += (s, e) => { dragging = false; e.Handled = true; };
        root.PointerCaptureLost += (s, e) => dragging = false;
        splitter.PointerCaptureLost += (s, e) => dragging = false;

        Grid.SetColumn(splitter, 1);
        root.Children.Add(splitter);

        // ── 右列：日志输出栏（与内容区同高，撑满，自动换行） ──
        var logHost = new Grid();
        _logBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            FontSize = 11,
            Background = new SolidColorBrush(ColorHelper.FromArgb(32, 255, 255, 255)),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            Text = "",
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(_logBox, ScrollBarVisibility.Auto);
        logHost.Children.Add(_logBox);
        Grid.SetColumn(logHost, 2);
        root.Children.Add(logHost);

        Content = root;
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        AddFilesFromPaths(items.Select(i => i.Path).ToArray());
    }

    private async void OnAddFiles(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker
        {
            ViewMode = Windows.Storage.Pickers.PickerViewMode.List,
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder,
        };
        picker.FileTypeFilter.Add(".kgg");
        picker.FileTypeFilter.Add(".kgm");
        picker.FileTypeFilter.Add(".kgma");
        picker.FileTypeFilter.Add(".vpr");
        picker.FileTypeFilter.Add(".flac");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_main!);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var results = await picker.PickMultipleFilesAsync().AsTask();
        if (results != null && results.Count > 0)
        {
            AddFilesFromPaths(results.Select(r => r.Path).ToArray());
        }
    }

    private void AddFilesFromPaths(string[] paths)
    {
        if (_main == null) return;
        var supported = new HashSet<string> { ".kgg", ".kgm", ".kgma", ".vpr", ".flac" };
        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            var ext = Path.GetExtension(path).ToLower();
            if (!supported.Contains(ext)) continue;
            if (_main.Files.Any(f => f.SourcePath.Equals(path, StringComparison.OrdinalIgnoreCase))) continue;

            var entry = new FileEntry
            {
                SourcePath = path,
                FileName = Path.GetFileName(path),
                SourceDirectory = Path.GetDirectoryName(path)!,
                BaseName = Path.GetFileNameWithoutExtension(path),
                Extension = ext,
                Status = FileStatus.Pending,
            };
            _main.Files.Add(entry);
        }
        UpdateKggWarning();
        UpdateFileCount();
    }

    private void UpdateKggWarning()
    {
        if (_main == null) return;
        var kggCount = _main.Files.Count(f => f.IsKgg);
        if (kggCount > 0)
        {
            _kggWarning!.Visibility = Visibility.Visible;
            _kggWarning.Text = $"⚠ 检测到 {kggCount} 个 KGG 文件。KGG 解密需要酷狗客户端的密钥缓存。若解密失败，请先用酷狗音乐客户端播放一次该文件后再试。";
        }
        else
        {
            _kggWarning!.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateFileCount()
    {
        if (_main == null) return;
        var total = _main.Files.Count;
        var pending = _main.Files.Count(f => f.Status == FileStatus.Pending);
        var completed = _main.Files.Count(f => f.Status == FileStatus.Completed);
        var failed = _main.Files.Count(f => f.Status == FileStatus.Failed);
        var needsManual = _main.Files.Count(f => f.Status == FileStatus.NeedsManualKGG);
        _fileCountLabel!.Text = $"共 {total} 个文件 | 待处理 {pending} | 完成 {completed} | 失败 {failed} | 需手动 {needsManual}";
    }

    private void OnClearCompleted(object sender, RoutedEventArgs e)
    {
        if (_main == null) return;
        var toRemove = _main.Files.Where(f => f.Status == FileStatus.Completed || f.Status == FileStatus.Failed).ToList();
        foreach (var f in toRemove) _main.Files.Remove(f);
        UpdateFileCount();
    }

    private void UpdateUnifiedOutputState()
    {
        var enabled = _unifiedOutputCheck!.IsChecked ?? false;
        _unifiedOutputBox!.IsEnabled = enabled;
        _browseOutputButton!.IsEnabled = enabled;
    }

    private async void OnBrowseOutput(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder,
        };
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_main!);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var result = await picker.PickSingleFolderAsync().AsTask();
        if (result != null)
        {
            _unifiedOutputBox!.Text = result.Path;
        }
    }

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        if (_main == null || _main.Files.Count == 0)
        {
            AppendLog("✗ 请先添加文件");
            return;
        }

        var workDir = _main.Files[0].SourceDirectory;

        _startButton!.IsEnabled = false;
        _cancelButton!.IsEnabled = true;
        _logBox!.Text = "";
        _progressBar!.Value = 0;

        var cts = new CancellationTokenSource();
        var engine = new ConversionEngine(workDir);
        _main.SetEngine(engine, cts);

        engine.Log += AppendLog;
        engine.FileStatusChanged += OnFileStatusChanged;
        engine.ReportProgress += (pct, step) =>
        {
            _progressBar!.Value = pct;
            _progressLabel!.Text = step;
        };
        engine.Completed += (ok) =>
        {
            _startButton!.IsEnabled = true;
            _cancelButton!.IsEnabled = false;
            _progressLabel!.Text = ok ? "✅ 完成" : "⚠️ 未完成";
            UpdateFileCount();
        };

        engine.SetFiles(_main.Files);

        AppendLog($"工作目录: {workDir}");
        AppendLog($"共 {_main.Files.Count} 个文件，开始转换…\n");

        try
        {
            bool useUnified = _unifiedOutputCheck!.IsChecked ?? false;
            string unifiedDir = _unifiedOutputBox!.Text?.Trim() ?? "";
            if (useUnified && string.IsNullOrEmpty(unifiedDir))
            {
                AppendLog("✗ 请指定统一输出目录");
                _startButton!.IsEnabled = true;
                return;
            }
            if (useUnified && !Directory.Exists(unifiedDir))
            {
                try { Directory.CreateDirectory(unifiedDir); }
                catch (Exception ex)
                {
                    AppendLog($"✗ 无法创建输出目录: {ex.Message}");
                    _startButton!.IsEnabled = true;
                    return;
                }
            }

            await engine.RunAsync(
                skipConvert: _skipConvertCheck?.IsChecked ?? false,
                useUnifiedOutput: useUnified,
                unifiedOutputDir: unifiedDir,
                cts.Token);
        }
        catch (Exception ex)
        {
            AppendLog($"✗ 异常: {ex.Message}");
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        _main?.Cts?.Cancel();
        AppendLog("⛔ 正在取消…");
    }

    private void OnFileStatusChanged(FileEntry f)
    {
        UpdateFileCount();
    }

    private void AppendLog(string message)
    {
        if (_logBox == null) return;
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var line = $"[{timestamp}] {message}\n";
        _logBox.Text += line;
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

    /// <summary>
    /// 包装可自动换行的文本（用于 CheckBox 等 Content 为 string 时窄列截断问题）
    /// </summary>
    private static TextBlock WrapText(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
    };
}

/// <summary>
/// 可设置拖动光标的 Grid（ProtectedCursor 是 protected，需子类暴露）
/// </summary>
internal sealed class SplitterGrid : Grid
{
    public SplitterGrid()
    {
        ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(
            Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast);
    }
}

internal class FileStatusToBrushConverter : Microsoft.UI.Xaml.Data.IValueConverter
{
    private static readonly SolidColorBrush PendingBrush = new(ColorHelper.FromArgb(255, 0x60, 0x60, 0x70));
    private static readonly SolidColorBrush CompletedBrush = new(ColorHelper.FromArgb(255, 0x4C, 0xAF, 0x50));
    private static readonly SolidColorBrush FailedBrush = new(ColorHelper.FromArgb(255, 0xF4, 0x43, 0x36));
    private static readonly SolidColorBrush NeedsManualBrush = new(ColorHelper.FromArgb(255, 0xFF, 0x98, 0x00));
    private static readonly SolidColorBrush ProcessingBrush = new(ColorHelper.FromArgb(255, 0x21, 0x96, 0xF3));

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is FileStatus status)
        {
            return status switch
            {
                FileStatus.Pending => PendingBrush,
                FileStatus.Processing => ProcessingBrush,
                FileStatus.Completed => CompletedBrush,
                FileStatus.Failed => FailedBrush,
                FileStatus.NeedsManualKGG => NeedsManualBrush,
                _ => PendingBrush,
            };
        }
        return PendingBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
