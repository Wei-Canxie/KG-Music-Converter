using System;
using System.IO;
using System.Text.Json;

namespace KGMusicConverter;

public enum ThemeMode { System, Light, Dark }
public enum BlurMode { None, Mica, Acrylic }

/// <summary>
/// 设置模型 — 持久化到 ~\AppData\Local\KGMusicConverter\settings.json
///
/// 设置页从不直接改这个实例：它改一份 <see cref="Clone"/>（草稿），
/// 用户按下"应用"时再用 <see cref="CopyFrom"/> 抄回去。
/// 这让拖动滑条变得廉价，也让"取消更改"成为可能。
/// </summary>
public class Settings
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KGMusicConverter");
    private static readonly string PathFile = Path.Combine(Dir, "settings.json");

    private const double MinOpacity = 0.3;
    private const double MaxOpacity = 1.0;
    private const double MaxBlurRadius = 1024.0;

    // ── 外观 ──
    public ThemeMode Theme { get; set; } = ThemeMode.Dark;
    public double WindowOpacity { get; set; } = 0.95;
    public double PanelOpacity { get; set; } = 1.0;
    public double BackgroundImageOpacity { get; set; } = 0.8;
    public string? BackgroundImagePath { get; set; }
    public BlurMode Blur { get; set; } = BlurMode.None;
    public double BlurRadius { get; set; } = 0;
    public byte ThemeR { get; set; } = 0xFF;
    public byte ThemeG { get; set; } = 0x66;
    public byte ThemeB { get; set; } = 0xAB;

    // ── 转换页运行选项（即时生效，但记住上次选择） ──
    public bool SkipCopy { get; set; }

    /// <summary>解密后转成这些格式（可多选；默认一个都不勾 = 只解密不转码）。</summary>
    public bool ConvertMp3 { get; set; }
    public bool ConvertWav { get; set; }
    public bool ConvertFlac { get; set; }

    /// <summary>勾上后，转换完成弹窗里"删除源文件"是默认按钮（仍会再问一次，不会静默删除）。</summary>
    public bool DeleteSourceFile { get; set; }

    public bool UseUnifiedOutput { get; set; }
    public string UnifiedOutputDir { get; set; } = "";

    /// <summary>草稿副本：设置页在它上面编辑。</summary>
    public Settings Clone() => (Settings)MemberwiseClone();

    /// <summary>把另一个实例的每个值抄进本实例（应用更改时用）。</summary>
    public void CopyFrom(Settings other)
    {
        var copy = other.Clone();

        Theme = copy.Theme;
        WindowOpacity = copy.WindowOpacity;
        PanelOpacity = copy.PanelOpacity;
        BackgroundImageOpacity = copy.BackgroundImageOpacity;
        BackgroundImagePath = copy.BackgroundImagePath;
        Blur = copy.Blur;
        BlurRadius = copy.BlurRadius;
        ThemeR = copy.ThemeR;
        ThemeG = copy.ThemeG;
        ThemeB = copy.ThemeB;
        SkipCopy = copy.SkipCopy;
        ConvertMp3 = copy.ConvertMp3;
        ConvertWav = copy.ConvertWav;
        ConvertFlac = copy.ConvertFlac;
        DeleteSourceFile = copy.DeleteSourceFile;
        UseUnifiedOutput = copy.UseUnifiedOutput;
        UnifiedOutputDir = copy.UnifiedOutputDir;
    }

    /// <summary>把越界值夹回合法区间，而不是让启动失败。</summary>
    public void Normalize()
    {
        WindowOpacity = Math.Clamp(WindowOpacity, MinOpacity, MaxOpacity);
        PanelOpacity = Math.Clamp(PanelOpacity, MinOpacity, MaxOpacity);
        BackgroundImageOpacity = Math.Clamp(BackgroundImageOpacity, 0.0, 1.0);
        BlurRadius = Math.Clamp(BlurRadius, 0.0, MaxBlurRadius);
        if (string.IsNullOrWhiteSpace(BackgroundImagePath)) BackgroundImagePath = null;
        if (string.IsNullOrWhiteSpace(UnifiedOutputDir)) UnifiedOutputDir = "";
    }

    public static Settings Load()
    {
        try
        {
            if (File.Exists(PathFile))
            {
                var json = File.ReadAllText(PathFile);
                var loaded = JsonSerializer.Deserialize<Settings>(json);
                if (loaded is not null)
                {
                    loaded.Normalize();
                    return loaded;
                }
            }

            // 兼容旧目录名（KugoMusicConverter → KGMusicConverter），迁移一次
            var legacy = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KugoMusicConverter", "settings.json");
            if (File.Exists(legacy))
            {
                var migrated = JsonSerializer.Deserialize<Settings>(File.ReadAllText(legacy));
                if (migrated is not null)
                {
                    migrated.Normalize();
                    migrated.Save();
                    AppLog.Log("Settings migrated from KugoMusicConverter directory");
                    return migrated;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Log($"Load settings failed, using defaults: {ex.Message}");
        }
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
        catch (Exception ex)
        {
            AppLog.Log($"Save settings failed: {ex.Message}");
        }
    }
}
