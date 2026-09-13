using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace KGMusicConverter;

/// <summary>
/// 格式整理工具：选一个目录，把里面的通用音频（flac / mp3 / ogg …）统一转成
/// WAV / MP3 / FLAC，或者批量删除加密文件 / 音频文件。
///
/// 刻意做成独立的一次性工具，<b>不碰转换队列</b> —— 整理的是用户自己的音乐目录，
/// 和"解密酷狗加密文件"是两件事，共用同一套 ffmpeg 命令（<see cref="AudioFormats"/>）。
///
/// 两条安全规则：
/// 1. 删除一律<b>二次确认</b>（第一次点只是把按钮变成"再点一次确认"），且走回收站；
/// 2. 整理进行中不允许关闭对话框，免得任务还在跑而界面已经没了。
/// </summary>
internal sealed class FormatTool
{
    private const string ConvertLabel = "开始转换";
    private const string DeleteEncryptedLabel = "删除加密文件";
    private const string DeleteAudioLabel = "删除音频文件";

    private static readonly (string Label, AudioFormat Format)[] FormatChoices =
    {
        ("MP3", AudioFormat.Mp3),
        ("WAV", AudioFormat.Wav),
        ("FLAC", AudioFormat.Flac),
    };

    private readonly MainWindow _main;
    private readonly ContentDialog _dialog;
    private readonly TextBox _dirBox;
    private readonly ComboBox _formatBox;
    private readonly Button _browseButton;
    private readonly Button _convertButton;
    private readonly Button _deleteEncryptedButton;
    private readonly Button _deleteAudioButton;
    private readonly TextBox _logBox;

    private bool _running;
    private string? _pendingDelete;   // "encrypted" / "audio"：二次确认状态

    private FormatTool(MainWindow main)
    {
        _main = main;

        _dirBox = new TextBox
        {
            FontSize = 13,
            CornerRadius = new CornerRadius(8),
            PlaceholderText = "选择一个目录，或直接把路径粘进来",
        };

        _browseButton = new Button
        {
            Content = "浏览…",
            FontSize = 13,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 6, 14, 6),
        };
        _browseButton.Click += OnBrowse;

        _formatBox = new ComboBox
        {
            FontSize = 13,
            CornerRadius = new CornerRadius(8),
            MinWidth = 110,
            SelectedIndex = 0,
        };
        foreach (var (label, _) in FormatChoices) _formatBox.Items.Add(label);

        _convertButton = new Button
        {
            Content = ConvertLabel,
            FontSize = 13,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 8, 16, 8),
        };
        _convertButton.Click += OnConvert;

        _deleteEncryptedButton = new Button
        {
            Content = DeleteEncryptedLabel,
            FontSize = 13,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 8, 14, 8),
        };
        _deleteEncryptedButton.Click += async (_, _) => await RequestDeleteAsync("encrypted");

        _deleteAudioButton = new Button
        {
            Content = DeleteAudioLabel,
            FontSize = 13,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 8, 14, 8),
        };
        _deleteAudioButton.Click += async (_, _) => await RequestDeleteAsync("audio");

        _logBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            FontSize = 11,
            Height = 240,
            CornerRadius = new CornerRadius(8),
        };
        ScrollViewer.SetVerticalScrollBarVisibility(_logBox, ScrollBarVisibility.Auto);

        _dialog = new ContentDialog
        {
            Title = "格式整理工具",
            Content = BuildContent(),
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.None,
        };

        // 默认的对话框最大宽度（约 548）比这里的内容窄，右侧的"浏览…""开始转换"
        // 会被裁到看不见、点不到 —— 显式放宽
        _dialog.Resources["ContentDialogMaxWidth"] = 720.0;

        // 整理跑到一半被关掉的话，任务还在后台写文件、界面却没了 —— 先挡住
        _dialog.Closing += (_, e) =>
        {
            if (!_running) return;
            e.Cancel = true;
            Append("整理进行中，完成后才能关闭");
        };
    }

    /// <summary>打开对话框（必须在窗口已经显示之后调用，否则拿不到 XamlRoot）。</summary>
    internal static async Task ShowAsync(MainWindow main)
    {
        if (main.Content?.XamlRoot is not { } xamlRoot) return;

        var tool = new FormatTool(main);
        tool._dialog.XamlRoot = xamlRoot;
        await tool._dialog.ShowAsync();
    }

    private UIElement BuildContent()
    {
        // 布局要点：凡是"输入框/说明 + 按钮"的行都用星号列 Grid，不用横向 StackPanel。
        // 横向 StackPanel 以无限宽度测量子元素，内容比对话框窄时右边的按钮会被顶出可见区域
        // （用户报的"路径选择按键被挤到无法点击的地方"就是这么来的）。
        var panel = new StackPanel { Spacing = 12, MinWidth = 440 };

        // ── 目录行：输入框可被压缩，按钮保住最小宽度 ──
        _dirBox.MinWidth = 0;
        _browseButton.MinWidth = 88;
        _browseButton.VerticalAlignment = VerticalAlignment.Center;

        var dirRow = new Grid { ColumnSpacing = 8 };
        dirRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dirRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_dirBox, 0);
        Grid.SetColumn(_browseButton, 1);
        dirRow.Children.Add(_dirBox);
        dirRow.Children.Add(_browseButton);
        panel.Children.Add(dirRow);

        panel.Children.Add(new TextBlock
        {
            Text = "只处理所选目录本身，不递归子目录。转换不会覆盖原文件；目标格式与文件相同时会跳过。",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
        });

        // ── 转码行 ──
        _formatBox.VerticalAlignment = VerticalAlignment.Center;
        _convertButton.HorizontalAlignment = HorizontalAlignment.Left;
        _convertButton.VerticalAlignment = VerticalAlignment.Center;

        var convertRow = new Grid { ColumnSpacing = 8 };
        convertRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        convertRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        convertRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var convertLabel = new TextBlock
        {
            Text = "统一转换为",
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(convertLabel, 0);
        Grid.SetColumn(_formatBox, 1);
        Grid.SetColumn(_convertButton, 2);
        convertRow.Children.Add(convertLabel);
        convertRow.Children.Add(_formatBox);
        convertRow.Children.Add(_convertButton);
        panel.Children.Add(convertRow);

        // ── 删除行 ──
        var deleteRow = new Grid { ColumnSpacing = 8 };
        deleteRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        deleteRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        deleteRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_deleteEncryptedButton, 0);
        Grid.SetColumn(_deleteAudioButton, 1);
        deleteRow.Children.Add(_deleteEncryptedButton);
        deleteRow.Children.Add(_deleteAudioButton);
        panel.Children.Add(deleteRow);

        panel.Children.Add(new TextBlock
        {
            Text = $"加密文件 = {string.Join(" ", AudioFormats.Encrypted)}；"
                 + $"音频文件 = {string.Join(" ", AudioFormats.Common)}。删除需点两次确认，且只进回收站。",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
        });

        _logBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        panel.Children.Add(_logBox);
        return panel;
    }

    // ── 交互 ──

    private async void OnBrowse(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder,
            };
            picker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_main);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync().AsTask();
            if (folder is null) return;

            _dirBox.Text = folder.Path;
            ResetPendingDelete();
            Append($"目录: {folder.Path}");
        }
        catch (Exception ex)
        {
            Append($"✗ 选择目录失败: {ex.Message}");
        }
    }

    private async void OnConvert(object sender, RoutedEventArgs e)
    {
        if (_running) return;
        ResetPendingDelete();

        if (!TryGetDirectory(out var dir)) return;

        var format = FormatChoices[Math.Clamp(_formatBox.SelectedIndex, 0, FormatChoices.Length - 1)].Format;

        string[] files;
        try
        {
            files = Directory.GetFiles(dir).Where(AudioFormats.IsCommonAudio).ToArray();
        }
        catch (Exception ex)
        {
            Append($"✗ 读取目录失败: {ex.Message}");
            return;
        }

        if (files.Length == 0)
        {
            Append($"目录里没有可转换的音频文件（{string.Join(" ", AudioFormats.Common)}）");
            return;
        }

        var ffmpeg = Path.Combine(Workspace.OutputDir, "ffmpeg.exe");
        if (!File.Exists(ffmpeg))
        {
            Workspace.EnsureCreated();
            Workspace.SeedEngines();
        }
        if (!File.Exists(ffmpeg))
        {
            Append("✗ 找不到 ffmpeg.exe（程序目录里没带，工作区也没播种成功）");
            return;
        }

        SetRunning(true);
        Append($"=== 统一转换为 {AudioFormats.Display(format)}：共 {files.Length} 个文件 ===");
        _main.AppendLog($"格式整理：{dir} → {AudioFormats.Display(format)}（{files.Length} 个文件）\n");

        try
        {
            await Task.Run(() =>
            {
                int total = files.Length, done = 0, ok = 0, skip = 0, fail = 0;

                foreach (var file in files)
                {
                    done++;

                    if (AudioFormats.IsAlready(format, file))
                    {
                        skip++;
                        Append($"[{done}/{total}] 跳过 {Path.GetFileName(file)}：本身已是 {AudioFormats.Display(format)}");
                        continue;
                    }

                    var target = Path.Combine(dir,
                        Path.GetFileNameWithoutExtension(file) + AudioFormats.Extension(format));
                    Append($"[{done}/{total}] 转 {AudioFormats.Display(format)}: {Path.GetFileName(file)}");

                    if (AudioFormats.Convert(ffmpeg, format, file, target, out var detail))
                    {
                        ok++;
                        Append($"  ✓ {Path.GetFileName(target)}（时长 {detail}）");
                    }
                    else
                    {
                        fail++;
                        Append($"  ✗ 失败: {detail}");
                    }
                }

                Append($"=== 完成：成功 {ok} · 跳过 {skip} · 失败 {fail} ===");
            });
        }
        finally
        {
            SetRunning(false);
        }
    }

    /// <summary>删除请求：第一次点击只是"上膛"，再点一次同样按钮才真的删。</summary>
    private async Task RequestDeleteAsync(string kind)
    {
        if (_running) return;
        if (!TryGetDirectory(out var dir)) return;

        var extensions = kind == "encrypted" ? AudioFormats.Encrypted : AudioFormats.Common;
        var label = kind == "encrypted" ? DeleteEncryptedLabel : DeleteAudioLabel;

        string[] files;
        try
        {
            files = Directory.GetFiles(dir)
                .Where(f => AudioFormats.HasExtension(f, extensions))
                .ToArray();
        }
        catch (Exception ex)
        {
            Append($"✗ 读取目录失败: {ex.Message}");
            return;
        }

        if (files.Length == 0)
        {
            ResetPendingDelete();
            Append($"目录里没有 {string.Join(" ", extensions)} 文件");
            return;
        }

        // 上膛：改按钮文案并等第二次点击
        if (_pendingDelete != kind)
        {
            ResetPendingDelete();
            _pendingDelete = kind;
            var button = kind == "encrypted" ? _deleteEncryptedButton : _deleteAudioButton;
            button.Content = $"再点一次确认删除 {files.Length} 个";
            Append($"⚠ 将删除 {files.Length} 个 {string.Join(" ", extensions)} 文件（放入回收站）。再点一次「再点一次确认删除 {files.Length} 个」执行。");
            return;
        }

        ResetPendingDelete();
        Append($"=== 删除 {files.Length} 个 {label} ===");
        _main.AppendLog($"格式整理：删除 {dir} 下的 {files.Length} 个文件（{string.Join(" ", extensions)}）\n");

        SetRunning(true);
        try
        {
            var (deleted, failed) = await Task.Run(() =>
            {
                int ok = 0, bad = 0;
                foreach (var file in files)
                {
                    if (FileOps.DeleteToRecycleBin(file))
                    {
                        ok++;
                        Append($"  🗑 {Path.GetFileName(file)}");
                    }
                    else
                    {
                        bad++;
                        Append($"  ✗ 删除失败: {Path.GetFileName(file)}");
                    }
                }
                return (ok, bad);
            });

            Append($"=== 完成：已放入回收站 {deleted} 个 · 失败 {failed} 个 ===");
        }
        finally
        {
            SetRunning(false);
        }
    }

    // ── 辅助 ──

    private bool TryGetDirectory(out string dir)
    {
        dir = _dirBox.Text.Trim();
        if (dir.Length == 0)
        {
            Append("✗ 请先选择目录");
            return false;
        }
        if (!Directory.Exists(dir))
        {
            Append($"✗ 目录不存在: {dir}");
            return false;
        }
        return true;
    }

    private void SetRunning(bool running)
    {
        _running = running;
        _browseButton.IsEnabled = !running;
        _convertButton.IsEnabled = !running;
        _deleteEncryptedButton.IsEnabled = !running;
        _deleteAudioButton.IsEnabled = !running;
        _convertButton.Content = running ? "整理中…" : ConvertLabel;
    }

    private void ResetPendingDelete()
    {
        _pendingDelete = null;
        _deleteEncryptedButton.Content = DeleteEncryptedLabel;
        _deleteAudioButton.Content = DeleteAudioLabel;
    }

    private void Append(string message)
    {
        AppLog.Log(message);
        _main.RunOnUi(() =>
        {
            _logBox.Text += message + Environment.NewLine;
            _logBox.SelectionStart = _logBox.Text.Length;
        });
    }
}
