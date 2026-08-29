using System;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace KugoMusicConverter;

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
            Padding = new Thickness(20, 16, 20, 16),
        };

        var root = new Grid
        {
            Background = tm.Background,
            MinWidth = 500,
            MinHeight = 400,
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var contentPanel = new StackPanel
        {
            Spacing = 14,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 580,
            MinWidth = 400,
        };

        // 标题
        contentPanel.Children.Add(new TextBlock
        {
            Text = "设置",
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Foreground = tm.Accent,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        // ── 外观卡片 ──
        var appearanceCard = new Border
        {
            Background = tm.CardBackground,
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(20),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var appearancePanel = new StackPanel { Spacing = 14 };

        appearancePanel.Children.Add(new TextBlock
        {
            Text = "外观",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = tm.Text,
        });

        // 主题模式
        var themePanel = new StackPanel { Spacing = 6 };
        themePanel.Children.Add(new TextBlock { Text = "主题", FontSize = 12, Foreground = tm.SubText });
        var themeButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        themeButtons.Children.Add(MakeThemeButton("跟随系统", ThemeMode.System));
        themeButtons.Children.Add(MakeThemeButton("亮色", ThemeMode.Light));
        themeButtons.Children.Add(MakeThemeButton("暗色", ThemeMode.Dark));
        themePanel.Children.Add(themeButtons);
        appearancePanel.Children.Add(themePanel);

        // 主题色（保留滑块）
        var colorPanel = new StackPanel { Spacing = 6 };
        colorPanel.Children.Add(new TextBlock { Text = "主题色", FontSize = 12, Foreground = tm.SubText });

        var color = tm.AccentColor;
        var slidersPanel = new StackPanel { Spacing = 4 };
        slidersPanel.Children.Add(MakeColorSlider("R", color.R, v => UpdateThemeColor((byte)v, color.G, color.B)));
        slidersPanel.Children.Add(MakeColorSlider("G", color.G, v => UpdateThemeColor(color.R, (byte)v, color.B)));
        slidersPanel.Children.Add(MakeColorSlider("B", color.B, v => UpdateThemeColor(color.R, color.G, (byte)v)));
        colorPanel.Children.Add(slidersPanel);

        // RGB/HEX 切换
        var togglePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
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
        colorPanel.Children.Add(togglePanel);

        if (_isHexMode)
        {
            _hexInput = new TextBox
            {
                Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}",
                FontSize = 13,
                Background = tm.Surface,
                Foreground = tm.Text,
                CornerRadius = new CornerRadius(8),
            };
            _hexInput.KeyDown += (_, args) =>
            {
                if (args.Key == Windows.System.VirtualKey.Enter)
                    TryParseHex(_hexInput.Text);
            };
            colorPanel.Children.Add(_hexInput);
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
            colorPanel.Children.Add(rgbInputs);
        }

        // 颜色预览
        var previewPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        previewPanel.Children.Add(new Ellipse { Width = 24, Height = 24, Fill = tm.Accent });
        previewPanel.Children.Add(new TextBlock
        {
            Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}",
            FontSize = 12,
            Foreground = tm.Text,
            VerticalAlignment = VerticalAlignment.Center,
        });
        colorPanel.Children.Add(previewPanel);
        appearancePanel.Children.Add(colorPanel);

        // ── 不透明度控制 ──
        appearancePanel.Children.Add(new TextBlock { Text = "不透明度", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = tm.Text, Margin = new Thickness(0, 8, 0, 0) });

        // 窗口不透明度
        appearancePanel.Children.Add(MakeOpacitySlider("窗口不透明度", _settings.WindowOpacity, v =>
        {
            _settings.WindowOpacity = v;
            ApplyOpacity();
        }));

        // 面板不透明度
        appearancePanel.Children.Add(MakeOpacitySlider("面板不透明度", _settings.PanelOpacity, v =>
        {
            _settings.PanelOpacity = v;
            ApplyOpacity();
        }));

        // 背景图不透明度
        appearancePanel.Children.Add(MakeOpacitySlider("背景图不透明度", _settings.BackgroundImageOpacity, v =>
        {
            _settings.BackgroundImageOpacity = v;
            _main?.SetBackgroundImageOpacity(v);
            _settings.Save();
        }));

        // 背景图选择
        var bgPanel = new StackPanel { Spacing = 6 };
        bgPanel.Children.Add(new TextBlock { Text = "窗口背景图", FontSize = 12, Foreground = tm.SubText });
        var bgButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var btnBrowseBg = new Button
        {
            Content = "选择图片…",
            FontSize = 12,
            Background = tm.Surface,
            Foreground = tm.Text,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6, 12, 6),
        };
        btnBrowseBg.Click += OnBrowseBackground;
        bgButtons.Children.Add(btnBrowseBg);
        var btnClearBg = new Button
        {
            Content = "清除",
            FontSize = 12,
            Background = tm.Surface,
            Foreground = tm.Text,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6, 12, 6),
        };
        btnClearBg.Click += (_, _) =>
        {
            _settings.BackgroundImagePath = null;
            _settings.Save();
            _main?.ApplyAllSettings(_settings);
            BuildUI();
        };
        bgButtons.Children.Add(btnClearBg);
        bgPanel.Children.Add(bgButtons);
        bgPanel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(_settings.BackgroundImagePath) ? "未设置" : _settings.BackgroundImagePath,
            FontSize = 10,
            Foreground = tm.SubText,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 380,
        });
        appearancePanel.Children.Add(bgPanel);

        // 模糊模式
        var blurPanel = new StackPanel { Spacing = 6 };
        blurPanel.Children.Add(new TextBlock { Text = "背景模糊效果", FontSize = 12, Foreground = tm.SubText });
        var blurButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        blurButtons.Children.Add(MakeBlurButton("默认", BlurMode.None));
        blurButtons.Children.Add(MakeBlurButton("云母", BlurMode.Mica));
        blurButtons.Children.Add(MakeBlurButton("亚克力", BlurMode.Acrylic));
        blurPanel.Children.Add(blurButtons);

        // 模糊强度
        blurPanel.Children.Add(MakeOpacitySlider("模糊强度", _settings.BlurIntensity, v =>
        {
            _settings.BlurIntensity = v;
            ApplyBlur();
            ApplySettings();
        }));

        appearancePanel.Children.Add(blurPanel);

        appearanceCard.Child = appearancePanel;
        contentPanel.Children.Add(appearanceCard);

        Grid.SetRow(contentPanel, 0);
        root.Children.Add(contentPanel);

        scroll.Content = root;
        Content = scroll;
    }

    private StackPanel MakeOpacitySlider(string label, double value, Action<double> onChanged)
    {
        var tm = ThemeManager.Instance;
        var panel = new StackPanel { Spacing = 4 };
        int percent = (int)Math.Round(value * 100);
        var valueLabel = new TextBlock
        {
            Text = $"{label}: {percent}%",
            FontSize = 12,
            Foreground = tm.SubText,
        };
        panel.Children.Add(valueLabel);

        var inputPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var slider = new Slider
        {
            Minimum = 5,
            Maximum = 100,
            Value = percent,
            Width = 220,
            Foreground = tm.Accent,
            SmallChange = 1,
            LargeChange = 10,
            StepFrequency = 1,
        };

        var input = new TextBox
        {
            Text = percent.ToString(),
            FontSize = 12,
            Width = 55,
            Background = tm.Surface,
            Foreground = tm.Text,
            CornerRadius = new CornerRadius(6),
        };

        slider.ValueChanged += (_, args) =>
        {
            int pct = (int)Math.Round(args.NewValue);
            input.Text = pct.ToString();
            valueLabel.Text = $"{label}: {pct}%";
            onChanged(pct / 100.0);
        };

        input.KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Enter && int.TryParse(input.Text, out var pct))
            {
                pct = Math.Clamp(pct, 5, 100);
                slider.Value = pct;
                valueLabel.Text = $"{label}: {pct}%";
                onChanged(pct / 100.0);
            }
        };

        inputPanel.Children.Add(slider);
        inputPanel.Children.Add(input);
        panel.Children.Add(inputPanel);

        return panel;
    }

    private void ApplyOpacity()
    {
        _settings.Save();
        _main?.ApplyOpacity(_settings.WindowOpacity, _settings.PanelOpacity);
    }

    private TextBox MakeRgbInput(byte value, string header, Action<byte> onChanged)
    {
        var tm = ThemeManager.Instance;
        var tb = new TextBox
        {
            Text = value.ToString(),
            FontSize = 13,
            Background = tm.Surface,
            Foreground = tm.Text,
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

    private UIElement MakeColorSlider(string label, byte current, Action<double> onChanged)
    {
        var tm = ThemeManager.Instance;
        var panel = new Grid { ColumnSpacing = 8 };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelText = new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = tm.Text,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 18,
        };
        Grid.SetColumn(labelText, 0);
        panel.Children.Add(labelText);

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 255,
            Value = current,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = tm.Accent,
            SmallChange = 1,
            LargeChange = 10,
            StepFrequency = 1,
        };
        slider.ValueChanged += (_, args) => onChanged(args.NewValue);
        Grid.SetColumn(slider, 1);
        panel.Children.Add(slider);

        var valueText = new TextBlock
        {
            Text = current.ToString(),
            FontSize = 12,
            Foreground = tm.SubText,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 28,
        };
        Grid.SetColumn(valueText, 2);
        panel.Children.Add(valueText);

        return panel;
    }

    private Button MakeThemeButton(string text, ThemeMode mode)
    {
        var tm = ThemeManager.Instance;
        var btn = new Button
        {
            Content = text,
            FontSize = 12,
            Background = _settings.Theme == mode ? tm.Accent : tm.Surface,
            Foreground = _settings.Theme == mode ? new SolidColorBrush(Colors.White) : tm.Text,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6, 12, 6),
        };
        btn.Click += (_, _) =>
        {
            _settings.Theme = mode;
            ThemeManager.Instance.Mode = mode;
            ApplySettings();
            BuildUI();
        };
        return btn;
    }

    private Button MakeBlurButton(string text, BlurMode mode)
    {
        var tm = ThemeManager.Instance;
        var btn = new Button
        {
            Content = text,
            FontSize = 12,
            Background = _settings.Blur == mode ? tm.Accent : tm.Surface,
            Foreground = _settings.Blur == mode ? new SolidColorBrush(Colors.White) : tm.Text,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6, 12, 6),
        };
        btn.Click += (_, _) =>
        {
            _settings.Blur = mode;
            ApplyBlur();
            ApplySettings();
            BuildUI();
        };
        return btn;
    }

    private async void OnBrowseBackground(object sender, RoutedEventArgs e)
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

        var result = await picker.PickSingleFileAsync().AsTask();
        if (result != null)
        {
            _settings.BackgroundImagePath = result.Path;
            _settings.Save();
            _main?.ApplyAllSettings(_settings);
            BuildUI();
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
            bool isDark = _settings.Theme != ThemeMode.Light;
            if (_main != null)
            {
                if (_settings.Blur == BlurMode.Mica)
                {
                    _main.SystemBackdrop = new MicaBackdrop { Kind = isDark ? MicaKind.Base : MicaKind.BaseAlt };
                }
                else if (_settings.Blur == BlurMode.Acrylic)
                {
                    _main.SystemBackdrop = new DesktopAcrylicBackdrop();
                }
                else
                {
                    _main.SystemBackdrop = null;
                }
            }
        }
        catch
        {
            if (_main != null) _main.SystemBackdrop = null;
        }
    }
}
