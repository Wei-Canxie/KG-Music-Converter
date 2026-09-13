using System;
using System.Runtime.InteropServices;
using System.Text;

namespace KGMusicConverter;

/// <summary>
/// 解码外部程序（解密引擎）的输出。
///
/// 实测结论：<c>unlockKuGoWin-64.exe</c> 的 stdout 是 <b>UTF-8</b>。
/// 一个典型陷阱：.NET 的 <c>Process.StandardOutput.ReadToEnd()</c> 在中文 Windows 上
/// 会按控制台/OEM 代码页（936）解码，把 "欢迎使用" 变成 "娆㈣繋浣跨敤" ——
/// 原版 Python 用 <c>encoding='utf-8'</c> 是对的，C# 端口丢了这一条。
///
/// 这里先按严格 UTF-8 解，解不动才退回系统 ANSI（GBK）：两种情况都不会出现乱码。
/// </summary>
internal static class AnsiText
{
    private const uint CpAcp = 0;   // CP_ACP：跟随系统 ANSI 代码页

    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int MultiByteToWideChar(
        uint codePage, uint dwFlags, byte[] lpMultiByteStr, int cbMultiByte,
        [Out] char[]? lpWideCharStr, int cchWideChar);

    internal static string Decode(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return string.Empty;

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // 不是合法 UTF-8 —— 按系统 ANSI（简体中文 = GBK）再试
            return DecodeAnsi(bytes);
        }
        catch (Exception ex)
        {
            AppLog.Log($"AnsiText.Decode failed: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// 系统 ANSI 代码页兜底。注意 dwFlags 必须传 0：
    /// <c>MB_ERR_INVALID_CHARS (0x8)</c> 只对 CP_UTF8 有效，带着它调系统代码页会直接失败返回 0。
    /// </summary>
    private static string DecodeAnsi(byte[] bytes)
    {
        try
        {
            int chars = MultiByteToWideChar(CpAcp, 0, bytes, bytes.Length, null, 0);
            if (chars <= 0) return string.Empty;

            var buffer = new char[chars];
            int written = MultiByteToWideChar(CpAcp, 0, bytes, bytes.Length, buffer, chars);
            return written <= 0 ? string.Empty : new string(buffer, 0, written);
        }
        catch (Exception ex)
        {
            AppLog.Log($"AnsiText.DecodeAnsi failed: {ex.Message}");
            return string.Empty;
        }
    }
}
