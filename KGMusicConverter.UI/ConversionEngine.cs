using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KGMusicConverter;

/// <summary>
/// 一次转换的运行选项。
///
/// 转码格式是<b>多选</b>：三种都不勾 = 只解密（输出解密后的 flac/ogg），
/// 勾几种就产出几种；解析出来的原始文件始终保留。
/// </summary>
public sealed class ConversionOptions
{
    public bool ConvertMp3 { get; set; }
    public bool ConvertWav { get; set; }
    public bool ConvertFlac { get; set; }

    /// <summary>勾选后，完成弹窗里"删除源文件"为默认按钮；删除动作始终由弹窗确认，不静默删除。</summary>
    public bool DeleteSourceFile { get; set; }

    public bool UseUnifiedOutput { get; set; }
    public string UnifiedOutputDir { get; set; } = "";

    /// <summary>是否勾了任何转码格式。</summary>
    public bool AnyConvert => ConvertMp3 || ConvertWav || ConvertFlac;

    /// <summary>勾选的格式，顺序固定 MP3 → WAV → FLAC。</summary>
    public IReadOnlyList<AudioFormat> Targets
    {
        get
        {
            var targets = new List<AudioFormat>();
            if (ConvertMp3) targets.Add(AudioFormat.Mp3);
            if (ConvertWav) targets.Add(AudioFormat.Wav);
            if (ConvertFlac) targets.Add(AudioFormat.Flac);
            return targets;
        }
    }
}

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

    /// <summary>来自收件箱的文件，成品改落到这里（避免回投被监视的文件夹）。</summary>
    private readonly string? _fallbackOutputDir;

    /// <summary>收件箱路径，用来判断某个源目录是不是收件箱。</summary>
    private readonly string? _inboxDir;

    public ConversionEngine(string workingDir, string? fallbackOutputDir = null, string? inboxDir = null)
    {
        WorkingDir = workingDir;
        _fallbackOutputDir = fallbackOutputDir;
        _inboxDir = inboxDir;
    }

    /// <summary>源目录是否就是收件箱（收件箱文件的"源目录"就是收件箱本身）。</summary>
    private bool IsInboxDirectory(string? directory)
    {
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(_inboxDir)) return false;

        try
        {
            var a = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
            var b = Path.GetFullPath(_inboxDir).TrimEnd(Path.DirectorySeparatorChar);
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
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

    public async Task RunAsync(ConversionOptions options, CancellationToken ct)
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

            // ── 阶段5: 转码（可选，格式可多选） ──
            if (!options.AnyConvert)
            {
                OnLog("未勾选转码格式：只解密，输出解密后的原始音频");
            }
            else
            {
                OnProgress(70, "转码");
                await Task.Run(() => PhaseConvertFormats(options, ct), ct);
            }

            // ── 阶段6: 移动到目标目录 ──
            OnProgress(90, "移动到目标目录");
            await Task.Run(() => PhaseMoveToTargets(options.UseUnifiedOutput, options.UnifiedOutputDir), ct);

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
            OnLog($"✗ 致命错误: {ex}");
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

            // 拿原始字节再按 GBK 解码：这工具是中文控制台程序，
            // 直接按 UTF-8 读会得到"娆㈣繋浣跨敤"这种乱码
            byte[] outBytes = ReadAllBytes(proc.StandardOutput.BaseStream);
            byte[] errBytes = ReadAllBytes(proc.StandardError.BaseStream);
            proc.WaitForExit(120_000);

            string stdout = AnsiText.Decode(outBytes);
            string stderr = AnsiText.Decode(errBytes);

            OnLog($"{Path.GetFileName(unlockExe)} 完成 (exit={proc.ExitCode})");
            foreach (var line in stdout.Split('\n'))
                if (line.Trim().Length > 0) OnLog($"  {line.Trim()}");

            if (proc.ExitCode != 0 && stderr.Trim().Length > 0)
            {
                foreach (var line in stderr.Split('\n'))
                    if (line.Trim().Length > 0) OnLog($"  ! {line.Trim()}");
            }

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

    // ── 阶段5: 转码（MP3 / WAV / FLAC，可多选） ──
    private void PhaseConvertFormats(ConversionOptions options, CancellationToken ct)
    {
        OnLog("=== 阶段5: 转码 ===");
        if (!File.Exists(FFmpeg)) { OnLog("⚠ 未找到 ffmpeg.exe，跳过转码"); return; }

        var targets = options.Targets;
        if (targets.Count == 0) { OnLog("未勾选转码格式，跳过"); return; }

        OnLog($"目标格式: {string.Join(" / ", targets.Select(AudioFormats.Display))}");

        var audioFiles = Directory.GetFiles(OutputDir)
            .Where(f =>
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                return ext == ".flac" || ext == ".ogg";
            })
            .ToList();

        if (audioFiles.Count == 0) { OnLog("kgm-vpr-out/ 中没有 FLAC/OGG 文件，跳过"); return; }

        int total = audioFiles.Count * targets.Count, done = 0;
        foreach (var audio in audioFiles)
        {
            ct.ThrowIfCancellationRequested();
            var baseName = Path.GetFileNameWithoutExtension(audio);

            foreach (var format in targets)
            {
                ct.ThrowIfCancellationRequested();
                done++;

                // 源文件本来就是目标格式：再编码一遍只会掉质量
                if (AudioFormats.IsAlready(format, audio))
                {
                    OnLog($"[{done}/{total}] 跳过 {Path.GetFileName(audio)}：本身已是 {AudioFormats.Display(format)}");
                    continue;
                }

                var dst = Path.Combine(OutputDir, baseName + AudioFormats.Extension(format));
                OnLog($"[{done}/{total}] 转 {AudioFormats.Display(format)}: {Path.GetFileName(audio)}");

                if (AudioFormats.Convert(FFmpeg, format, audio, dst, out var detail))
                    OnLog($"  ✓ {baseName}{AudioFormats.Extension(format)}（时长 {detail}）");
                else
                    OnLog($"  ✗ 转换失败: {detail}");
            }
        }
        OnLog("✓ 转码完成");
    }

    /// <summary>
    /// 删除已成功转换的源文件（只有在完成弹窗里确认后才会走到这里）。
    ///
    /// 两道防线：
    /// 1. 工作区 / 产物目录里的文件一律不碰 —— 那是应用自己的临时产物；
    /// 2. 源文件路径若与本批产物相同则跳过 —— 加密 .flac 解密后同名同后缀会覆盖源文件，
    ///    那之后"源文件"其实就是成品，删了等于把成品删掉。
    /// </summary>
    public int DeleteSources(IEnumerable<FileEntry>? entries = null, bool quiet = false)
    {
        var produced = new HashSet<string>(
            _files.Select(f => FileOps.ToFullPathOrEmpty(f.OutputPath)).Where(path => path.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        int deleted = 0;
        foreach (var f in entries ?? _files)
        {
            if (f.Status != FileStatus.Completed) continue;
            if (string.IsNullOrEmpty(f.SourcePath) || !File.Exists(f.SourcePath)) continue;

            var full = FileOps.ToFullPathOrEmpty(f.SourcePath);
            if (full.Length == 0) continue;
            if (produced.Contains(full)) continue;
            if (FileOps.IsInside(full, WorkingDir) || FileOps.IsInside(full, OutputDir)) continue;

            if (FileOps.DeleteToRecycleBin(full))
            {
                deleted++;
                if (!quiet) OnLog($"  🗑 源文件已放入回收站: {Path.GetFileName(full)}");
            }
            else if (!quiet)
            {
                OnLog($"  ✗ 无法删除: {Path.GetFileName(full)}");
            }
        }
        return deleted;
    }

    // ── 阶段6: 移动到目标目录 ──
    private void PhaseMoveToTargets(bool useUnifiedOutput, string unifiedOutputDir)
    {
        OnLog("=== 阶段6: 移动到目标目录 ===");
        var outputFiles = Directory.GetFiles(OutputDir).Where(f => !f.EndsWith(".exe") && !f.EndsWith(".bat")).ToList();

        // 本批里同名项 → 成品文件路径。解密器按"基名"命名产物，
        // 所以同名项只有一份产物：后来的项直接复用，而不是报"未找到输出"。
        var movedByBase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var f in _files)
        {
            if (f.Status == FileStatus.Failed || f.Status == FileStatus.NeedsManualKGG) continue;

            if (movedByBase.TryGetValue(f.BaseName, out var alreadyMoved))
            {
                f.OutputPath = alreadyMoved;
                f.Status = FileStatus.Completed;
                OnFileChanged(f);
                OnLog($"  ~ {f.BaseName}: 与本批同名项重复，成品已产出 → {Path.GetFileName(alreadyMoved)}");
                continue;
            }

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
            else if (IsInboxDirectory(f.SourceDirectory) && !string.IsNullOrEmpty(_fallbackOutputDir))
                targetDir = _fallbackOutputDir;   // 收件箱来的：成品进成品目录，不回投热文件夹
            else
                targetDir = f.SourceDirectory;

            // 一次运行可能同时产出 .flac 与 .mp3（转码没跳过时），都要搬走，
            // 只搬第一个会把 mp3 落在工作区里成为孤儿文件
            var produced = outputFiles
                .Where(of => Path.GetFileNameWithoutExtension(of)
                    .Equals(f.BaseName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            try
            {
                foreach (var producedFile in produced)
                {
                    var target = Path.Combine(targetDir, Path.GetFileName(producedFile));
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(producedFile, target);

                    // OutputPath 指向用户最想要的那个：转码产物优先于解密出来的原始文件
                    if (f.OutputPath is null || OutputRank(target) < OutputRank(f.OutputPath))
                        f.OutputPath = target;

                    OnLog($"  → {Path.GetFileName(target)} → {targetDir}");
                }

                f.Status = FileStatus.Completed;
                OnFileChanged(f);

                if (!string.IsNullOrEmpty(f.OutputPath))
                    movedByBase[f.BaseName] = f.OutputPath;
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

    /// <summary>产物的"想要程度"排序：转码过的排在解密出来的原始文件前面。</summary>
    private static readonly string[] OutputPreference = { ".mp3", ".wav", ".flac", ".ogg" };

    private static int OutputRank(string path)
    {
        var idx = Array.IndexOf(OutputPreference, Path.GetExtension(path).ToLowerInvariant());
        return idx < 0 ? OutputPreference.Length : idx;
    }
}
