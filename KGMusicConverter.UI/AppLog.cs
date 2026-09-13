using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace KGMusicConverter;

/// <summary>日志级别（渲染成 V2rayN 风格的 <c>[Info]</c> / <c>[Warn]</c> / <c>[Error]</c>）。</summary>
internal enum LogLevel
{
    Info,
    Warn,
    Error,
}

/// <summary>
/// 日志行格式，对齐 V2rayN / Xray 的样式：
///
/// <code>
/// 2026/08/30 01:43:11.815593 [Info] [19404:2832169709] proxy/http: request to Method [CONNECT] ...
/// </code>
///
/// 四段：<c>yyyy/MM/dd HH:mm:ss.ffffff</c>（6 位小数）→ <c>[级别]</c> → <c>[PID:会话号]</c> → 正文。
///
/// 方括号里的数字在 V2rayN 日志里是<b>会话/连接号</b> —— 同一条连接的多行共享同一个号，
/// 便于把交织在一起的多路输出拆回各自的会话。这里用同样语义：
/// 一次转换 = 一个会话号，转换开始时换新号，于是"哪些行属于同一次转换"一眼可辨。
///
/// 会话号前面再缀上本进程 PID：会话号只区分"同一次转换"，区分不了进程 ——
/// 同时开多个实例（例如新旧版本对比）时，靠 PID 才看得出某一行是谁写的。
/// </summary>
internal static class LogFormat
{
    private static readonly object Gate = new();
    private static uint _sessionId = NewSessionId();

    /// <summary>本进程 PID（进程内恒定，取一次即可）。多实例并存时用来区分某一行是哪个进程写的。</summary>
    private static readonly int ProcessId = Environment.ProcessId;

    /// <summary>当前会话号（同一次转换的所有日志共享）。</summary>
    internal static uint SessionId
    {
        get { lock (Gate) return _sessionId; }
    }

    /// <summary>开一次新会话（每次开始转换时调用）。</summary>
    internal static void BeginSession()
    {
        lock (Gate) _sessionId = NewSessionId();
    }

    private static uint NewSessionId() => (uint)Random.Shared.NextInt64(1, uint.MaxValue);

    /// <summary>把一行正文渲染成完整的 V2rayN 风格日志行。</summary>
    internal static string Line(string message, LogLevel level) =>
        $"{DateTime.Now:yyyy/MM/dd HH:mm:ss.ffffff} [{level}] [{ProcessId}:{SessionId}] {message}";

    /// <summary>
    /// 从正文猜级别：引擎的日志用 ✗ / ⚠ / ✓ 标记结果，
    /// 不猜级别的话所有行都是 [Info]，出错时反而看不出来。
    /// </summary>
    internal static LogLevel InferLevel(string line)
    {
        if (line.Contains('✗') || line.Contains("失败") || line.Contains("错误")
            || line.Contains("异常") || line.Contains("致命"))
        {
            return LogLevel.Error;
        }

        if (line.Contains('⚠') || line.Contains("未找到") || line.Contains("缺少"))
        {
            return LogLevel.Warn;
        }

        return LogLevel.Info;
    }

    /// <summary>
    /// 条目之间的分隔：一个空行。
    ///
    /// 界面日志栏（运行时追加 + 切页后重建）和日志文件都<b>必须走这一个常量</b> ——
    /// 之前两边各写各的，结果文件里空行正常、重建界面时却用 <c>string.Join("", …)</c>
    /// 把裸行（行里不带换行）直接粘成一坨。分隔符只留一处定义就不会再漂开。
    /// </summary>
    internal static string EntrySeparator => Environment.NewLine + Environment.NewLine;

    internal static IEnumerable<string> SplitLines(string message)
    {
        foreach (var raw in message.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (raw.Trim().Length > 0) yield return raw;
        }
    }
}

/// <summary>
/// 诊断日志：<c>%TEMP%\KGMusicConverter.log</c>，行格式与界面日志一致（V2rayN 风格）。
/// 写入失败一律吞掉——日志本身绝不能把应用拖崩。
/// </summary>
internal static class AppLog
{
    private static readonly object Gate = new();

    /// <summary>不带 BOM 的 UTF-8：日志文件开头不该出现 ﻿。</summary>
    private static readonly UTF8Encoding NoBomUtf8 = new(encoderShouldEmitUTF8Identifier: false);

    internal static string LogPath { get; } =
        Path.Combine(Path.GetTempPath(), "KGMusicConverter.log");

    /// <summary>按 V2rayN 风格记一行（自动拆行、自动判级别）。</summary>
    internal static void Log(string message, LogLevel? level = null)
    {
        foreach (var line in LogFormat.SplitLines(message))
        {
            WriteFormatted(LogFormat.Line(line, level ?? LogFormat.InferLevel(line)));
        }
    }

    /// <summary>写入一行已经带好前缀的内容（界面日志走这条，避免重复加前缀）。</summary>
    internal static void WriteFormatted(string formattedLine)
    {
        try
        {
            lock (Gate)
            {
                // 条目之间空一行：日志栏与文件都更容易读
                //（"=== 阶段N ===" 这类状态行也因而被空行隔开）
                File.AppendAllText(
                    LogPath,
                    formattedLine + LogFormat.EntrySeparator,
                    NoBomUtf8);
            }
        }
        catch
        {
            // 记不下来就算了
        }
    }
}
