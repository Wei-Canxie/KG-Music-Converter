using System;
using System.IO;
using System.Text.Json;

namespace KugoMusicConverter;

public enum ThemeMode { System, Light, Dark }
public enum BlurMode { None, Mica, Acrylic }

/// <summary>
/// 设置模型 — 持久化到 ~\AppData\Local\KugoMusicConverter\settings.json
/// </summary>
public class Settings
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KugoMusicConverter");
    private static readonly string PathFile = Path.Combine(Dir, "settings.json");

    public ThemeMode Theme { get; set; } = ThemeMode.Dark;
    public double WindowOpacity { get; set; } = 1.0;
    public double PanelOpacity { get; set; } = 1.0;
    public double BackgroundImageOpacity { get; set; } = 1.0;
    public string? BackgroundImagePath { get; set; }
    public BlurMode Blur { get; set; } = BlurMode.None;
    public double BlurRadius { get; set; } = 0;
    public byte ThemeR { get; set; } = 0xFF;
    public byte ThemeG { get; set; } = 0x66;
    public byte ThemeB { get; set; } = 0xAB;

    public static Settings Load()
    {
        try
        {
            if (File.Exists(PathFile))
            {
                var json = File.ReadAllText(PathFile);
                return JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
            }
        }
        catch { }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(PathFile, json);
        }
        catch { }
    }
}
