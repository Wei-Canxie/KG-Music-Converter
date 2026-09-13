using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Threading;

namespace KGMusicConverter;

/// <summary>
/// 转换页（V2rayN Vertical 风格）：
/// 左侧操作区 | 可拖动灰色分割线 | 右侧与内容区同高的日志栏。
///
/// 页面只是视图：队列、日志、运行状态都归 <see cref="MainWindow"/> 所有，
/// 所以切页或重建页面都不会丢进度；运行选项（转码格式/删除源文件/统一输出）
/// 是即时生效的运行选项，不属于"草稿 + 应用"的设置模型。
/// </summary>
internal sealed class ConvertControl : ToolPage
{
    private readonly MainWindow _main;

    private ListView? _queueList;
    private TextBox? _logBox;
    private ProgressBar? _progressBar;
    private TextBlock? _progressLabel;
    private Button? _startButton;
    private Button? _cancelButton;
    private Button? _addFilesButton;
    private Button? _clearCompletedButton;
    private CheckBox? _convertMp3Check;
    private CheckBox? _convertWavCheck;
    private CheckBox? _convertFlacCheck;
    private CheckBox? _deleteSourceCheck;
    private CheckBox? _unifiedOutputCheck;
    private TextBox? _unifiedOutputBox;
    private Button? _browseOutputButton;
    private TextBlock? _kggWarning;
    private TextBlock? _fileCountLabel;
    private Button? _formatToolButton;
    private TextBlock? _formatSummary;

    private static readonly SolidColorBrush NeedsManualBrush = new(ColorHelper.FromArgb(255, 0xFF, 0x98, 0x00));

    public ConvertControl(MainWindow main)
    {
        _main = main;

        BuildUI();
        RefreshFromState();

        // 运行状态变化（引擎推进 / 队列变化）→ 页面刷新
        void OnRunStateChanged() => RefreshFromState();
        _main.RunStateChanged += OnRunStateChanged;
        RegisterUnsubscribe(() => _main.RunStateChanged -= OnRunStateChanged);
    }

    private void BuildUI()
    {
        var tm = ThemeManager.Instance;

        // V2rayN Vertical：左操作区 | 可拖动分割线 | 右侧全高日志
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

        // 收件箱（热文件夹）：丢进去就自动入队
        var inboxRow = new Grid { ColumnSpacing = 8 };
        inboxRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inboxRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inboxRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inboxRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var inboxHint = new TextBlock
        {
            Text = "📥 热文件夹：丢进收件箱会自动入队；收件箱来的文件成品放「成品目录」",
            FontSize = 12,
            Foreground = tm.SubText,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(inboxHint, 0);
        inboxRow.Children.Add(inboxHint);

        var inboxButton = new Button
        {
            Content = "打开收件箱",
            FontSize = 12,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6, 12, 6),
        };
        inboxButton.Click += (_, _) => _main.OpenInbox();
        Grid.SetColumn(inboxButton, 1);
        inboxRow.Children.Add(inboxButton);

        var workspaceButton = new Button
        {
            Content = "打开工作区",
            FontSize = 12,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6, 12, 6),
        };
        workspaceButton.Click += (_, _) => _main.OpenWorkspace();
        Grid.SetColumn(workspaceButton, 2);
        inboxRow.Children.Add(workspaceButton);

        var outputButton = new Button
        {
            Content = "打开成品目录",
            FontSize = 12,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6, 12, 6),
        };
        outputButton.Click += (_, _) => _main.OpenOutputFolder();
        Grid.SetColumn(outputButton, 3);
        inboxRow.Children.Add(outputButton);

        contentPanel.Children.Add(inboxRow);

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

        // 队列列表
        var queueScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 200,
        };
        _queueList = new ListView
        {
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = false,
            ItemsSource = _main.Files,
            Background = new SolidColorBrush(Colors.Transparent),
        };
        queueScroll.Content = _queueList;
        contentPanel.Children.Add(queueScroll);

        // 运行选项（即时生效，但记住上次选择）
        var optionsPanel = new StackPanel { Spacing = 10 };

        // 转码是可选动作，收进展开栏：默认收起不占版面，勾了哪些格式在标题里一眼可见
        _formatSummary = new TextBlock
        {
            Text = "输出格式选项",
            TextWrapping = TextWrapping.Wrap,
        };

        var formatExpander = new Expander
        {
            Header = _formatSummary,
            IsExpanded = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };

        var formatPanel = new StackPanel { Spacing = 2 };
        formatPanel.Children.Add(new TextBlock
        {
            Text = "勾选后解密完直接转码；都不勾选 = 只解密，保留原始音频。",
            FontSize = 12,
            Foreground = tm.SubText,
            TextWrapping = TextWrapping.Wrap,
        });
        _convertMp3Check = AddFormatCheck(formatPanel, "转 MP3", _main.Live.ConvertMp3);
        _convertWavCheck = AddFormatCheck(formatPanel, "转 WAV", _main.Live.ConvertWav);
        _convertFlacCheck = AddFormatCheck(formatPanel, "转 FLAC", _main.Live.ConvertFlac);
        formatExpander.Content = formatPanel;

        optionsPanel.Children.Add(formatExpander);
        UpdateFormatHeader();

        // 删除源文件（放在"统一输出"上面）
        _deleteSourceCheck = new CheckBox
        {
            Content = WrapText("删除源文件（转换完成后会再确认一次；删除进回收站）"),
            FontSize = 13,
            IsChecked = _main.Live.DeleteSourceFile,
        };
        _deleteSourceCheck.Checked += (_, _) => _main.SaveRunOptions();
        _deleteSourceCheck.Unchecked += (_, _) => _main.SaveRunOptions();
        optionsPanel.Children.Add(_deleteSourceCheck);

        _unifiedOutputCheck = new CheckBox
        {
            Content = WrapText("统一输出到指定目录："),
            FontSize = 13,
            IsChecked = _main.Live.UseUnifiedOutput,
        };
        _unifiedOutputCheck.Checked += (_, _) => { UpdateUnifiedOutputState(); _main.SaveRunOptions(); };
        _unifiedOutputCheck.Unchecked += (_, _) => { UpdateUnifiedOutputState(); _main.SaveRunOptions(); };
        optionsPanel.Children.Add(_unifiedOutputCheck);

        var outputDirPanel = new Grid { ColumnSpacing = 8, Margin = new Thickness(28, 0, 0, 0) };
        outputDirPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        outputDirPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _unifiedOutputBox = new TextBox
        {
            FontSize = 13,
            CornerRadius = new CornerRadius(8),
            Text = _main.Live.UnifiedOutputDir,
        };
        _unifiedOutputBox.LostFocus += (_, _) => _main.SaveRunOptions();
        Grid.SetColumn(_unifiedOutputBox, 0);
        outputDirPanel.Children.Add(_unifiedOutputBox);
        _browseOutputButton = new Button
        {
            Content = "浏览…",
            FontSize = 13,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 6, 14, 6),
        };
        _browseOutputButton.Click += OnBrowseOutput;
        Grid.SetColumn(_browseOutputButton, 1);
        outputDirPanel.Children.Add(_browseOutputButton);
        optionsPanel.Children.Add(outputDirPanel);

        contentPanel.Children.Add(optionsPanel);

        // 进度
        var progressPanel = new StackPanel { Spacing = 6 };
        _progressLabel = new TextBlock
        {
            Text = "就绪",
            FontSize = 12,
            Foreground = tm.SubText,
            TextWrapping = TextWrapping.Wrap,
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

        // 按钮（Grid 均分，窄列也不溢出）
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

        // 格式整理工具：独立于队列的一次性小工具（选目录 → 统一转码 / 批量删除）
        _formatToolButton = new Button
        {
            Content = "🧰 格式整理工具",
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 10, 8, 10),
        };
        _formatToolButton.Click += (_, _) => _ = _main.ShowFormatToolAsync();
        contentPanel.Children.Add(_formatToolButton);

        leftScroll.Content = contentPanel;
        Grid.SetColumn(leftScroll, 0);
        root.Children.Add(leftScroll);

        // ── 中列：可拖动分割线（手写 Pointer 拖动，调整左右列占比） ──
        var splitter = new SplitterGrid
        {
            Width = 8,
            Background = new SolidColorBrush(ColorHelper.FromArgb(24, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
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

        var col0 = root.ColumnDefinitions[0];
        bool dragging = false;
        double dragStartX = 0, dragStartWidth = 0;

        splitter.PointerPressed += (_, e) =>
        {
            dragging = true;
            dragStartX = e.GetCurrentPoint(root).Position.X;
            dragStartWidth = col0.ActualWidth;
            e.Handled = true;
        };
        root.PointerMoved += (_, e) =>
        {
            if (!dragging) return;
            var delta = e.GetCurrentPoint(root).Position.X - dragStartX;
            col0.Width = new GridLength(
                Math.Clamp(dragStartWidth + delta, 360, Math.Max(360, root.ActualWidth - 300 - 8)),
                GridUnitType.Pixel);
            e.Handled = true;
        };
        root.PointerReleased += (_, e) => { dragging = false; e.Handled = true; };
        root.PointerCaptureLost += (_, _) => dragging = false;

        Grid.SetColumn(splitter, 1);
        root.Children.Add(splitter);

        // ── 右列：日志栏（与内容区同高，自动换行） ──
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
            Text = _main.LogText,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(_logBox, ScrollBarVisibility.Auto);
        logHost.Children.Add(_logBox);
        Grid.SetColumn(logHost, 2);
        root.Children.Add(logHost);

        Content = root;

        // 交给窗口，之后引擎输出的日志直接落到这里
        _main.LogBox = _logBox;
        _main.ProgressBar = _progressBar;
        _main.ProgressLabel = _progressLabel;
        _main.StartButton = _startButton;
        _main.CancelButton = _cancelButton;
        _main.QueueList = _queueList;
        _main.ConvertMp3Check = _convertMp3Check;
        _main.ConvertWavCheck = _convertWavCheck;
        _main.ConvertFlacCheck = _convertFlacCheck;
        _main.DeleteSourceCheck = _deleteSourceCheck;
        _main.FormatToolButton = _formatToolButton;
        _main.UnifiedOutputCheck = _unifiedOutputCheck;
        _main.UnifiedOutputBox = _unifiedOutputBox;
        _main.BrowseOutputButton = _browseOutputButton;
        _main.KggWarning = _kggWarning;
        _main.ClearCompletedButton = _clearCompletedButton;

        // 左列滚动位置（切页回来能回到原处）
        leftScroll.ViewChanged += (_, _) => _main.SetScrollOffset("convert", leftScroll.VerticalOffset);

        UpdateUnifiedOutputState();
    }

    /// <summary>把应用状态投影到页面控件上。</summary>
    private void RefreshFromState()
    {
        if (_queueList is null) return;

        var total = _main.Files.Count;
        var pending = _main.Files.Count(f => f.Status == FileStatus.Pending);
        var done = _main.Files.Count(f => f.Status == FileStatus.Completed);
        var failed = _main.Files.Count(f => f.Status is FileStatus.Failed or FileStatus.NeedsManualKGG);

        if (_fileCountLabel is not null)
        {
            _fileCountLabel.Text = total == 0
                ? "共 0 个文件"
                : $"共 {total} 个文件 · 待处理 {pending} · 完成 {done}" + (failed > 0 ? $" · 失败 {failed}" : "");
        }

        if (_progressBar is not null) _progressBar.Value = _main.ProgressValue;
        if (_progressLabel is not null) _progressLabel.Text = _main.ProgressLabelText ?? "就绪";

        if (_startButton is not null) _startButton.IsEnabled = !_main.IsRunning && total > 0;
        if (_cancelButton is not null) _cancelButton.IsEnabled = _main.IsRunning;
        if (_addFilesButton is not null) _addFilesButton.IsEnabled = !_main.IsRunning;
        if (_clearCompletedButton is not null) _clearCompletedButton.IsEnabled = !_main.IsRunning;
        // 整理工具也吃 ffmpeg，转换跑着的时候先别开，免得抢 CPU
        if (_formatToolButton is not null) _formatToolButton.IsEnabled = !_main.IsRunning;

        UpdateKggWarning();
    }

    private void UpdateKggWarning()
    {
        if (_kggWarning is null) return;

        var kggCount = _main.Files.Count(f => f.IsKgg);
        if (kggCount > 0)
        {
            _kggWarning.Visibility = Visibility.Visible;
            _kggWarning.Text = $"⚠ 检测到 {kggCount} 个 KGG 文件。KGG 解密需要酷狗客户端的密钥缓存。若解密失败，请先用酷狗音乐客户端播放一次该文件后再试。";
        }
        else
        {
            _kggWarning.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateUnifiedOutputState()
    {
        bool enabled = _unifiedOutputCheck?.IsChecked ?? false;
        if (_unifiedOutputBox is not null) _unifiedOutputBox.IsEnabled = enabled;
        if (_browseOutputButton is not null) _browseOutputButton.IsEnabled = enabled;
    }

    // ── 交互 ──

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        _main.AddFiles(items.Select(i => i.Path));
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

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_main);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var results = await picker.PickMultipleFilesAsync().AsTask();
        if (results is { Count: > 0 })
        {
            _main.AddFiles(results.Select(r => r.Path));
        }
    }

    private async void OnBrowseOutput(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder,
        };
        picker.FileTypeFilter.Add("*");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_main);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync().AsTask();
        if (folder is not null && _unifiedOutputBox is not null)
        {
            _unifiedOutputBox.Text = folder.Path;
            _main.SaveRunOptions();
        }
    }

    private void OnStart(object sender, RoutedEventArgs e)
    {
        if (_main.Files.Count == 0)
        {
            _main.AppendLog("✗ 请先添加文件\n");
            return;
        }
        _main.StartConversion();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => _main.CancelConversion();

    private void OnClearCompleted(object sender, RoutedEventArgs e) => _main.RemoveCompletedFiles();

    /// <summary>加一个转码格式勾选框并挂上保存回调（三个格式共用同一套行为）。</summary>
    private CheckBox AddFormatCheck(Panel parent, string label, bool isChecked)
    {
        var check = new CheckBox
        {
            Content = WrapText(label),
            FontSize = 13,
            IsChecked = isChecked,
        };
        check.Checked += (_, _) => { _main.SaveRunOptions(); UpdateFormatHeader(); };
        check.Unchecked += (_, _) => { _main.SaveRunOptions(); UpdateFormatHeader(); };
        parent.Children.Add(check);
        return check;
    }

    /// <summary>展开栏标题带上当前选择 —— 收起时也能一眼看出这次会不会转码。</summary>
    private void UpdateFormatHeader()
    {
        if (_formatSummary is null) return;

        var picked = new List<string>();
        if (_convertMp3Check?.IsChecked == true) picked.Add("MP3");
        if (_convertWavCheck?.IsChecked == true) picked.Add("WAV");
        if (_convertFlacCheck?.IsChecked == true) picked.Add("FLAC");

        _formatSummary.Text = picked.Count == 0
            ? "输出格式选项（只解密）"
            : $"输出格式选项（{string.Join(" / ", picked)}）";
    }

    /// <summary>包装可自动换行的文本（CheckBox 的 Content 为 string 时窄列会截断）。</summary>
    private static TextBlock WrapText(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
    };
}

/// <summary>
/// 可设置拖动光标的 Grid（ProtectedCursor 是 protected，需子类暴露）。
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
