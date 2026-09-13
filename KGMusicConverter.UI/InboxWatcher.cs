using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.UI.Dispatching;

namespace KGMusicConverter;

/// <summary>
/// 收件箱热文件夹：丢进 <see cref="Workspace.Inbox"/> 的音乐文件自动入队。
///
/// 检测用 <see cref="FileSystemWatcher"/>（事件驱动，不轮询），但事件只在文件"刚出现/正在写"
/// 时触发 —— 一个 37MB 的文件被复制进来会触发多次 Changed。所以再加一道<b>落盘稳定判定</b>：
/// 每秒检查一次候选文件，大小连续两次不变、且能被独占打开，才算写完，这时才回调。
/// 这样不会把半个文件塞进队列。
/// </summary>
internal sealed class InboxWatcher : IDisposable
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".kgg", ".kgm", ".kgma", ".vpr", ".flac" };

    /// <summary>下载/复制过程中的半成品后缀，直接忽略。</summary>
    private static readonly string[] TransientSuffixes =
        { ".tmp", ".part", ".crdownload", ".download", ".!ut" };

    private readonly Action<IReadOnlyList<string>> _onArrived;
    private readonly DispatcherQueue? _dispatcher;
    private readonly Dictionary<string, long> _pending = new(StringComparer.OrdinalIgnoreCase);

    private FileSystemWatcher? _watcher;
    private DispatcherQueueTimer? _settleTimer;

    internal InboxWatcher(Action<IReadOnlyList<string>> onArrived)
    {
        _onArrived = onArrived;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        try
        {
            Directory.CreateDirectory(Workspace.Inbox);

            _watcher = new FileSystemWatcher(Workspace.Inbox)
            {
                IncludeSubdirectories = false,
                Filter = "*.*",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            _watcher.Created += OnFileTouched;
            _watcher.Changed += OnFileTouched;
            _watcher.Renamed += OnFileRenamed;
            _watcher.Error += (_, e) => AppLog.Log($"InboxWatcher error: {e.GetException().Message}");
            _watcher.EnableRaisingEvents = true;

            _settleTimer = _dispatcher?.CreateTimer();
            if (_settleTimer is not null)
            {
                _settleTimer.Interval = TimeSpan.FromSeconds(1);
                _settleTimer.Tick += (_, _) => FlushSettledFiles();
                _settleTimer.Start();
            }

            AppLog.Log($"InboxWatcher watching {Workspace.Inbox}");
        }
        catch (Exception ex)
        {
            AppLog.Log($"InboxWatcher init failed: {ex.Message}");
        }
    }

    /// <summary>这个文件是不是本工具能处理的候选（收件箱扫描也用它）。</summary>
    internal static bool IsCandidateFile(string path)
    {
        try
        {
            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) return false;
            if (name.StartsWith("~$", StringComparison.Ordinal)) return false;

            var ext = Path.GetExtension(name);
            if (!SupportedExtensions.Contains(ext)) return false;

            foreach (var transient in TransientSuffixes)
            {
                if (name.EndsWith(transient, StringComparison.OrdinalIgnoreCase)) return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void OnFileTouched(object sender, FileSystemEventArgs e) => TrackCandidate(e.FullPath);

    private void OnFileRenamed(object sender, RenamedEventArgs e) => TrackCandidate(e.FullPath);

    private void TrackCandidate(string path)
    {
        try
        {
            if (!IsCandidateFile(path)) return;

            // 大小未知（文件还没成形）→ 记为 -1，下一轮再量
            long size = -1;
            try { size = new FileInfo(path).Length; } catch { }

            lock (_pending) _pending[path] = size;
        }
        catch (Exception ex)
        {
            AppLog.Log($"InboxWatcher.TrackCandidate failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 每秒检查：大小与上一轮相同 + 能被独占打开 → 认定写完，回调。
    /// 大小还在变或打不开就保留，等下一轮。
    /// </summary>
    private void FlushSettledFiles()
    {
        List<string>? ready = null;

        try
        {
            List<string> keys;
            lock (_pending) keys = _pending.Keys.ToList();

            foreach (var path in keys)
            {
                long size;
                try
                {
                    if (!File.Exists(path))
                    {
                        lock (_pending) _pending.Remove(path);
                        continue;
                    }
                    size = new FileInfo(path).Length;
                }
                catch
                {
                    continue;
                }

                long previous;
                lock (_pending)
                {
                    if (!_pending.TryGetValue(path, out previous)) continue;
                }

                // 第一次量到：记下来，下一轮再比
                if (previous < 0)
                {
                    lock (_pending) _pending[path] = size;
                    continue;
                }

                // 还在长 → 更新记录，继续等
                if (size != previous)
                {
                    lock (_pending) _pending[path] = size;
                    continue;
                }

                if (!IsFileReadable(path)) continue;

                lock (_pending) _pending.Remove(path);
                (ready ??= new List<string>()).Add(path);
            }
        }
        catch (Exception ex)
        {
            AppLog.Log($"InboxWatcher.FlushSettledFiles failed: {ex.Message}");
        }

        if (ready is null || ready.Count == 0) return;

        if (_dispatcher is null)
        {
            _onArrived(ready);
            return;
        }

        _dispatcher.TryEnqueue(() => _onArrived(ready));
    }

    private static bool IsFileReadable(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return stream.Length >= 0;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            if (_watcher is not null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnFileTouched;
                _watcher.Changed -= OnFileTouched;
                _watcher.Renamed -= OnFileRenamed;
                _watcher.Dispose();
                _watcher = null;
            }

            if (_settleTimer is not null)
            {
                _settleTimer.Stop();
                _settleTimer = null;
            }

            lock (_pending) _pending.Clear();
        }
        catch (Exception ex)
        {
            AppLog.Log($"InboxWatcher.Dispose failed: {ex.Message}");
        }
    }
}
