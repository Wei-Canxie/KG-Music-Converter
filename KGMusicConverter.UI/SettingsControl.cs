using System;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace KGMusicConverter;

internal sealed class SettingsControl : UserControl
{
    private MainWindow? _main;
    private Settings _settings;
    private TextBox? _rInput;
    private TextBox? _gInput;
    private TextBox? _bInput;
    private TextBox? _hexInput;
    private bool _isHexMode = false;

    public SettingsControl(MainWindow main)
    {
        _main = main;
        _settings = Settings.Load();
        BuildUI();
    }

    private void BuildUI()
    {
        var tm = ThemeManager.Instance;

        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var panel = new StackPanel { Spacing = 12, Padding = new Thickness(24, 16, 24, 16) };
        panel.Children.Add(Header("外观设置"));

        // ── 主题 ──
        panel.Children.Add(new TextBlock { Text = "主题", FontWeight = FontWeights.SemiBold });
        var themePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

        var themeFollowRadio = new RadioButton { Content = "跟随系统" };
        var themeLightRadio = new RadioButton { Content = "亮色" };
        var themeDarkRadio = new RadioButton { Content = "暗色" };

        switch (_settings.Theme)
        {
            case ThemeMode.Light: themeLightRadio.IsChecked = true; break;
            case ThemeMode.Dark: themeDarkRadio.IsChecked = true; break;
            default: themeFollowRadio.IsChecked = true; break;
        }

        themeFollowRadio.Checked += (_, _) => { _settings.Theme = ThemeMode.System; ApplySettings(); };
        themeLightRadio.Checked += (_, _) => { _settings.Theme = ThemeMode.Light; ApplySettings(); };
        themeDarkRadio.Checked += (_, _) => { _settings.Theme = ThemeMode.Dark; ApplySettings(); };

        themePanel.Children.Add(themeFollowRadio);
        themePanel.Children.Add(themeLightRadio);
        themePanel.Children.Add(themeDarkRadio);
        panel.Children.Add(themePanel);

        // ── 主题色 ──
        panel.Children.Add(new TextBlock { Text = "主题色", FontWeight = FontWeights.SemiBold });
        var color = tm.AccentColor;

        // RGB/HEX 切换
        var togglePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var btnRgb = new Button
        {
            Content = "RGB",
            FontSize = 11,
            Background = !_isHexMode ? tm.Accent : tm.Surface,
            Foreground = !_isHexMode ? new SolidColorBrush(Colors.White) : tm.Text,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 4, 10, 4),
        };
        btnRgb.Click += (_, _) => { _isHexMode = false; BuildUI(); };
        var btnHex = new Button
        {
            Content = "HEX",
            FontSize = 11,
            Background = _isHexMode ? tm.Accent : tm.Surface,
            Foreground = _isHexMode ? new SolidColorBrush(Colors.White) : tm.Text,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 4, 10, 4),
        };
        btnHex.Click += (_, _) => { _isHexMode = true; BuildUI(); };
        togglePanel.Children.Add(btnRgb);
        togglePanel.Children.Add(btnHex);

        if (_isHexMode)
        {
            _hexInput = new TextBox
            {
                Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}",
                FontSize = 13,
                CornerRadius = new CornerRadius(8),
                MaxWidth = 200,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            _hexInput.KeyDown += (_, args) =>
            {
                if (args.Key == Windows.System.VirtualKey.Enter)
                    TryParseHex(_hexInput.Text);
            };
            _hexInput.LostFocus += (_, _) => TryParseHex(_hexInput.Text);
            togglePanel.Children.Add(_hexInput);
        }
        else
        {
            var rgbInputs = new Grid { ColumnSpacing = 8 };
            rgbInputs.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rgbInputs.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rgbInputs.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _rInput = MakeRgbInput(color.R, "R", v => UpdateThemeColor((byte)v, color.G, color.B));
            _gInput = MakeRgbInput(color.G, "G", v => UpdateThemeColor(color.R, (byte)v, color.B));
            _bInput = MakeRgbInput(color.B, "B", v => UpdateThemeColor(color.R, color.G, (byte)v));
            Grid.SetColumn(_rInput, 0);
            Grid.SetColumn(_gInput, 1);
            Grid.SetColumn(_bInput, 2);
            rgbInputs.Children.Add(_rInput);
            rgbInputs.Children.Add(_gInput);
            rgbInputs.Children.Add(_bInput);
            togglePanel.Children.Add(rgbInputs);
        }

        panel.Children.Add(togglePanel);

        // 颜色预览
        var previewPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        previewPanel.Children.Add(new Ellipse { Width = 24, Height = 24, Fill = tm.Accent });
        previewPanel.Children.Add(new TextBlock
        {
            Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}",
            FontSize = 12,
            Foreground = tm.SubText,
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(previewPanel);

        // ── 窗口不透明度 ──
        panel.Children.Add(new TextBlock { Text = "不透明度", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0) });
        panel.Children.Add(BuildSliderWithTextBox("窗口不透明度", _settings.WindowOpacity, 0.3, 1.0,
            v => { _settings.WindowOpacity = v; ApplyOpacity(); },
            step: 0.05, format: "0%"));

        // ── 背景图片 ──
        panel.Children.Add(new TextBlock { Text = "背景图片", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0) });
        var bgPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        var bgPathLabel = new TextBlock
        {
            Text = string.IsNullOrEmpty(_settings.BackgroundImagePath) ? "(无)" : System.IO.Path.GetFileName(_settings.BackgroundImagePath),
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 140,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var selectBgBtn = new Button { Content = "选择图片" };
        selectBgBtn.Click += (_, _) =>
        {
            var path = OnBrowseBackground();
            if (!string.IsNullOrEmpty(path))
            {
                _settings.BackgroundImagePath = path;
                bgPathLabel.Text = System.IO.Path.GetFileName(path);
                ApplySettings();
            }
        };

        var clearBgBtn = new Button { Content = "恢复默认" };
        clearBgBtn.Click += (_, _) =>
        {
            _settings.BackgroundImagePath = null;
            bgPathLabel.Text = "(无)";
            ApplySettings();
        };

        bgPanel.Children.Add(bgPathLabel);
        bgPanel.Children.Add(selectBgBtn);
        bgPanel.Children.Add(clearBgBtn);
        panel.Children.Add(bgPanel);

        panel.Children.Add(BuildSliderWithTextBox("背景图不透明度", _settings.BackgroundImageOpacity, 0.0, 1.0,
            v => { _settings.BackgroundImageOpacity = v; _main?.SetBackgroundImageOpacity(v); ApplySettings(); },
            step: 0.05, format: "0%"));

        // ── 背景模糊 ──
        panel.Children.Add(new TextBlock { Text = "背景效果", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0) });
        var blurPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

        var blurNoneRadio = new RadioButton { Content = "默认" };
        var blurMicaRadio = new RadioButton { Content = "云母 (Mica)" };
        var blurAcrylicRadio = new RadioButton { Content = "亚克力 (Acrylic)" };

        switch (_settings.Blur)
        {
            case BlurMode.Mica: blurMicaRadio.IsChecked = true; break;
            case BlurMode.Acrylic: blurAcrylicRadio.IsChecked = true; break;
            default: blurNoneRadio.IsChecked = true; break;
        }

        blurNoneRadio.Checked += (_, _) => { _settings.Blur = BlurMode.None; ApplySettings(); };
        blurMicaRadio.Checked += (_, _) => { _settings.Blur = BlurMode.Mica; ApplySettings(); };
        blurAcrylicRadio.Checked += (_, _) => { _settings.Blur = BlurMode.Acrylic; ApplySettings(); };

        blurPanel.Children.Add(blurNoneRadio);
        blurPanel.Children.Add(blurMicaRadio);
        blurPanel.Children.Add(blurAcrylicRadio);
        panel.Children.Add(blurPanel);

        // 模糊半径（像素半径，滑块 0-255，文本框可到 1024）
        panel.Children.Add(BuildSliderWithTextBox("模糊半径", _settings.BlurRadius, 0, 255,
            v => { _settings.BlurRadius = v; ApplyBlur(); ApplySettings(); },
            step: 1, format: "0", textMin: 0, textMax: 1024));

        scroll.Content = panel;
        Content = scroll;
    }

    // ⭐ 核心复用组件：Slider + TextBox + ±按钮 三件套（OsuCursorWin3 模板）
    private FrameworkElement BuildSliderWithTextBox(
        string label, double value,
        double sliderMin, double sliderMax,
        Action<double> apply,
        double step = 1.0, string format = "0.##",
        double? textMin = null, double? textMax = null)
    {
        double tMin = textMin ?? sliderMin;
        double tMax = textMax ?? sliderMax;

        // 四列布局：标签 | Slider | TextBox | ±按钮
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110, GridUnitType.Pixel) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70, GridUnitType.Pixel) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelText = new TextBlock
        {
            Text = label,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
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
        };

        var valueBox = new TextBox
        {
            Text = value.ToString(format),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
        };

        // ± 按钮：TextBlock 作 Content（保证居中），32×32
        var minusText = new TextBlock
        {
            Text = "−",
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var plusText = new TextBlock
        {
            Text = "+",
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var minusBtn = new Button
        {
            Content = minusText,
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 0, 1, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var plusBtn = new Button
        {
            Content = plusText,
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Margin = new Thickness(1, 0, 2, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        // Slider → TextBox + apply
        slider.ValueChanged += (_, _) =>
        {
            var v = Math.Clamp(slider.Value, sliderMin, sliderMax);
            valueBox.Text = v.ToString(format);
            apply(v);
        };

        // TextBox 输入（可超出 slider 范围，但受 textMin/textMax 约束）
        void ApplyFromText()
        {
            if (double.TryParse(valueBox.Text, out var v))
            {
                v = Math.Clamp(v, tMin, tMax);
                if (v >= sliderMin && v <= sliderMax) slider.Value = v;
                valueBox.Text = v.ToString(format);
                apply(v);
            }
            else valueBox.Text = slider.Value.ToString(format);
        }

        valueBox.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter) { ApplyFromText(); e.Handled = true; }
        };
        valueBox.LostFocus += (_, _) => ApplyFromText();

        // ± 按钮
        minusBtn.Click += (_, _) => { slider.Value = Math.Max(sliderMin, slider.Value - step); };
        plusBtn.Click += (_, _) => { slider.Value = Math.Min(sliderMax, slider.Value + step); };

        var buttonsPanel = new StackPanel { Orientation = Orientation.Horizontal };
        buttonsPanel.Children.Add(minusBtn);
        buttonsPanel.Children.Add(plusBtn);

        Grid.SetColumn(labelText, 0);
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(valueBox, 2);
        Grid.SetColumn(buttonsPanel, 3);
        grid.Children.Add(labelText);
        grid.Children.Add(slider);
        grid.Children.Add(valueBox);
        grid.Children.Add(buttonsPanel);

        return grid;
    }

    private void ApplyOpacity()
    {
        _settings.Save();
        _main?.ApplyOpacity(_settings.WindowOpacity, _settings.WindowOpacity);
    }

    private TextBox MakeRgbInput(byte value, string header, Action<byte> onChanged)
    {
        var tm = ThemeManager.Instance;
        var tb = new TextBox
        {
            Text = value.ToString(),
            FontSize = 13,
            CornerRadius = new CornerRadius(8),
            Header = header,
        };
        tb.KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Enter && byte.TryParse(tb.Text, out var v))
            {
                onChanged(v);
            }
        };
        tb.LostFocus += (_, _) =>
        {
            if (byte.TryParse(tb.Text, out var v))
            {
                onChanged(v);
            }
        };
        return tb;
    }

    private string? OnBrowseBackground()
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

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_main!);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            return picker.PickSingleFileAsync().AsTask().Result?.Path;
        }
        catch
        {
            return null;
        }
    }

    private void TryParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6 &&
            byte.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) &&
            byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
            byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            UpdateThemeColor(r, g, b);
        }
    }

    private void UpdateThemeColor(byte r, byte g, byte b)
    {
        ThemeManager.Instance.AccentColor = ColorHelper.FromArgb(255, r, g, b);
        _settings.ThemeR = r;
        _settings.ThemeG = g;
        _settings.ThemeB = b;
        _settings.Save();
        BuildUI();
    }

    private void ApplySettings()
    {
        _settings.Save();
        _main?.ApplyAllSettings(_settings);
    }

    private void ApplyBlur()
    {
        try
        {
            if (_main != null)
            {
                if (_settings.Blur == BlurMode.Mica)
                {
                    _main.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
                }
                else if (_settings.Blur == BlurMode.Acrylic)
                {
                    _main.SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
                }
                else
                {
                    _main.SystemBackdrop = null;
                }
                _main.ApplyBlurRadius(_settings.BlurRadius, _settings.Blur);
            }
        }
        catch
        {
            if (_main != null) _main.SystemBackdrop = null;
        }
    }

    private static TextBlock Header(string text) => new()
    {
        Text = text,
        FontSize = 20,
        FontWeight = FontWeights.SemiBold
    };
}
