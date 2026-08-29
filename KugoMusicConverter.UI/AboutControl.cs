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

        var slidersPanel = new StackPanel { Spacing = 8 };
        var color = MainWindow.ThemeColorRef;
        slidersPanel.Children.Add(MakeColorSlider("R", color.R, v => UpdateThemeColor((byte)v, color.G, color.B)));
        slidersPanel.Children.Add(MakeColorSlider("G", color.G, v => UpdateThemeColor(color.R, (byte)v, color.B)));
        slidersPanel.Children.Add(MakeColorSlider("B", color.B, v => UpdateThemeColor(color.R, color.G, (byte)v)));
        themePanel.Children.Add(slidersPanel);

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

    private UIElement MakeColorSlider(string label, byte current, Action<double> onChanged)
    {
        var panel = new Grid { ColumnSpacing = 12 };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelText = new TextBlock
        {
            Text = label,
            FontSize = 13,
            Foreground = TextBrush,
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
            Foreground = MainWindow.ThemeBrushRef,
        };
        slider.ValueChanged += (_, args) => onChanged(args.NewValue);
        Grid.SetColumn(slider, 1);
        panel.Children.Add(slider);

        var valueText = new TextBlock
        {
            Text = current.ToString(),
            FontSize = 13,
            Foreground = SubTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 30,
        };
        Grid.SetColumn(valueText, 2);
        panel.Children.Add(valueText);

        return panel;
    }

    private void UpdateThemeColor(byte r, byte g, byte b)
    {
        MainWindow.SetThemeColor(r, g, b);
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
