using System;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace KGMusicConverter;

/// <summary>
/// 设置页：所有控件只改<b>草稿</b>并标记脏，右下角浮出"应用 / 取消更改"卡片。
///
/// 硬规则：这里的回调里绝不调用 Save()——否则未应用的改动会写盘，
/// 草稿模型当场失效。
///
/// 外观类改动会即时预览到窗口上（主题 / 不透明度 / 背景），
/// 这样用户能在按下"应用"之前就看见效果；但拖动不透明度滑条时不重建页面，
/// 否则正在拖的那个滑条会被销毁。
/// </summary>
internal sealed class SettingsControl : ToolPage
{
    private const string PageTag = "settings";

    private readonly MainWindow _main;
    private ScrollViewer? _scroll;

    public SettingsControl(MainWindow main)
    {
        _main = main;
        BuildUI();
    }

    private Settings Draft => _main.Draft;

    private void BuildUI()
    {
        var tm = ThemeManager.Instance;

        _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var page = new StackPanel
        {
            Spacing = 12,
            Padding = new Thickness(24, 16, 24, 16),
            MaxWidth = 640,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        page.Children.Add(Header("外观设置"));
        page.Children.Add(Description("改动先写进草稿，点击右下角“应用”后才会真正生效；点“取消更改”放弃。"));

        // ── 主题 ──
        page.Children.Add(Section("主题"));
        var themeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

        var followRadio = new RadioButton { Content = "跟随系统" };
        var lightRadio = new RadioButton { Content = "亮色" };
        var darkRadio = new RadioButton { Content = "暗色" };
        switch (Draft.Theme)
        {
            case ThemeMode.Light: lightRadio.IsChecked = true; break;
            case ThemeMode.Dark: darkRadio.IsChecked = true; break;
            default: followRadio.IsChecked = true; break;
        }

        followRadio.Checked += (_, _) => SetTheme(ThemeMode.System);
        lightRadio.Checked += (_, _) => SetTheme(ThemeMode.Light);
        darkRadio.Checked += (_, _) => SetTheme(ThemeMode.Dark);

        themeRow.Children.Add(followRadio);
        themeRow.Children.Add(lightRadio);
        themeRow.Children.Add(darkRadio);
        page.Children.Add(themeRow);

        // ── 主题色 ──
        page.Children.Add(Section("主题色"));
        var colorRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };

        // 预览块显示<b>草稿</b>里的颜色：这是控件自身的状态回显，不是窗口外观预览
        var swatch = new Ellipse
        {
            Width = 24,
            Height = 24,
            Fill = new SolidColorBrush(ColorHelper.FromArgb(255, Draft.ThemeR, Draft.ThemeG, Draft.ThemeB)),
        };
        colorRow.Children.Add(swatch);

        var colorBox = new TextBox
        {
            Text = $"#{Draft.ThemeR:X2}{Draft.ThemeG:X2}{Draft.ThemeB:X2}",
            FontSize = 13,
            Width = 120,
            VerticalAlignment = VerticalAlignment.Center,
        };
        void CommitColor()
        {
            var hex = colorBox.Text.TrimStart('#').Trim();
            if (hex.Length == 6 &&
                byte.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) &&
                byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
                byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
            {
                Draft.ThemeR = r;
                Draft.ThemeG = g;
                Draft.ThemeB = b;
                _main.MarkDirty();
                _main.RebuildCurrentPage();
            }
            else
            {
                colorBox.Text = $"#{Draft.ThemeR:X2}{Draft.ThemeG:X2}{Draft.ThemeB:X2}";
            }
        }
        colorBox.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter) { CommitColor(); e.Handled = true; }
        };
        colorBox.LostFocus += (_, _) => CommitColor();
        colorRow.Children.Add(colorBox);

        colorRow.Children.Add(new TextBlock
        {
            Text = "HEX（如 #FF66AB）",
            FontSize = 12,
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Center,
        });
        page.Children.Add(colorRow);

        // ── 不透明度 ──
        page.Children.Add(Section("不透明度"));
        bool material = Draft.Blur != BlurMode.None;

        if (material)
        {
            page.Children.Add(Description("背景材质（云母 / 亚克力）激活时窗口不透明度固定为 100%。"));
        }

        page.Children.Add(BuildSliderWithTextBox(
            material ? "窗口不透明度（材质激活时固定）" : "窗口不透明度",
            Draft.WindowOpacity,
            0.3,
            1.0,
            value =>
            {
                Draft.WindowOpacity = value;
                _main.MarkDirty();          // → PreviewDraftAppearance，不重建页面
            },
            step: 0.05,
            format: "P0",
            enabled: !material));

        // ── 背景图片 ──
        page.Children.Add(Section("背景图片"));

        var pathLabel = new TextBlock
        {
            Text = DescribeImage(Draft.BackgroundImagePath),
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 200,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var chooseButton = new Button { Content = "选择图片…" };
        chooseButton.Click += async (_, _) =>
        {
            var path = await PickImageAsync();
            if (!string.IsNullOrEmpty(path))
            {
                Draft.BackgroundImagePath = path;
                pathLabel.Text = DescribeImage(path);
                _main.MarkDirty();
            }
        };

        var clearButton = new Button { Content = "清除" };
        clearButton.Click += (_, _) =>
        {
            Draft.BackgroundImagePath = null;
            pathLabel.Text = DescribeImage(null);
            _main.MarkDirty();
        };

        var imageRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        imageRow.Children.Add(pathLabel);
        imageRow.Children.Add(chooseButton);
        imageRow.Children.Add(clearButton);
        page.Children.Add(imageRow);

        page.Children.Add(BuildSliderWithTextBox(
            "背景图不透明度",
            Draft.BackgroundImageOpacity,
            0.0,
            1.0,
            value =>
            {
                Draft.BackgroundImageOpacity = value;
                _main.MarkDirty();
            },
            step: 0.05));

        // ── 背景效果 ──
        page.Children.Add(Section("背景效果"));
        var blurRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

        var noneRadio = new RadioButton { Content = "默认" };
        var micaRadio = new RadioButton { Content = "云母 (Mica)" };
        var acrylicRadio = new RadioButton { Content = "亚克力 (Acrylic)" };
        switch (Draft.Blur)
        {
            case BlurMode.Mica: micaRadio.IsChecked = true; break;
            case BlurMode.Acrylic: acrylicRadio.IsChecked = true; break;
            default: noneRadio.IsChecked = true; break;
        }

        noneRadio.Checked += (_, _) => SetBlur(BlurMode.None);
        micaRadio.Checked += (_, _) => SetBlur(BlurMode.Mica);
        acrylicRadio.Checked += (_, _) => SetBlur(BlurMode.Acrylic);

        blurRow.Children.Add(noneRadio);
        blurRow.Children.Add(micaRadio);
        blurRow.Children.Add(acrylicRadio);
        page.Children.Add(blurRow);

        // 模糊半径：滑条覆盖常用区间，数字框可以超出（0–1024）
        page.Children.Add(BuildSliderWithTextBox(
            "模糊半径",
            Draft.BlurRadius,
            0,
            255,
            value =>
            {
                Draft.BlurRadius = value;
                _main.MarkDirty();
            },
            step: 1,
            format: "0",
            textMax: 1024));

        _scroll.Content = page;
        _scroll.ViewChanged += (_, _) => _main.SetScrollOffset(PageTag, _scroll.VerticalOffset);
        Content = _scroll;
    }

    private void SetTheme(ThemeMode mode)
    {
        if (Draft.Theme == mode) return;
        Draft.Theme = mode;
        _main.MarkDirty();   // 不做即时预览：等"应用"才换主题
    }

    private void SetBlur(BlurMode mode)
    {
        if (Draft.Blur == mode) return;
        Draft.Blur = mode;
        _main.MarkDirty();
        // 重建页面：材质模式下"窗口不透明度"整行要变成禁用 + 改写文案
        _main.RebuildCurrentPage();
    }

    private static string DescribeImage(string? path) =>
        string.IsNullOrEmpty(path) ? "(无)" : System.IO.Path.GetFileName(path);

    private async System.Threading.Tasks.Task<string?> PickImageAsync()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail,
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary,
            };
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".bmp");
            picker.FileTypeFilter.Add(".webp");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_main);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var result = await picker.PickSingleFileAsync().AsTask();
            return result?.Path;
        }
        catch (Exception ex)
        {
            AppLog.Log($"PickImageAsync failed: {ex.Message}");
            return null;
        }
    }

    // ── 控件助手 ──

    /// <summary>
    /// 数值行三件套：标签 | 滑条 | 数字框 | − / +（四列 110 / * / 70 / auto）。
    /// 滑条负责快速拖动，数字框可以超出滑条范围。
    /// </summary>
    private FrameworkElement BuildSliderWithTextBox(
        string label,
        double value,
        double sliderMin,
        double sliderMax,
        Action<double> apply,
        double step = 1.0,
        string format = "0.##",
        double? textMin = null,
        double? textMax = null,
        bool enabled = true)
    {
        double tMin = textMin ?? sliderMin;
        double tMax = textMax ?? sliderMax;

        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110, GridUnitType.Pixel) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70, GridUnitType.Pixel) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelText = new TextBlock
        {
            Text = label,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var slider = new Slider
        {
            Minimum = sliderMin,
            Maximum = sliderMax,
            Value = Math.Clamp(value, sliderMin, sliderMax),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
            SmallChange = step,
            LargeChange = step * 10,
            StepFrequency = step,
            IsEnabled = enabled,
        };

        var valueBox = new TextBox
        {
            Text = value.ToString(format),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
            IsEnabled = enabled,
        };

        var minusBtn = new Button
        {
            Content = new TextBlock { Text = "−", FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 0, 1, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            IsEnabled = enabled,
        };
        var plusBtn = new Button
        {
            Content = new TextBlock { Text = "+", FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Margin = new Thickness(1, 0, 2, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            IsEnabled = enabled,
        };

        slider.ValueChanged += (_, _) =>
        {
            var v = Math.Clamp(slider.Value, sliderMin, sliderMax);
            valueBox.Text = v.ToString(format);
            apply(v);
        };

        void ApplyFromText()
        {
            if (double.TryParse(valueBox.Text, out var v))
            {
                v = Math.Clamp(v, tMin, tMax);
                if (v >= sliderMin && v <= sliderMax) slider.Value = v;
                valueBox.Text = v.ToString(format);
                apply(v);
            }
            else
            {
                valueBox.Text = slider.Value.ToString(format);
            }
        }

        valueBox.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter) { ApplyFromText(); e.Handled = true; }
        };
        valueBox.LostFocus += (_, _) => ApplyFromText();

        minusBtn.Click += (_, _) => slider.Value = Math.Max(sliderMin, slider.Value - step);
        plusBtn.Click += (_, _) => slider.Value = Math.Min(sliderMax, slider.Value + step);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(minusBtn);
        buttons.Children.Add(plusBtn);

        Grid.SetColumn(labelText, 0);
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(valueBox, 2);
        Grid.SetColumn(buttons, 3);
        grid.Children.Add(labelText);
        grid.Children.Add(slider);
        grid.Children.Add(valueBox);
        grid.Children.Add(buttons);

        if (!enabled) grid.Opacity = 0.55;
        return grid;
    }

    private static TextBlock Header(string text) => new()
    {
        Text = text,
        FontSize = 20,
        FontWeight = FontWeights.SemiBold,
    };

    private static TextBlock Section(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 8, 0, 0),
    };

    private static TextBlock Description(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Opacity = 0.7,
        TextWrapping = TextWrapping.Wrap,
    };
}
