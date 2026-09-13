using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace KGMusicConverter;

/// <summary>
/// 应用专属工作区，位于 <c>%LOCALAPPDATA%\KGMusicConverter\</c>：
///
/// <code>
/// KGMusicConverter\
/// ├── settings.json
/// ├── inbox\          ← 收件箱（热文件夹：丢进去的文件自动入队）
/// └── workspace\      ← 工作目录：引擎 + 待处理文件 + 中间产物
///     ├── unlockKuGoWin-64.exe / kgg-dec.exe / kgm.mask
///     ├── kgm-vpr-out\ffmpeg.exe
///     └── kgm-vpr-out\   ← 解密/转码产物，最后由引擎搬回源目录或统一输出目录
/// </code>
///
/// 这样做的原因：解密引擎会从<b>当前工作目录</b>查找 <c>unlockKuGoWin-64.exe</c> /
/// <c>kgg-dec.exe</c> / <c>kgm.mask</c>，并把产物写到该目录下的 <c>kgm-vpr-out\</c>。
/// 把工作目录固定在应用自己的地盘，用户就不必往音乐文件夹里放引擎，音乐文件夹也不会
/// 被中间产物污染 —— 成品由引擎阶段6 搬回源目录（或统一输出目录）。
/// </summary>
internal static class Workspace
{
    private static readonly string AppDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KGMusicConverter");

    /// <summary>工作目录：引擎在这里运行。</summary>
    internal static string Root { get; } = Path.Combine(AppDir, "workspace");

    /// <summary>收件箱：丢进这里的文件会被自动加入队列。</summary>
    internal static string Inbox { get; } = Path.Combine(AppDir, "inbox");

    /// <summary>产物目录（引擎的 OutputDir）。</summary>
    internal static string OutputDir { get; } = Path.Combine(Root, "kgm-vpr-out");

    /// <summary>
    /// 成品目录：来自收件箱的文件，成品放这里 —— 绝不能放回收件箱，
    /// 那会被热文件夹当成新文件重新入队，形成自喂循环。
    /// </summary>
    internal static string Output { get; } = Path.Combine(AppDir, "output");

    /// <summary>引擎文件名（放在工作目录里）。</summary>
    private static readonly string[] EngineFiles =
    {
        "unlockKuGoWin-64.exe",
        "unlockKuGoWin-32.exe",
        "kgg-dec.exe",
        "kgm.mask",
    };

    /// <summary>ffmpeg 放在产物目录里（引擎按此路径查找）。</summary>
    private const string FfmpegName = "ffmpeg.exe";

    /// <summary>清理临时文件时必须保住的东西（它们是工具本身，不是临时产物）。</summary>
    private static readonly HashSet<string> KeepFileNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "unlockKuGoWin-64.exe",
            "unlockKuGoWin-32.exe",
            "kgg-dec.exe",
            "kgm.mask",
            FfmpegName,
        };

    /// <summary>
    /// 会话标记：启动时写入本进程 PID，正常退出时删除。
    /// 下次启动若发现标记还在、而那个 PID 已经不存在，就说明上次是被强杀的。
    /// </summary>
    internal static string SessionFlag { get; } = Path.Combine(AppDir, "session.lock");

    internal static void EnsureCreated()
    {
        try
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Inbox);
            Directory.CreateDirectory(OutputDir);
            Directory.CreateDirectory(Output);
        }
        catch (Exception ex)
        {
            AppLog.Log($"Workspace.EnsureCreated failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 把程序目录下的解密引擎复制进工作区（只补缺失的，不覆盖已有的）。
    ///
    /// 发布包把引擎放在 exe 旁边，首次运行时自动"播种"到工作区 ——
    /// 用户既不用手动摆放，也不用把引擎丢进音乐文件夹。
    /// </summary>
    internal static void SeedEngines()
    {
        try
        {
            var appDir = AppContext.BaseDirectory;

            foreach (var name in EngineFiles)
            {
                CopyIfMissing(Path.Combine(appDir, name), Path.Combine(Root, name));
            }

            CopyIfMissing(
                Path.Combine(appDir, "kgm-vpr-out", FfmpegName),
                Path.Combine(OutputDir, FfmpegName));
        }
        catch (Exception ex)
        {
            AppLog.Log($"Workspace.SeedEngines failed: {ex.Message}");
        }
    }

    private static void CopyIfMissing(string source, string destination)
    {
        try
        {
            if (!File.Exists(source)) return;
            if (File.Exists(destination))
            {
                // 同大小视为已就绪，避免每次启动重复拷贝几十 MB
                if (new FileInfo(source).Length == new FileInfo(destination).Length) return;
            }

            File.Copy(source, destination, overwrite: true);
            AppLog.Log($"Engine seeded: {Path.GetFileName(destination)}");
        }
        catch (Exception ex)
        {
            AppLog.Log($"Seed {Path.GetFileName(destination)} failed: {ex.Message}");
        }
    }

    /// <summary>工作区里是否已有可用引擎（64 位或 32 位）。</summary>
    internal static bool HasUnlockTool =>
        File.Exists(Path.Combine(Root, "unlockKuGoWin-64.exe")) ||
        File.Exists(Path.Combine(Root, "unlockKuGoWin-32.exe"));

    internal static bool HasKggDec => File.Exists(Path.Combine(Root, "kgg-dec.exe"));

    /// <summary>缺哪些引擎（用于给出可操作的错误提示）。</summary>
    internal static IReadOnlyList<string> MissingEngines()
    {
        var missing = new List<string>();

        if (!HasUnlockTool) missing.Add("unlockKuGoWin-64.exe");
        if (!HasKggDec) missing.Add("kgg-dec.exe");
        if (!File.Exists(Path.Combine(Root, "kgm.mask"))) missing.Add("kgm.mask");
        if (!File.Exists(Path.Combine(OutputDir, FfmpegName))) missing.Add(FfmpegName);

        return missing;
    }

    /// <summary>
    /// 上次会话是否被强制结束（进程已不在，标记文件却还在）。
    /// 还活着的实例（多开）不算强杀 —— 绝不能去动别人正在用的工作区。
    /// </summary>
    internal static bool WasPreviousSessionKilled()
    {
        try
        {
            if (!File.Exists(SessionFlag)) return false;

            var text = File.ReadAllText(SessionFlag).Trim();
            if (int.TryParse(text, out var pid) && IsProcessAlive(pid)) return false;

            return true;
        }
        catch (Exception ex)
        {
            AppLog.Log($"WasPreviousSessionKilled failed: {ex.Message}");
            return false;
        }
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch
        {
            return false;   // 进程不存在
        }
    }

    internal static void MarkSessionStarted()
    {
        try
        {
            EnsureCreated();
            File.WriteAllText(SessionFlag, Environment.ProcessId.ToString());
        }
        catch (Exception ex)
        {
            AppLog.Log($"MarkSessionStarted failed: {ex.Message}");
        }
    }

    /// <summary>正常退出：标记清除，下次启动就不会误判为强杀。</summary>
    internal static void MarkSessionEnded()
    {
        try
        {
            if (File.Exists(SessionFlag)) File.Delete(SessionFlag);
        }
        catch (Exception ex)
        {
            AppLog.Log($"MarkSessionEnded failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 清理工作区里的临时文件：复制进来的源文件、解密/转码中间产物。
    /// 引擎与 ffmpeg 保留；成品目录（output）不动 —— 那是用户要的东西。
    /// </summary>
    /// <returns>(删掉的数量, 删不掉的数量)</returns>
    internal static (int removed, int failed) CleanTempFiles()
    {
        int removed = 0, failed = 0;

        try
        {
            EnsureCreated();

            foreach (var file in Directory.GetFiles(Root))
            {
                if (KeepFileNames.Contains(Path.GetFileName(file))) continue;
                if (TryDelete(file)) removed++; else failed++;
            }

            foreach (var file in Directory.GetFiles(OutputDir))
            {
                if (KeepFileNames.Contains(Path.GetFileName(file))) continue;
                if (TryDelete(file)) removed++; else failed++;
            }
        }
        catch (Exception ex)
        {
            AppLog.Log($"CleanTempFiles failed: {ex.Message}");
        }

        return (removed, failed);
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch
        {
            return false;   // 正被占用：留着，下次启动再清
        }
    }

    /// <summary>收件箱里当前可处理的音频文件（启动时补捞一次）。</summary>
    internal static IReadOnlyList<string> ScanInbox()
    {
        try
        {
            if (!Directory.Exists(Inbox)) return Array.Empty<string>();

            return Directory.GetFiles(Inbox)
                .Where(InboxWatcher.IsCandidateFile)
                .ToList();
        }
        catch (Exception ex)
        {
            AppLog.Log($"Workspace.ScanInbox failed: {ex.Message}");
            return Array.Empty<string>();
        }
    }
}
