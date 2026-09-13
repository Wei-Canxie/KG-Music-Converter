using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI;

namespace KGMusicConverter;

public class ThemeManager : INotifyPropertyChanged
{
    private static readonly Lazy<ThemeManager> _instance = new(() => new ThemeManager());
    public static ThemeManager Instance => _instance.Value;

    private ThemeMode _mode = ThemeMode.Dark;
    private Windows.UI.Color _accentColor = ColorHelper.FromArgb(255, 0xFF, 0x66, 0xAB);
    private bool _isDark = true;
    private double _panelOpacity = 1.0;

    public event PropertyChangedEventHandler? PropertyChanged;

    private ThemeManager() { }

    public ThemeMode Mode
    {
        get => _mode;
        set { _mode = value; OnPropertyChanged(); Refresh(); }
    }

    public Windows.UI.Color AccentColor
    {
        get => _accentColor;
        set { _accentColor = value; OnPropertyChanged(); Refresh(); }
    }

    public bool IsDark
    {
        get => _isDark;
        private set { _isDark = value; OnPropertyChanged(); }
    }

    public double PanelOpacity
    {
        get => _panelOpacity;
        set { _panelOpacity = value; OnPropertyChanged(); }
    }

    public Microsoft.UI.Xaml.Media.SolidColorBrush Background { get; private set; } = null!;
    public Microsoft.UI.Xaml.Media.SolidColorBrush Surface { get; private set; } = null!;
    public Microsoft.UI.Xaml.Media.SolidColorBrush CardBackground { get; private set; } = null!;
    public Microsoft.UI.Xaml.Media.SolidColorBrush Text { get; private set; } = null!;
    public Microsoft.UI.Xaml.Media.SolidColorBrush SubText { get; private set; } = null!;
    public Microsoft.UI.Xaml.Media.SolidColorBrush Accent { get; private set; } = null!;
    public Microsoft.UI.Xaml.Media.SolidColorBrush AccentDark { get; private set; } = null!;
    public Microsoft.UI.Xaml.Media.SolidColorBrush Border { get; private set; } = null!;

    static ThemeManager()
    {
        var mgr = Instance;
        mgr.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush();
        mgr.Surface = new Microsoft.UI.Xaml.Media.SolidColorBrush();
        mgr.CardBackground = new Microsoft.UI.Xaml.Media.SolidColorBrush();
        mgr.Text = new Microsoft.UI.Xaml.Media.SolidColorBrush();
        mgr.SubText = new Microsoft.UI.Xaml.Media.SolidColorBrush();
        mgr.Accent = new Microsoft.UI.Xaml.Media.SolidColorBrush();
        mgr.AccentDark = new Microsoft.UI.Xaml.Media.SolidColorBrush();
        mgr.Border = new Microsoft.UI.Xaml.Media.SolidColorBrush();
        mgr.Refresh();
    }

    public void Refresh()
    {
        IsDark = _mode switch
        {
            ThemeMode.Light => false,
            ThemeMode.Dark => true,
            ThemeMode.System => IsSystemDark(),
            _ => true
        };

        if (IsDark)
        {
            Background.Color = ColorHelper.FromArgb(255, 0x1E, 0x1E, 0x2A);
            Surface.Color = ColorHelper.FromArgb(255, 0x2A, 0x2A, 0x3A);
            CardBackground.Color = ColorHelper.FromArgb(255, 0x2A, 0x2A, 0x3A);
            Text.Color = ColorHelper.FromArgb(255, 0xE0, 0xE0, 0xE8);
            SubText.Color = ColorHelper.FromArgb(255, 0x90, 0x90, 0xA0);
            Border.Color = ColorHelper.FromArgb(255, 0x3A, 0x3A, 0x4A);
        }
        else
        {
            Background.Color = ColorHelper.FromArgb(255, 0xF5, 0xF5, 0xF5);
            Surface.Color = ColorHelper.FromArgb(255, 0xFF, 0xFF, 0xFF);
            CardBackground.Color = ColorHelper.FromArgb(255, 0xFF, 0xFF, 0xFF);
            Text.Color = ColorHelper.FromArgb(255, 0x1A, 0x1A, 0x2A);
            SubText.Color = ColorHelper.FromArgb(255, 0x60, 0x60, 0x70);
            Border.Color = ColorHelper.FromArgb(255, 0xDD, 0xDD, 0xE8);
        }

        Accent.Color = _accentColor;
        AccentDark.Color = Darken(_accentColor, 0.8f);
    }

    private static Windows.UI.Color Darken(Windows.UI.Color c, float factor)
    {
        return ColorHelper.FromArgb(255,
            (byte)(c.R * factor),
            (byte)(c.G * factor),
            (byte)(c.B * factor));
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 0;
        }
        catch { return true; }
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
