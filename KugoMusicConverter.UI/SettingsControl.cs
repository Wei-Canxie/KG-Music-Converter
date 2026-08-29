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

    private static readonly SolidColorBrush TextBrush = new(ColorHelper.FromArgb(255, 0xE0, 0xE0, 0xE8));
    private static readonly SolidColorBrush SubTextBrush = new(ColorHelper.FromArgb(255, 0x90, 0x90, 0xA0));
    private static readonly SolidColorBrush SurfaceBrush = new(ColorHelper.FromArgb(255, 0x2A, 0x2A, 0x3A));

    public SettingsControl(MainWindow main)
    {
        _main = main;
        _settings = Settings.Load();
        BuildUI();
    }

    private void BuildUI()
    {
        var root = new Grid
        {
            Background = GetCurrentBackgroundBrush(),
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
            Foreground = MainWindow.ThemeBrushRef,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        // 外观卡片
        var appearanceCard = new Border
        {
            Background = SurfaceBrush,
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            Width = 520,
        };
        var appearancePanel = new StackPanel { Spacing = 16 };

        appearancePanel.Children.Add(new TextBlock
        {
            Text = "外观",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextBrush,
        });

        // 主题模式
        var themePanel = new StackPanel { Spacing = 8 };
        themePanel.Children.Add(new TextBlock
        {
            Text = "主题",
            FontSize = 13,
            Foreground = SubTextBrush,
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

        // 窗口不透明度
        var opacityPanel = new StackPanel { Spacing = 8 };
        opacityPanel.Children.Add(new TextBlock
        {
            Text = $"窗口不透明度: {_settings.WindowOpacity:P0}",
            FontSize = 13,
            Foreground = SubTextBrush,
        });
        var opacitySlider = new Slider
        {
            Minimum = 0.3,
            Maximum = 1.0,
            Value = _settings.WindowOpacity,
            Width = 300,
            Foreground = MainWindow.ThemeBrushRef,
        };
        opacitySlider.ValueChanged += (_, args) =>
        {
            _settings.WindowOpacity = args.NewValue;
            _main!.SetWindowOpacity(args.NewValue);
        };
        opacityPanel.Children.Add(opacitySlider);
        appearancePanel.Children.Add(opacityPanel);

        // 背景图
        var bgPanel = new StackPanel { Spacing = 8 };
        bgPanel.Children.Add(new TextBlock
        {
            Text = "窗口背景图",
            FontSize = 13,
            Foreground = SubTextBrush,
        });
        var bgButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var btnBrowseBg = new Button
        {
            Content = "选择图片…",
            FontSize = 13,
            Background = SurfaceBrush,
            Foreground = TextBrush,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 8, 16, 8),
        };
        btnBrowseBg.Click += OnBrowseBackground;
        bgButtons.Children.Add(btnBrowseBg);
        var btnClearBg = new Button
        {
            Content = "清除",
            FontSize = 13,
            Background = SurfaceBrush,
            Foreground = TextBrush,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 8, 16, 8),
        };
        btnClearBg.Click += (_, _) =>
        {
            _settings.BackgroundImagePath = null;
            ApplyBackground();
        };
        bgButtons.Children.Add(btnClearBg);
        bgPanel.Children.Add(bgButtons);
        var bgPathText = new TextBlock
        {
            Text = string.IsNullOrEmpty(_settings.BackgroundImagePath) ? "未设置" : _settings.BackgroundImagePath,
            FontSize = 11,
            Foreground = SubTextBrush,
            TextWrapping = TextWrapping.Wrap,
        };
        bgPanel.Children.Add(bgPathText);
        appearancePanel.Children.Add(bgPanel);

        // 模糊模式
        var blurPanel = new StackPanel { Spacing = 8 };
        blurPanel.Children.Add(new TextBlock
        {
            Text = "背景模糊效果",
            FontSize = 13,
            Foreground = SubTextBrush,
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
            Foreground = SubTextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        Grid.SetRow(footer, 1);
        root.Children.Add(footer);

        Content = root;
    }

    private Button MakeThemeButton(string text, ThemeMode mode)
    {
        var btn = new Button
        {
            Content = text,
            FontSize = 13,
            Background = _settings.Theme == mode ? MainWindow.ThemeBrushRef : SurfaceBrush,
            Foreground = _settings.Theme == mode ? new SolidColorBrush(Colors.White) : TextBrush,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 8, 16, 8),
        };
        btn.Click += (_, _) =>
        {
            _settings.Theme = mode;
            ApplyTheme();
            BuildUI(); // refresh
        };
        return btn;
    }

    private Button MakeBlurButton(string text, BlurMode mode)
    {
        var btn = new Button
        {
            Content = text,
            FontSize = 13,
            Background = _settings.Blur == mode ? MainWindow.ThemeBrushRef : SurfaceBrush,
            Foreground = _settings.Blur == mode ? new SolidColorBrush(Colors.White) : TextBrush,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 8, 16, 8),
        };
        btn.Click += (_, _) =>
        {
            _settings.Blur = mode;
            ApplyBlur();
            BuildUI(); // refresh
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
            ApplyBackground();
            BuildUI();
        }
    }

    private void ApplyTheme()
    {
        _settings.Save();
        // 通知所有控件更新
        _main?.ApplyAllSettings(_settings);
    }

    private void ApplyBlur()
    {
        _settings.Save();
        _main?.ApplyAllSettings(_settings);
    }

    private void ApplyBackground()
    {
        _settings.Save();
        _main?.ApplyAllSettings(_settings);
    }

    private SolidColorBrush GetCurrentBackgroundBrush()
    {
        bool isDark = _settings.Theme != ThemeMode.Light;
        if (isDark)
            return new SolidColorBrush(ColorHelper.FromArgb(255, 0x1E, 0x1E, 0x2A));
        else
            return new SolidColorBrush(ColorHelper.FromArgb(255, 0xF5, 0xF5, 0xF5));
    }
}
