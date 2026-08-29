using System;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace KugoMusicConverter;

internal sealed class AboutControl : UserControl
{
    private MainWindow? _main;

    public AboutControl(MainWindow main)
    {
        _main = main;
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

        contentPanel.Children.Add(new TextBlock
        {
            Text = "关于",
            FontSize = 28,
            FontWeight = FontWeights.Bold,
            Foreground = tm.Accent,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var infoCard = new Border
        {
            Background = tm.CardBackground,
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
            Foreground = tm.SubText,
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
            Foreground = tm.Accent,
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

        Grid.SetRow(contentPanel, 0);
        root.Children.Add(contentPanel);

        var footer = new TextBlock
        {
            Text = "© 2026 Evilist. 基于 GPL v3 许可证发布。",
            FontSize = 11,
            Foreground = tm.SubText,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        Grid.SetRow(footer, 1);
        root.Children.Add(footer);

        Content = root;
    }

    private StackPanel MakeInfoRow(string label, string value)
    {
        var tm = ThemeManager.Instance;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = label + ":",
            FontSize = 13,
            Foreground = tm.SubText,
            Width = 80,
        });
        panel.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 13,
            Foreground = tm.Text,
        });
        return panel;
    }
}
