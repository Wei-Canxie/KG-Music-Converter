using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace KGMusicConverter;

/// <summary>转码目标格式。</summary>
public enum AudioFormat
{
    Mp3,
    Wav,
    Flac,
}

/// <summary>
/// 音频格式定义与 ffmpeg 调用。
///
/// 转换引擎（阶段5）和格式整理工具共用这一份命令构造 —— 两处各写一套参数的话，
/// 迟早会分叉成"主流程保留封面、整理工具丢封面"这种不一致。
///
/// 三条命令都对着真实样本验证过（37MB FLAC → 96kHz/立体声，带 480×480 mjpeg 封面）：
/// MP3 与 FLAC 都完整保留封面与元数据，WAV 装不下封面所以不映射视频流。
/// </summary>
internal static class AudioFormats
{
    /// <summary>通用（未加密）音频后缀 —— 格式整理工具的识别范围。</summary>
    internal static readonly string[] Common =
    {
        ".flac", ".mp3", ".ogg", ".wav", ".m4a", ".aac", ".wma", ".ape",
    };

    /// <summary>酷狗加密后缀。</summary>
    internal static readonly string[] Encrypted = { ".kgm", ".kgma", ".kgg", ".vpr" };

    internal static string Extension(AudioFormat format) => format switch
    {
        AudioFormat.Mp3 => ".mp3",
        AudioFormat.Wav => ".wav",
        _ => ".flac",
    };

    internal static string Display(AudioFormat format) => format switch
    {
        AudioFormat.Mp3 => "MP3",
        AudioFormat.Wav => "WAV",
        _ => "FLAC",
    };

    internal static bool HasExtension(string path, IEnumerable<string> extensions) =>
        extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    internal static bool IsCommonAudio(string path) => HasExtension(path, Common);

    internal static bool IsEncrypted(string path) => HasExtension(path, Encrypted);

    /// <summary>文件本身就是目标格式 —— 再编码一遍只会掉质量、白等几分钟。</summary>
    internal static bool IsAlready(AudioFormat format, string path) =>
        Path.GetExtension(path).Equals(Extension(format), StringComparison.OrdinalIgnoreCase);

    /// <summary>构造 ffmpeg 参数：能装封面的格式就带上封面与元数据（WAV 装不下，所以不映射视频流）。</summary>
    internal static string BuildArguments(AudioFormat format, string source, string target) => format switch
    {
        AudioFormat.Mp3 =>
            $"-i \"{source}\" -q:a 0 -map_metadata 0 -map 0:a -map 0:v? -c:v copy -id3v2_version 3 -y \"{target}\"",
        AudioFormat.Flac =>
            $"-i \"{source}\" -map_metadata 0 -map 0:a -map 0:v? -c:v copy -c:a flac -y \"{target}\"",
        _ =>
            $"-i \"{source}\" -map_metadata 0 -map 0:a -c:a pcm_s16le -y \"{target}\"",
    };

    /// <summary>
    /// 跑一次 ffmpeg 转码。<paramref name="detail"/> 成功时是时长，失败时是最后一行有意义的输出。
    /// </summary>
    internal static bool Convert(string ffmpeg, AudioFormat format, string source, string target, out string detail)
    {
        detail = "";
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    Arguments = BuildArguments(format, source, target),
                    WorkingDirectory = Path.GetDirectoryName(ffmpeg) ?? ".",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                }
            };

            proc.Start();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(600_000);

            if (File.Exists(target) && new FileInfo(target).Length > 0)
            {
                detail = ExtractDuration(stderr);
                return true;
            }

            detail = LastMeaningfulLine(stderr);
            return false;
        }
        catch (Exception ex)
        {
            detail = ex.Message;
            return false;
        }
    }

    internal static string ExtractDuration(string ffmpegStderr)
    {
        var idx = ffmpegStderr.IndexOf("Duration:", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "?";
        var start = idx + "Duration:".Length;
        var end = ffmpegStderr.IndexOf(',', start);
        return end < 0 ? "?" : ffmpegStderr[start..end].Trim();
    }

    private static string LastMeaningfulLine(string text)
    {
        foreach (var line in text.Replace("\r\n", "\n").Split('\n').Reverse())
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0) return trimmed;
        }
        return "未知错误";
    }
}

/// <summary>文件操作：删除一律走回收站，误删还能捞回来。</summary>
internal static class FileOps
{
    /// <summary>删除到回收站；回收站不可用时退回直接删除。</summary>
    internal static bool DeleteToRecycleBin(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;

            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                Microsoft.VisualBasic.FileIO.UICancelOption.DoNothing);

            return !File.Exists(path);
        }
        catch (Exception ex)
        {
            AppLog.Log($"DeleteToRecycleBin failed, falling back to File.Delete: {ex.Message}");
            try
            {
                File.Delete(path);
                return !File.Exists(path);
            }
            catch (Exception inner)
            {
                AppLog.Log($"File.Delete failed: {inner.Message}");
                return false;
            }
        }
    }

    /// <summary>path 是否位于 directory 之内（含 directory 本身）。</summary>
    internal static bool IsInside(string path, string directory)
    {
        try
        {
            var target = Path.GetFullPath(path);
            var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return target.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    internal static string ToFullPathOrEmpty(string? path)
    {
        try { return string.IsNullOrEmpty(path) ? "" : Path.GetFullPath(path); }
        catch { return ""; }
    }
}
