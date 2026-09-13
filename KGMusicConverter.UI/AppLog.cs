using System;
using System.IO;

namespace KGMusicConverter;

/// <summary>
/// 追加式诊断日志：%TEMP%\KGMusicConverter.log。
/// 写入失败一律吞掉——日志本身绝不能把应用拖崩。
/// </summary>
internal static class AppLog
{
    private static readonly object Gate = new();

    internal static string LogPath { get; } =
        Path.Combine(Path.GetTempPath(), "KGMusicConverter.log");

    internal static void Log(string message)
    {
        try
        {
            lock (Gate)
            {
                File.AppendAllText(
                    LogPath,
                    $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 记不下来就算了
        }
    }
}
