using System;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace KugoMusicConverter;

internal sealed class AboutControl : UserControl
{
    private MainWindow? _main;
    private TextBox? _rInput;
    private TextBox? _gInput;
    private TextBox? _bInput;
    private TextBox? _hexInput;
    private bool _isHexMode = false;

    private static readonly SolidColorBrush BackgroundBrush = new(ColorHelper.FromArgb(255, 0x1E, 0x1E, 0x2A));
    private static readonly SolidColorBrush SurfaceBrush = new(ColorHelper.FromArgb(255, 0x2A, 0x2A, 0x3A));
    private static readonly SolidColorBrush TextBrush = new(ColorHelper.FromArgb(255, 0xE0, 0xE0, 0xE8));
    private static readonly SolidColorBrush SubTextBrush = new(ColorHelper.FromArgb(255, 0x90, 0x90, 0xA0));

    public AboutControl(MainWindow main)
    {
        _main = main;
        BuildUI();
    }

    private void BuildUI()
    {
        var root = new Grid
        {
            Background = BackgroundBrush,
            Padding = new Thickness(28, 20, 28, 20),
        };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var contentPanel = new StackPanel { Spacing = 16, VerticalAlignment = VerticalAlignment.Center };

        contentPanel.Children.Add(new TextBlock
        {
            Text = "关于",
            FontSize = 28,
            FontWeight = FontWeights.Bold,
            Foreground = MainWindow.ThemeBrushRef,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var infoCard = new Border
        {
            Background = SurfaceBrush,
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            Width = 500,
        };
        var infoPanel = new StackPanel { Spacing = 12 };

        infoPanel.Children.Add(MakeInfoRow("程序名称", "Kugo Music Converter"));
        infoPanel.Children.Add(MakeInfoRow("版本", "v0.3.0 (UI 改版)"));
        infoPanel.Children.Add(MakeInfoRow("作者", "Evilist"));
        infoPanel.Children.Add(MakeInfoRow("构建时间", "2026-08-29"));
        infoPanel.Children.Add(MakeInfoRow("运行时", ".NET 8 / WinUI 3 / Windows App SDK"));

        var sourcePanel = new StackPanel { Spacing = 6 };
        sourcePanel.Children.Add(new TextBlock
        {
            Text = "原始项目",
            FontSize = 13,
            Foreground = SubTextBrush,
        });
        var linkPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        linkPanel.Children.Add(new TextBlock
        {
            Text = "🔗",
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var linkText = new TextBlock
        {
            Text = "github.com/hu568/Kugo-Music-Converter-Modpacks",
            FontSize = 13,
            Foreground = MainWindow.ThemeBrushRef,
            VerticalAlignment = VerticalAlignment.Center,
        };
        linkText.Tapped += (_, _) =>
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/hu568/Kugo-Music-Converter-Modpacks",
                UseShellExecute = true,
            };
            try { System.Diagnostics.Process.Start(psi); } catch { }
        };
        linkPanel.Children.Add(linkText);
        sourcePanel.Children.Add(linkPanel);
        infoPanel.Children.Add(sourcePanel);

        infoCard.Child = infoPanel;
        contentPanel.Children.Add(infoCard);

        // 主题色卡片
        var themeCard = new Border
        {
            Background = SurfaceBrush,
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            Width = 500,
        };
        var themePanel = new StackPanel { Spacing = 12 };
        themePanel.Children.Add(new TextBlock
        {
            Text = "主题色",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextBrush,
        });
        themePanel.Children.Add(new TextBlock
        {
            Text = "点击色块调整主题色（默认 #ff66ab）",
            FontSize = 12,
            Foreground = SubTextBrush,
        });

        // 切换按钮
        var togglePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var btnRgb = new Button
        {
            Content = "RGB",
            FontSize = 12,
            Background = !_isHexMode ? MainWindow.ThemeBrushRef : SurfaceBrush,
            Foreground = !_isHexMode ? new SolidColorBrush(Colors.White) : TextBrush,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 6, 12, 6),
        };
        btnRgb.Click += (_, _) => { _isHexMode = false; BuildUI(); };
        var btnHex = new Button
        {
            Content = "HEX",
            FontSize = 12,
            Background = _isHexMode ? MainWindow.ThemeBrushRef : SurfaceBrush,
            Foreground = _isHexMode ? new SolidColorBrush(Colors.White) : TextBrush,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 6, 12, 6),
        };
        btnHex.Click += (_, _) => { _isHexMode = true; BuildUI(); };
        togglePanel.Children.Add(btnRgb);
        togglePanel.Children.Add(btnHex);
        themePanel.Children.Add(togglePanel);

        var color = MainWindow.ThemeColorRef;

        if (_isHexMode)
        {
            // HEX 输入模式
            var hexPanel = new StackPanel { Spacing = 8 };
            hexPanel.Children.Add(new TextBlock
            {
                Text = "HEX 颜色码",
                FontSize = 12,
                Foreground = SubTextBrush,
            });
            _hexInput = new TextBox
            {
                Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}",
                FontSize = 14,
                Background = SurfaceBrush,
                Foreground = TextBrush,
                CornerRadius = new CornerRadius(8),
            };
            _hexInput.KeyDown += (_, args) =>
            {
                if (args.Key == Windows.System.VirtualKey.Enter)
                {
                    TryParseHex(_hexInput.Text);
                }
            };
            hexPanel.Children.Add(_hexInput);
            themePanel.Children.Add(hexPanel);
        }
        else
        {
            // RGB 输入模式
            var rgbPanel = new StackPanel { Spacing = 8 };
            rgbPanel.Children.Add(new TextBlock
            {
                Text = "RGB 数值（0-255）",
                FontSize = 12,
                Foreground = SubTextBrush,
            });

            var rgbInputs = new Grid { ColumnSpacing = 12 };
            rgbInputs.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rgbInputs.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rgbInputs.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _rInput = new TextBox
            {
                Text = color.R.ToString(),
                FontSize = 14,
                Background = SurfaceBrush,
                Foreground = TextBrush,
                CornerRadius = new CornerRadius(8),
                Header = "R",
            };
            _rInput.KeyDown += (_, args) =>
            {
                if (args.Key == Windows.System.VirtualKey.Enter)
                    TryParseRgb(_rInput.Text, _gInput!.Text, _bInput!.Text);
            };
            Grid.SetColumn(_rInput, 0);
            rgbInputs.Children.Add(_rInput);

            _gInput = new TextBox
            {
                Text = color.G.ToString(),
                FontSize = 14,
                Background = SurfaceBrush,
                Foreground = TextBrush,
                CornerRadius = new CornerRadius(8),
                Header = "G",
            };
            _gInput.KeyDown += (_, args) =>
            {
                if (args.Key == Windows.System.VirtualKey.Enter)
                    TryParseRgb(_rInput!.Text, _gInput.Text, _bInput!.Text);
            };
            Grid.SetColumn(_gInput, 1);
            rgbInputs.Children.Add(_gInput);

            _bInput = new TextBox
            {
                Text = color.B.ToString(),
                FontSize = 14,
                Background = SurfaceBrush,
                Foreground = TextBrush,
                CornerRadius = new CornerRadius(8),
                Header = "B",
            };
            _bInput.KeyDown += (_, args) =>
            {
                if (args.Key == Windows.System.VirtualKey.Enter)
                    TryParseRgb(_rInput!.Text, _gInput!.Text, _bInput.Text);
            };
            Grid.SetColumn(_bInput, 2);
            rgbInputs.Children.Add(_bInput);

            rgbPanel.Children.Add(rgbInputs);
            themePanel.Children.Add(rgbPanel);
        }

        // 预览
        var previewPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };
        previewPanel.Children.Add(new Ellipse
        {
            Width = 32,
            Height = 32,
            Fill = MainWindow.ThemeBrushRef,
        });
        previewPanel.Children.Add(new TextBlock
        {
            Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}",
            FontSize = 14,
            Foreground = TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        themePanel.Children.Add(previewPanel);

        themeCard.Child = themePanel;
        contentPanel.Children.Add(themeCard);

        Grid.SetRow(contentPanel, 0);
        root.Children.Add(contentPanel);

        var footer = new TextBlock
        {
            Text = "© 2026 Evilist. 基于 GPL v3 许可证发布。",
            FontSize = 11,
            Foreground = SubTextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        Grid.SetRow(footer, 1);
        root.Children.Add(footer);

        Content = root;
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
        MainWindow.SetThemeColor(r, g, b);
        // 保存到设置
        var settings = Settings.Load();
        settings.ThemeR = r;
        settings.ThemeG = g;
        settings.ThemeB = b;
        settings.Save();
        BuildUI();
    }

    private StackPanel MakeInfoRow(string label, string value)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = label + ":",
            FontSize = 13,
            Foreground = SubTextBrush,
            Width = 80,
        });
        panel.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 13,
            Foreground = TextBrush,
        });
        return panel;
    }
}
