using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KGMusicConverter;

/// <summary>
/// 转换引擎 — 支持文件条目状态追踪、源目录/统一输出目录。
/// </summary>
public sealed class ConversionEngine
{
    public string WorkingDir { get; }
    public string OutputDir => Path.Combine(WorkingDir, "kgm-vpr-out");
    public string UnlockTool => Path.Combine(WorkingDir, "unlockKuGoWin-64.exe");
    public string UnlockTool32 => Path.Combine(WorkingDir, "unlockKuGoWin-32.exe");
    public string KggDec => Path.Combine(WorkingDir, "kgg-dec.exe");
    public string FFmpeg => Path.Combine(OutputDir, "ffmpeg.exe");

    public event Action<string>? Log;
    public event Action<FileEntry>? FileStatusChanged;
    public event Action<int, string>? ReportProgress;
    public event Action<bool>? Completed;

    private readonly List<FileEntry> _files = new();
    private readonly Dictionary<string, string> _baseNameToSourceDir = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<FileEntry> Files => _files;

    public ConversionEngine(string workingDir)
    {
        WorkingDir = workingDir;
    }

    private void OnLog(string msg) => Log?.Invoke(msg);
    private void OnProgress(int pct, string step) => ReportProgress?.Invoke(pct, step);
    private void OnFileChanged(FileEntry f) => FileStatusChanged?.Invoke(f);

    public void SetFiles(IEnumerable<FileEntry> files)
    {
        _files.Clear();
        _baseNameToSourceDir.Clear();
        foreach (var f in files)
        {
            _files.Add(f);
            _baseNameToSourceDir[f.BaseName] = f.SourceDirectory;
        }
    }

    public async Task RunAsync(bool skipConvert, bool useUnifiedOutput, string unifiedOutputDir, CancellationToken ct)
    {
        try
        {
            // ── 阶段1: 复制到工作目录 ──
            OnProgress(5, "复制文件");
            await Task.Run(() => PhaseCopyToWorking(ct), ct);

            // ── 阶段2: 重命名 .flac → .kgm ──
            OnProgress(15, "重命名 .flac → .kgm");
            await Task.Run(() => PhaseRenameFlacToKgm(), ct);

            // ── 阶段3 & 4: 并行解密 ──
            OnProgress(30, "并行解密 KGM / KGG");
            var unlockTask = Task.Run(() => PhaseRunUnlockTool(), ct);
            var kggTask = Task.Run(() => PhaseProcessKgg(), ct);
            await Task.WhenAll(unlockTask, kggTask);

            // ── 阶段5: 批量转 MP3（可选） ──
            if (skipConvert)
            {
                OnLog("跳过转 MP3");
            }
            else
            {
                OnProgress(70, "批量转 MP3");
                await Task.Run(() => PhaseConvertFlacToMp3(ct), ct);
            }

            // ── 阶段6: 移动到目标目录 ──
            OnProgress(90, "移动到目标目录");
            await Task.Run(() => PhaseMoveToTargets(useUnifiedOutput, unifiedOutputDir), ct);

            // ── 阶段7: 清理 ──
            OnProgress(97, "清理临时文件");
            PhaseCleanup();

            OnProgress(100, "完成");

            bool allOk = _files.All(f => f.Status == FileStatus.Completed || f.Status == FileStatus.NeedsManualKGG);
            OnLog(allOk ? "✅ 全部流程执行完毕！" : "⚠️ 部分文件未完成");
            Completed?.Invoke(allOk);
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

    // ── 阶段1: 复制 ──
    private void PhaseCopyToWorking(CancellationToken ct)
    {
        OnLog("=== 阶段1: 复制文件到工作目录 ===");
        foreach (var f in _files)
        {
            ct.ThrowIfCancellationRequested();
            f.Status = FileStatus.Processing;
            OnFileChanged(f);

            var dst = Path.Combine(WorkingDir, f.FileName);
            try
            {
                if (File.Exists(dst))
                {
                    if (new FileInfo(f.SourcePath).Length != new FileInfo(dst).Length)
                        File.Copy(f.SourcePath, dst, true);
                }
                else
                {
                    File.Copy(f.SourcePath, dst);
                }
                OnLog($"  → 复制: {f.FileName}");
            }
            catch (Exception e)
            {
                f.Status = FileStatus.Failed;
                f.ErrorMessage = e.Message;
                OnFileChanged(f);
                OnLog($"  ✗ 复制失败 {f.FileName}: {e.Message}");
            }
        }
        OnLog("✓ 复制完成");
    }

    // ── 阶段2: 重命名 ──
    private void PhaseRenameFlacToKgm()
    {
        OnLog("=== 阶段2: 重命名 .flac → .kgm ===");
        var flacFiles = _files.Where(f => f.Extension.Equals(".flac", StringComparison.OrdinalIgnoreCase)).ToList();
        if (flacFiles.Count == 0) { OnLog("无 .flac 文件，跳过"); return; }

        foreach (var f in flacFiles)
        {
            var target = f.BaseName + ".kgm";
            var src = Path.Combine(WorkingDir, f.FileName);
            var dst = Path.Combine(WorkingDir, target);
            OnLog($"处理: {f.FileName}");
            if (File.Exists(dst))
            {
                OnLog($"  → 跳过: {target} 已存在");
                // 更新文件条目中的文件名为 .kgm
                f.FileName = target;
                f.Extension = ".kgm";
            }
            else
            {
                try
                {
                    File.Move(src, dst);
                    f.FileName = target;
                    f.Extension = ".kgm";
                    f.BaseName = f.BaseName; // 不变
                    OnLog($"  → 重命名: {f.FileName}");
                }
                catch (Exception e)
                {
                    f.Status = FileStatus.Failed;
                    f.ErrorMessage = e.Message;
                    OnFileChanged(f);
                    OnLog($"  ✗ 重命名失败: {e.Message}");
                }
            }
        }
        OnLog("✓ 重命名完成");
    }

    // ── 阶段3: unlockKuGoWin ──
    private void PhaseRunUnlockTool()
    {
        OnLog("=== 阶段3: unlockKuGoWin 解密 ===");
        var unlockExe = File.Exists(UnlockTool) ? UnlockTool :
                       (File.Exists(UnlockTool32) ? UnlockTool32 : null);

        if (unlockExe == null) { OnLog("⚠ unlockKuGoWin 未找到"); return; }

        var kgmFiles = _files.Where(f => new[] { ".kgm", ".kgma", ".vpr" }.Contains(f.Extension)).ToList();
        if (kgmFiles.Count == 0) { OnLog("无 KGM/KGMA/VPR 文件，跳过"); return; }

        OnLog($"正在启动 {Path.GetFileName(unlockExe)} ...");
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = unlockExe,
                    WorkingDirectory = WorkingDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    CreateNoWindow = true,
                }
            };
            proc.Start();
            proc.StandardInput.WriteLine();
            string stdout = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(120_000);
            OnLog($"{Path.GetFileName(unlockExe)} 完成 (exit={proc.ExitCode})");
            foreach (var line in stdout.Split('\n'))
                if (line.Trim().Length > 0) OnLog($"  {line.Trim()}");

            foreach (var f in kgmFiles)
                OnFileChanged(f);
        }
        catch (Exception e)
        {
            OnLog($"✗ unlockKuGoWin 错误: {e.Message}");
        }
    }

    // ── 阶段4: kgg-dec ──
    private void PhaseProcessKgg()
    {
        OnLog("=== 阶段4: KGG 解密 → .ogg ===");
        if (!File.Exists(KggDec)) { OnLog("⚠ kgg-dec.exe 未找到"); return; }

        Directory.CreateDirectory(OutputDir);
        var kggFiles = _files.Where(f => f.IsKgg).ToList();
        if (kggFiles.Count == 0) { OnLog("无 KGG 文件，跳过"); return; }

        foreach (var f in kggFiles)
        {
            OnLog($"处理: {f.FileName}");
            var src = Path.Combine(WorkingDir, f.FileName);
            try
            {
                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = KggDec,
                        Arguments = $"\"{src}\"",
                        WorkingDirectory = WorkingDir,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                    }
                };
                proc.Start();
                proc.WaitForExit(120_000);

                var tempOgg = Path.Combine(WorkingDir, $"{f.BaseName}_kgg-dec.ogg");
                var tempOggOut = Path.Combine(OutputDir, $"{f.BaseName}_kgg-dec.ogg");
                var outputOgg = Path.Combine(OutputDir, $"{f.BaseName}.ogg");

                if (File.Exists(tempOggOut))
                    tempOgg = tempOggOut;
                else if (!File.Exists(tempOgg))
                {
                    f.Status = FileStatus.NeedsManualKGG;
                    f.ErrorMessage = "缺少解密密钥，请先用酷狗客户端播放一次该文件";
                    OnFileChanged(f);
                    OnLog($"  ✗ {f.FileName}: 缺少解密密钥");
                    continue;
                }

                OnLog("  ✓ kgg-dec 解密成功");
                if (File.Exists(outputOgg)) File.Delete(outputOgg);
                File.Move(tempOgg, outputOgg);
                f.OutputPath = outputOgg;
                OnFileChanged(f);
            }
            catch (Exception e)
            {
                f.Status = FileStatus.Failed;
                f.ErrorMessage = e.Message;
                OnFileChanged(f);
                OnLog($"  ✗ {f.FileName}: {e.Message}");
            }
        }
        OnLog("✓ KGG 处理完成");
    }

    // ── 阶段5: 批量转 MP3 ──
    private void PhaseConvertFlacToMp3(CancellationToken ct)
    {
        OnLog("=== 阶段5: 批量转 MP3 ===");
        if (!File.Exists(FFmpeg)) { OnLog("⚠ 未找到 ffmpeg.exe，跳过"); return; }

        var audioFiles = Directory.GetFiles(OutputDir)
            .Where(f => {
                var ext = Path.GetExtension(f).ToLower();
                return (ext == ".flac" || ext == ".ogg");
            })
            .ToList();

        if (audioFiles.Count == 0) { OnLog("kgm-vpr-out/ 中没有 FLAC/OGG 文件，跳过"); return; }

        int total = audioFiles.Count, done = 0;
        foreach (var audio in audioFiles)
        {
            ct.ThrowIfCancellationRequested();
            var baseName = Path.GetFileNameWithoutExtension(audio);
            var dst = Path.Combine(OutputDir, $"{baseName}.mp3");

            OnLog($"[{++done}/{total}] 转换: {Path.GetFileName(audio)}");
            try
            {
                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = FFmpeg,
                        Arguments = $"-i \"{audio}\" -q:a 0 -map_metadata 0 -map 0:a -map 0:v? -c:v copy -id3v2_version 3 -y \"{dst}\"",
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
                }
                else
                {
                    OnLog($"  ✗ 转换失败");
                }
            }
            catch (Exception e)
            {
                OnLog($"  ✗ 错误: {e.Message}");
            }
        }
        OnLog("✓ 批量转换完成");
    }

    // ── 阶段6: 移动到目标目录 ──
    private void PhaseMoveToTargets(bool useUnifiedOutput, string unifiedOutputDir)
    {
        OnLog("=== 阶段6: 移动到目标目录 ===");
        var outputFiles = Directory.GetFiles(OutputDir).Where(f => !f.EndsWith(".exe") && !f.EndsWith(".bat")).ToList();

        foreach (var f in _files)
        {
            if (f.Status == FileStatus.Failed || f.Status == FileStatus.NeedsManualKGG) continue;

            // 找到对应的输出文件
            var match = outputFiles.FirstOrDefault(of =>
                Path.GetFileNameWithoutExtension(of).Equals(f.BaseName, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                // 查找可能的 MP3 输出
                match = outputFiles.FirstOrDefault(of =>
                    Path.GetFileNameWithoutExtension(of).Equals(f.BaseName, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetExtension(of).Equals(".mp3", StringComparison.OrdinalIgnoreCase));
            }

            if (match == null)
            {
                f.Status = FileStatus.Failed;
                f.ErrorMessage = "未找到输出文件";
                OnFileChanged(f);
                OnLog($"  ✗ {f.BaseName}: 未找到输出");
                continue;
            }

            string targetDir;
            if (useUnifiedOutput && Directory.Exists(unifiedOutputDir))
                targetDir = unifiedOutputDir;
            else
                targetDir = f.SourceDirectory;

            try
            {
                var target = Path.Combine(targetDir, Path.GetFileName(match));
                if (File.Exists(target)) File.Delete(target);
                File.Move(match, target);
                f.OutputPath = target;
                f.Status = FileStatus.Completed;
                OnFileChanged(f);
                OnLog($"  → {Path.GetFileName(target)} → {targetDir}");
            }
            catch (Exception e)
            {
                f.Status = FileStatus.Failed;
                f.ErrorMessage = e.Message;
                OnFileChanged(f);
                OnLog($"  ✗ 移动失败 {f.BaseName}: {e.Message}");
            }
        }
        OnLog("✓ 移动完成");
    }

    // ── 阶段7: 清理 ──
    private void PhaseCleanup()
    {
        OnLog("=== 阶段7: 清理 ===");
        int deleted = 0;
        foreach (var f in Directory.GetFiles(WorkingDir))
        {
            var name = Path.GetFileName(f);
            if (name.EndsWith("_kgg-dec.ogg", StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(f); deleted++; } catch { }
            }
        }
        OnLog($"✓ 清理完成，删除 {deleted} 个临时文件");
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
