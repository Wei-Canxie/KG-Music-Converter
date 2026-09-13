using System;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace KGMusicConverter;

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

        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var panel = new StackPanel { Spacing = 12, Padding = new Thickness(24, 16, 24, 16), MaxWidth = 560 };
        panel.Children.Add(Header("关于"));

        panel.Children.Add(MakeInfoRow("程序名称", "KG Music Converter"));
        panel.Children.Add(MakeInfoRow("作者", "Evilist"));
        panel.Children.Add(MakeInfoRow("运行时", ".NET 8 / WinUI 3 / Windows App SDK"));

        panel.Children.Add(new TextBlock { Text = "原始项目", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0) });
        var linkPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        linkPanel.Children.Add(new TextBlock { Text = "🔗", FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
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
        panel.Children.Add(linkPanel);

        scroll.Content = panel;
        Content = scroll;
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

    private static TextBlock Header(string text) => new()
    {
        Text = text,
        FontSize = 20,
        FontWeight = FontWeights.SemiBold
    };
}
