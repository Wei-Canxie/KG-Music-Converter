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
        var root = new Grid
        {
            Background = tm.Background,
            Padding = new Thickness(28, 20, 28, 20),
        };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var contentPanel = new StackPanel { Spacing = 16, VerticalAlignment = VerticalAlignment.Center };

        // 标题
        contentPanel.Children.Add(new TextBlock
        {
            Text = "设置",
            FontSize = 28,
            FontWeight = FontWeights.Bold,
            Foreground = tm.Accent,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        // 外观卡片
        var appearanceCard = new Border
        {
            Background = tm.CardBackground,
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            Width = 560,
        };
        var appearancePanel = new StackPanel { Spacing = 16 };

        appearancePanel.Children.Add(new TextBlock
        {
            Text = "外观",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = tm.Text,
        });

        // ── 主题模式 ──
        var themePanel = new StackPanel { Spacing = 8 };
        themePanel.Children.Add(new TextBlock
        {
            Text = "主题",
            FontSize = 13,
            Foreground = tm.SubText,
        });
        var themeButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var btnSystem = MakeThemeButton("跟随系统", ThemeMode.System);
        var btnLight = MakeThemeButton("亮色", ThemeMode.Light);
        var btnDark = MakeThemeButton("暗色", ThemeMode.Dark);
        themeButtons.Children.Add(btnSystem);
        themeButtons.Children.Add(btnLight);
        themeButtons.Children.Add(btnDark);
        themePanel.Children.Add(themeButtons);
        appearancePanel.Children.Add(themePanel);

        // ── 主题色 ──
        var colorPanel = new StackPanel { Spacing = 8 };
        colorPanel.Children.Add(new TextBlock
        {
            Text = "主题色",
            FontSize = 13,
            Foreground = tm.SubText,
        });

        // 滑块
        var color = tm.AccentColor;
        var slidersPanel = new StackPanel { Spacing = 6 };
        slidersPanel.Children.Add(MakeColorSlider("R", color.R, v => UpdateThemeColor((byte)v, color.G, color.B)));
        slidersPanel.Children.Add(MakeColorSlider("G", color.G, v => UpdateThemeColor(color.R, (byte)v, color.B)));
        slidersPanel.Children.Add(MakeColorSlider("B", color.B, v => UpdateThemeColor(color.R, color.G, (byte)v)));
        colorPanel.Children.Add(slidersPanel);

        // RGB/HEX 切换
        var togglePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var btnRgb = new Button
        {
            Content = "RGB",
            FontSize = 12,
            Background = !_isHexMode ? tm.Accent : tm.Surface,
            Foreground = !_isHexMode ? new SolidColorBrush(Colors.White) : tm.Text,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 6, 12, 6),
        };
        btnRgb.Click += (_, _) => { _isHexMode = false; BuildUI(); };
        var btnHex = new Button
        {
            Content = "HEX",
            FontSize = 12,
            Background = _isHexMode ? tm.Accent : tm.Surface,
            Foreground = _isHexMode ? new SolidColorBrush(Colors.White) : tm.Text,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 6, 12, 6),
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
                FontSize = 14,
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
            var rgbInputs = new Grid { ColumnSpacing = 12 };
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
        var previewPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };
        previewPanel.Children.Add(new Ellipse { Width = 28, Height = 28, Fill = tm.Accent });
        previewPanel.Children.Add(new TextBlock
        {
            Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}",
            FontSize = 13,
            Foreground = tm.Text,
            VerticalAlignment = VerticalAlignment.Center,
        });
        colorPanel.Children.Add(previewPanel);
        appearancePanel.Children.Add(colorPanel);

        // ── 窗口不透明度 ──
        var opacityPanel = new StackPanel { Spacing = 8 };
        int opacityPercent = (int)Math.Round(_settings.WindowOpacity * 100);
        var opacityLabel = new TextBlock
        {
            Text = $"窗口不透明度: {opacityPercent}%",
            FontSize = 13,
            Foreground = tm.SubText,
        };
        opacityPanel.Children.Add(opacityLabel);
        var opacitySlider = new Slider
        {
            Minimum = 5,
            Maximum = 100,
            Value = opacityPercent,
            Width = 300,
            Foreground = tm.Accent,
            SmallChange = 1,
            LargeChange = 10,
            StepFrequency = 1,
        };
        opacitySlider.ValueChanged += (_, args) =>
        {
            int pct = (int)Math.Round(args.NewValue);
            _settings.WindowOpacity = pct / 100.0;

            // 标题栏透明度：<=90% 时 +10% 以造成差异效果
            if (pct <= 90)
            {
                var root = _main?.Content as FrameworkElement;
                if (root != null) root.Opacity = Math.Min(1.0, (pct + 10) / 100.0);
            }
            else
            {
                var root = _main?.Content as FrameworkElement;
                if (root != null) root.Opacity = 1.0;
            }

            opacityLabel.Text = $"窗口不透明度: {pct}%";
        };
        opacityPanel.Children.Add(opacitySlider);
        appearancePanel.Children.Add(opacityPanel);

        // ── 背景图 ──
        var bgPanel = new StackPanel { Spacing = 8 };
        bgPanel.Children.Add(new TextBlock
        {
            Text = "窗口背景图",
            FontSize = 13,
            Foreground = tm.SubText,
        });
        var bgButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var btnBrowseBg = new Button
        {
            Content = "选择图片…",
            FontSize = 13,
            Background = tm.Surface,
            Foreground = tm.Text,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 8, 16, 8),
        };
        btnBrowseBg.Click += OnBrowseBackground;
        bgButtons.Children.Add(btnBrowseBg);
        var btnClearBg = new Button
        {
            Content = "清除",
            FontSize = 13,
            Background = tm.Surface,
            Foreground = tm.Text,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 8, 16, 8),
        };
        btnClearBg.Click += (_, _) =>
        {
            _settings.BackgroundImagePath = null;
            _settings.Save();
            ApplyBackground();
            BuildUI();
        };
        bgButtons.Children.Add(btnClearBg);
        bgPanel.Children.Add(bgButtons);
        var bgPathText = new TextBlock
        {
            Text = string.IsNullOrEmpty(_settings.BackgroundImagePath) ? "未设置" : _settings.BackgroundImagePath,
            FontSize = 11,
            Foreground = tm.SubText,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 400,
        };
        bgPanel.Children.Add(bgPathText);
        appearancePanel.Children.Add(bgPanel);

        // ── 模糊模式 ──
        var blurPanel = new StackPanel { Spacing = 8 };
        blurPanel.Children.Add(new TextBlock
        {
            Text = "背景模糊效果",
            FontSize = 13,
            Foreground = tm.SubText,
        });
        var blurButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var btnBlurNone = MakeBlurButton("默认", BlurMode.None);
        var btnBlurMica = MakeBlurButton("云母", BlurMode.Mica);
        var btnBlurAcrylic = MakeBlurButton("亚克力", BlurMode.Acrylic);
        blurButtons.Children.Add(btnBlurNone);
        blurButtons.Children.Add(btnBlurMica);
        blurButtons.Children.Add(btnBlurAcrylic);
        blurPanel.Children.Add(blurButtons);
        appearancePanel.Children.Add(blurPanel);

        appearanceCard.Child = appearancePanel;
        contentPanel.Children.Add(appearanceCard);

        Grid.SetRow(contentPanel, 0);
        root.Children.Add(contentPanel);

        // 底部
        var footer = new TextBlock
        {
            Text = "设置会自动保存到本地",
            FontSize = 11,
            Foreground = tm.SubText,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        Grid.SetRow(footer, 1);
        root.Children.Add(footer);

        Content = root;
    }

    private TextBox MakeRgbInput(byte value, string header, Action<byte> onChanged)
    {
        var tm = ThemeManager.Instance;
        var tb = new TextBox
        {
            Text = value.ToString(),
            FontSize = 14,
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
        var panel = new Grid { ColumnSpacing = 12 };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelText = new TextBlock
        {
            Text = label,
            FontSize = 13,
            Foreground = tm.Text,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 20,
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
            FontSize = 13,
            Foreground = tm.SubText,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 30,
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
            FontSize = 13,
            Background = _settings.Theme == mode ? tm.Accent : tm.Surface,
            Foreground = _settings.Theme == mode ? new SolidColorBrush(Colors.White) : tm.Text,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 8, 16, 8),
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
            FontSize = 13,
            Background = _settings.Blur == mode ? tm.Accent : tm.Surface,
            Foreground = _settings.Blur == mode ? new SolidColorBrush(Colors.White) : tm.Text,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 8, 16, 8),
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
            ApplyBackground();
            BuildUI();
        }
    }

    private void TryParseRgb(string rStr, string gStr, string bStr)
    {
        if (byte.TryParse(rStr, out var r) &&
            byte.TryParse(gStr, out var g) &&
            byte.TryParse(bStr, out var b))
        {
            UpdateThemeColor(r, g, b);
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
                _main.SystemBackdrop = _settings.Blur switch
                {
                    BlurMode.Mica => new MicaBackdrop { Kind = isDark ? MicaKind.Base : MicaKind.BaseAlt },
                    BlurMode.Acrylic => new DesktopAcrylicBackdrop(),
                    _ => null
                };
            }
        }
        catch
        {
            if (_main != null) _main.SystemBackdrop = null;
        }
    }

    private void ApplyBackground()
    {
        var root = _main?.Content as Grid;
        if (root == null) return;

        if (!string.IsNullOrEmpty(_settings.BackgroundImagePath) && System.IO.File.Exists(_settings.BackgroundImagePath))
        {
            try
            {
                var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                using var stream = System.IO.File.OpenRead(_settings.BackgroundImagePath);
                bitmap.SetSource(stream.AsRandomAccessStream());
                root.Background = new ImageBrush
                {
                    ImageSource = bitmap,
                    Stretch = Stretch.UniformToFill,
                };
            }
            catch
            {
                root.Background = ThemeManager.Instance.Background;
            }
        }
        else
        {
            root.Background = ThemeManager.Instance.Background;
        }
    }
}
