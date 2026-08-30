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

        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        // 模板风格：扁平 StackPanel + Spacing + Padding，背景透明（透出窗口背景图/模糊）
        var contentPanel = new StackPanel
        {
            Spacing = 16,
            Padding = new Thickness(24, 16, 24, 16),
            MaxWidth = 900,
        };

        // 标题
        var titlePanel = new StackPanel { Spacing = 4 };
        titlePanel.Children.Add(new TextBlock
        {
            Text = "转换队列",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = "拖入或添加文件，点击开始转换",
            FontSize = 13,
            Foreground = tm.SubText,
        });
        contentPanel.Children.Add(titlePanel);

        // 文件计数
        _fileCountLabel = new TextBlock
        {
            Text = "共 0 个文件",
            FontSize = 12,
            Foreground = tm.SubText,
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
        };
        dropZone.DragOver += DropZone_DragOver;
        dropZone.Drop += DropZone_Drop;
        dropZone.Child = new TextBlock
        {
            Text = "📁 拖入文件到此处，或点击下方按钮添加",
            FontSize = 14,
            Foreground = tm.SubText,
            HorizontalAlignment = HorizontalAlignment.Center,
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
            Content = "跳过复制（文件已在工作目录）",
            FontSize = 13,
        };
        optionsPanel.Children.Add(_skipCopyCheck);

        _skipConvertCheck = new CheckBox
        {
            Content = "跳过转 MP3（仅解密）",
            FontSize = 13,
        };
        optionsPanel.Children.Add(_skipConvertCheck);

        _unifiedOutputCheck = new CheckBox
        {
            Content = "统一输出到指定目录：",
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

        // 日志区（轻量卡片）
        _logBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            FontSize = 11,
            Background = new SolidColorBrush(ColorHelper.FromArgb(64, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            BorderBrush = tm.Border,
            CornerRadius = new CornerRadius(8),
            Text = "",
            MinHeight = 100,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(_logBox, ScrollBarVisibility.Auto);
        contentPanel.Children.Add(_logBox);

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

        // 按钮
        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right };

        _addFilesButton = new Button
        {
            Content = "添加文件…",
            FontSize = 14,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20, 10, 20, 10),
        };
        _addFilesButton.Click += OnAddFiles;
        buttonPanel.Children.Add(_addFilesButton);

        _clearCompletedButton = new Button
        {
            Content = "清除已完成",
            FontSize = 14,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20, 10, 20, 10),
        };
        _clearCompletedButton.Click += OnClearCompleted;
        buttonPanel.Children.Add(_clearCompletedButton);

        _cancelButton = new Button
        {
            Content = "取消",
            FontSize = 14,
            IsEnabled = false,
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
            Background = tm.Accent,
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(32, 10, 32, 10),
        };
        _startButton.Click += OnStart;
        buttonPanel.Children.Add(_startButton);

        contentPanel.Children.Add(buttonPanel);

        scroll.Content = contentPanel;
        Content = scroll;
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
