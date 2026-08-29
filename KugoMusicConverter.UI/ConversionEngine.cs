using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KugoMusicConverter;

/// <summary>
/// 转换引擎 — 将 main.py 的 6 步流程移植为异步可取消的 C# 实现。
/// 通过 Log 回调把实时日志喂给 UI，通过 ReportProgress 更新进度条。
/// </summary>
public sealed class ConversionEngine
{
    public string ProjectDir { get; }
    public string InputDir => Path.Combine(ProjectDir, "input");
    public string OutputDir => Path.Combine(ProjectDir, "kgm-vpr-out");
    public string UnlockTool => Path.Combine(ProjectDir, "unlockKuGoWin-64.exe");
    public string UnlockTool32 => Path.Combine(ProjectDir, "unlockKuGoWin-32.exe");
    public string KggDec => Path.Combine(ProjectDir, "kgg-dec.exe");
    public string FFmpeg => Path.Combine(OutputDir, "ffmpeg.exe");

    public event Action<string>? Log;
    public event Action<int, string>? ReportProgress; // (percent, stepName)
    public event Action<bool>? Completed; // true=all ok

    private readonly List<string> _copiedFiles = new();

    public ConversionEngine(string projectDir)
    {
        ProjectDir = projectDir;
    }

    private void OnLog(string msg) => Log?.Invoke(msg);
    private void OnProgress(int pct, string step) => ReportProgress?.Invoke(pct, step);

    public async Task RunAsync(bool skipCopy, bool skipConvert, CancellationToken ct)
    {
        var results = new Dictionary<string, object>();
        try
        {
            // ── 步骤1: 复制 ──
            int copied = 0;
            if (!skipCopy)
            {
                OnProgress(5, "复制文件");
                copied = StepCopyFromInput();
                results["copy"] = copied;
            }
            else
            {
                results["copy"] = "已跳过";
            }

            // ── 步骤2: 重命名 .flac → .kgm ──
            OnProgress(15, "重命名 .flac → .kgm");
            int renamed = StepRenameFlacToKgm();
            results["rename"] = renamed;

            // ── 步骤3 & 4: 并行解密 ──
            OnProgress(30, "并行解密 KGM / KGG");
            var unlockTask = Task.Run(() => StepRunUnlockTool(), ct);
            var kggTask = Task.Run(() => StepProcessKgg(), ct);
            await Task.WhenAll(unlockTask, kggTask);
            results["unlock"] = unlockTask.Result;
            results["kgg"] = kggTask.Result;

            // ── 步骤5: 批量转 MP3 ──
            if (skipConvert)
            {
                results["convert"] = "已跳过";
            }
            else
            {
                OnProgress(80, "批量转 MP3");
                bool conv = await Task.Run(() => StepConvertFlacToMp3(ct), ct);
                results["convert"] = conv;
            }

            // ── 步骤6: 清理 ──
            OnProgress(95, "清理临时文件");
            StepCleanup();
            results["cleanup"] = true;

            OnProgress(100, "完成");
            OnLog("✅ 全部流程执行完毕！");
            Completed?.Invoke(true);
        }
        catch (OperationCanceledException)
        {
            OnLog("⛔ 已取消");
            Completed?.Invoke(false);
        }
        catch (Exception ex)
        {
            OnLog($"✗ 致命错误: {ex.Message}");
            Completed?.Invoke(false);
        }
    }

    // ── 步骤1 ──
    private int StepCopyFromInput()
    {
        _copiedFiles.Clear();
        OnLog("=== 步骤1: 从 input/ 复制文件 ===");

        if (!Directory.Exists(InputDir))
        {
            OnLog($"⚠ input/ 目录不存在: {InputDir}");
            return 0;
        }

        var supported = new HashSet<string> { ".kgg", ".kgm", ".kgma", ".vpr", ".flac" };
        var audioFiles = Directory.GetFiles(InputDir)
            .Where(f => supported.Contains(Path.GetExtension(f).ToLower()))
            .Select(Path.GetFileName)
            .ToList();

        if (audioFiles.Count == 0)
        {
            OnLog("⚠ input/ 中没有支持的音频文件");
            return 0;
        }

        OnLog($"找到 {audioFiles.Count} 个音频文件：");
        int copied = 0, skipped = 0;

        foreach (var f in audioFiles)
        {
            var src = Path.Combine(InputDir, f);
            var dst = Path.Combine(ProjectDir, f);
            if (File.Exists(dst))
            {
                if (new FileInfo(src).Length != new FileInfo(dst).Length)
                {
                    File.Copy(src, dst, true);
                    OnLog($"  → 覆盖: {f}");
                    copied++;
                    _copiedFiles.Add(f);
                }
                else
                {
                    OnLog($"  → 跳过（已存在）: {f}");
                    skipped++;
                }
            }
            else
            {
                File.Copy(src, dst);
                OnLog($"  → 复制: {f}");
                copied++;
                _copiedFiles.Add(f);
            }
        }

        OnLog($"✓ 复制完成: 新增 {copied} 个，跳过 {skipped} 个");
        return copied;
    }

    // ── 步骤2 ──
    private int StepRenameFlacToKgm()
    {
        OnLog("=== 步骤2: 处理加密的 .flac 文件 ===");
        var flacFiles = Directory.GetFiles(ProjectDir)
            .Where(f => Path.GetExtension(f).Equals(".flac", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .ToList();

        if (flacFiles.Count == 0)
        {
            OnLog("没有找到 .flac 文件，跳过");
            return 0;
        }

        int renamed = 0, skipped = 0, errors = 0;
        foreach (var flac in flacFiles)
        {
            var baseName = Path.GetFileNameWithoutExtension(flac);
            var target = baseName + ".kgm";
            var src = Path.Combine(ProjectDir, flac);
            var dst = Path.Combine(ProjectDir, target);

            OnLog($"处理: {flac}");
            if (File.Exists(dst))
            {
                OnLog($"  → 跳过: {target} 已存在");
                skipped++;
            }
            else
            {
                try
                {
                    File.Move(src, dst);
                    OnLog($"  → 重命名: {flac} → {target}");
                    renamed++;
                }
                catch (Exception e)
                {
                    OnLog($"  ✗ 重命名失败: {e.Message}");
                    errors++;
                }
            }
        }

        OnLog($"✓ 重命名完成: 成功 {renamed}，跳过 {skipped}，失败 {errors}");
        return renamed;
    }

    // ── 步骤3 ──
    private bool StepRunUnlockTool()
    {
        OnLog("=== 步骤3: unlockKuGoWin 解密 ===");
        var unlockExe = File.Exists(UnlockTool) ? UnlockTool :
                       (File.Exists(UnlockTool32) ? UnlockTool32 : null);

        if (unlockExe == null)
        {
            OnLog("⚠ unlockKuGoWin 未找到（已尝试 64 位和 32 位版本）");
            return false;
        }

        var supported = new HashSet<string> { ".kgm", ".kgma", ".vpr" };
        bool hasFiles = Directory.GetFiles(ProjectDir)
            .Any(f => supported.Contains(Path.GetExtension(f).ToLower()));

        if (!hasFiles)
        {
            OnLog("没有找到 .kgm/.kgma/.vpr 文件，跳过");
            return true;
        }

        OnLog($"正在启动 {Path.GetFileName(unlockExe)} ...");
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = unlockExe,
                    WorkingDirectory = ProjectDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    CreateNoWindow = true,
                }
            };
            proc.Start();
            // 工具结束时提示"按Enter退出"，自动发送回车
            proc.StandardInput.WriteLine();
            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(120_000);

            OnLog($"{Path.GetFileName(unlockExe)} 已执行完成 (exit={proc.ExitCode})");
            if (!string.IsNullOrWhiteSpace(stdout))
                foreach (var line in stdout.Split('\n'))
                    if (line.Trim().Length > 0) OnLog($"  {line.Trim()}");
            return true;
        }
        catch (Exception e)
        {
            OnLog($"✗ unlockKuGoWin 错误: {e.Message}");
            return false;
        }
    }

    // ── 步骤4 ──
    private bool StepProcessKgg()
    {
        OnLog("=== 步骤4: KGG 解密 → .ogg ===");
        if (!File.Exists(KggDec))
        {
            OnLog("⚠ kgg-dec.exe 未找到");
            return false;
        }

        Directory.CreateDirectory(OutputDir);
        var kggFiles = Directory.GetFiles(ProjectDir)
            .Where(f => Path.GetExtension(f).Equals(".kgg", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .ToList();

        if (kggFiles.Count == 0)
        {
            OnLog("没有找到 .kgg 文件，跳过");
            return true;
        }

        int success = 0, failed = 0;
        foreach (var kgg in kggFiles)
        {
            var baseName = Path.GetFileNameWithoutExtension(kgg);
            var src = Path.Combine(ProjectDir, kgg);
            OnLog($"处理: {kgg}");

            try
            {
                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = KggDec,
                        Arguments = $"\"{src}\"",
                        WorkingDirectory = ProjectDir,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                    }
                };
                proc.Start();
                proc.WaitForExit(120_000);

                if (proc.ExitCode != 0)
                {
                    OnLog($"  ✗ kgg-dec 返回错误码: {proc.ExitCode}");
                    failed++;
                    continue;
                }
            }
            catch (Exception e)
            {
                OnLog($"  ✗ kgg-dec 运行错误: {e.Message}");
                failed++;
                continue;
            }

            var tempOgg = Path.Combine(ProjectDir, $"{baseName}_kgg-dec.ogg");
            var tempOggOut = Path.Combine(OutputDir, $"{baseName}_kgg-dec.ogg");
            var outputOgg = Path.Combine(OutputDir, $"{baseName}.ogg");

            if (File.Exists(tempOggOut))
                tempOgg = tempOggOut;
            else if (!File.Exists(tempOgg))
            {
                OnLog("  ✗ kgg-dec 未生成输出文件（可能缺少解密密钥，请先用酷狗客户端播放一次该文件）");
                failed++;
                continue;
            }

            OnLog("  ✓ kgg-dec 解密成功");
            try
            {
                if (File.Exists(outputOgg)) File.Delete(outputOgg);
                File.Move(tempOgg, outputOgg);
                OnLog($"  → 输出: kgm-vpr-out/{baseName}.ogg");
                success++;
            }
            catch (Exception e)
            {
                OnLog($"  ✗ 移动输出文件失败: {e.Message}");
                SafeDelete(tempOgg);
                failed++;
            }
        }

        OnLog($"✓ KGG 处理完成: 成功 {success} 个，失败 {failed} 个");
        return failed == 0;
    }

    // ── 步骤5 ──
    private bool StepConvertFlacToMp3(CancellationToken ct)
    {
        OnLog("=== 步骤5: 批量转 MP3 ===");
        if (!File.Exists(FFmpeg))
        {
            OnLog("⚠ 未找到 ffmpeg.exe，跳过");
            return true;
        }

        var audioFiles = Directory.GetFiles(OutputDir)
            .Where(f => {
                var ext = Path.GetExtension(f).ToLower();
                return (ext == ".flac" || ext == ".ogg") && !ext.Equals(".mp3");
            })
            .Select(Path.GetFileName)
            .ToList();

        if (audioFiles.Count == 0)
        {
            OnLog("kgm-vpr-out/ 中没有 FLAC/OGG 文件，跳过");
            return true;
        }

        int total = audioFiles.Count, success = 0, failed = 0;
        for (int i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();
            var audio = audioFiles[i];
            var baseName = Path.GetFileNameWithoutExtension(audio);
            var src = Path.Combine(OutputDir, audio);
            var dst = Path.Combine(OutputDir, $"{baseName}.mp3");

            OnLog($"[{i + 1}/{total}] 正在转换: {audio}");
            try
            {
                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = FFmpeg,
                        Arguments = $"-i \"{src}\" -q:a 0 -map_metadata 0 -map 0:a -map 0:v? -c:v copy -id3v2_version 3 -y \"{dst}\"",
                        WorkingDirectory = OutputDir,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                    }
                };
                proc.Start();
                string stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit(600_000);

                if (File.Exists(dst))
                {
                    var duration = ExtractDuration(stderr);
                    OnLog($"  ✓ {baseName}.mp3 (时长 {duration})");
                    success++;
                }
                else
                {
                    OnLog($"  ✗ 转换失败");
                    failed++;
                }
            }
            catch (Exception e)
            {
                OnLog($"  ✗ 错误: {e.Message}");
                failed++;
            }
        }

        OnLog($"✓ 批量转换完成: 成功 {success} 个，失败 {failed} 个");
        return failed == 0;
    }

    // ── 步骤6 ──
    private void StepCleanup()
    {
        OnLog("=== 步骤6: 清理临时文件 ===");
        int deleted = 0;
        foreach (var f in _copiedFiles)
        {
            var path = Path.Combine(ProjectDir, f);
            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                    OnLog($"  → 删除: {f}");
                    deleted++;
                }
                catch (Exception e)
                {
                    OnLog($"  ⚠ 删除 {f} 失败: {e.Message}");
                }
            }
        }

        // 清理 kgg-dec 遗留的临时文件
        foreach (var f in Directory.GetFiles(ProjectDir))
        {
            if (Path.GetFileName(f).EndsWith("_kgg-dec.ogg", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.Delete(f);
                    OnLog($"  → 删除临时文件: {Path.GetFileName(f)}");
                    deleted++;
                }
                catch { }
            }
        }

        _copiedFiles.Clear();
        OnLog($"✓ 清理完成，共删除 {deleted} 个文件");
    }

    // ── 工具方法 ──
    private static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static string ExtractDuration(string ffmpegStderr)
    {
        var idx = ffmpegStderr.IndexOf("Duration:", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "?";
        var start = idx + "Duration:".Length;
        var end = ffmpegStderr.IndexOf(',', start);
        if (end < 0) return "?";
        return ffmpegStderr[start..end].Trim();
    }
}