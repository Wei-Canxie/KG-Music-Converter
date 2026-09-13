using System;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace KGMusicConverter;

/// <summary>关于页：程序信息与原始项目链接。</summary>
internal sealed class AboutControl : ToolPage
{
    private const string PageTag = "about";

    private readonly MainWindow _main;
    private ScrollViewer? _scroll;

    public AboutControl(MainWindow main)
    {
        _main = main;
        BuildUI();
    }

    private void BuildUI()
    {
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

        page.Children.Add(new TextBlock
        {
            Text = "关于",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
        });

        page.Children.Add(MakeInfoRow("程序名称", "KG Music Converter"));
        page.Children.Add(MakeInfoRow("作者", "Evilist"));
        page.Children.Add(MakeInfoRow("运行时", ".NET 8 / WinUI 3 / Windows App SDK"));

        page.Children.Add(MakeLinkRow("项目详情", "https://github.com/Wei-Canxie/KG-Music-Converter"));
        page.Children.Add(MakeLinkRow("鸣谢项目", "https://github.com/hu568/Kugo-Music-Converter-Modpacks"));

        _scroll.Content = page;
        _scroll.ViewChanged += (_, _) => _main.SetScrollOffset(PageTag, _scroll.VerticalOffset);
        Content = _scroll;
    }

    /// <summary>一条"标签 + 可点击链接"。</summary>
    private StackPanel MakeLinkRow(string label, string url)
    {
        var tm = ThemeManager.Instance;

        var row = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
        });

        // 图标 + 链接：用星列约束宽度，长 URL 才会换行而不是溢出
        //（横向 StackPanel 以无限宽量测子元素，TextWrapping 不会生效）
        var linkPanel = new Grid { ColumnSpacing = 8 };
        linkPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        linkPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = new TextBlock
        {
            Text = "🔗",
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(icon, 0);
        linkPanel.Children.Add(icon);

        var linkText = new TextBlock
        {
            Text = url.Replace("https://", ""),
            FontSize = 13,
            Foreground = tm.Accent,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        linkText.Tapped += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                AppLog.Log($"Open link failed: {ex.Message}");
            }
        };
        Grid.SetColumn(linkText, 1);
        linkPanel.Children.Add(linkText);

        row.Children.Add(linkPanel);
        return row;
    }

    private StackPanel MakeInfoRow(string label, string value)
    {
        var tm = ThemeManager.Instance;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row.Children.Add(new TextBlock
        {
            Text = label + ":",
            FontSize = 13,
            Foreground = tm.SubText,
            Width = 80,
        });
        row.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 13,
            Foreground = tm.Text,
            TextWrapping = TextWrapping.Wrap,
        });
        return row;
    }
}
