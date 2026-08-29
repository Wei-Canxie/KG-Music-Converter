using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KugoMusicConverter;

public enum FileStatus
{
    Pending,
    Processing,
    Completed,
    Failed,
    NeedsManualKGG
}

/// <summary>
/// 文件条目 — 实现 INotifyPropertyChanged 以便 UI 自动刷新状态。
/// </summary>
public class FileEntry : INotifyPropertyChanged
{
    private FileStatus _status;
    private string? _errorMessage;

    public string SourcePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string SourceDirectory { get; set; } = "";
    public string BaseName { get; set; } = "";
    public string Extension { get; set; } = "";
    public string? OutputPath { get; set; }

    public bool IsKgg => Extension.Equals(".kgg", StringComparison.OrdinalIgnoreCase);

    public FileStatus Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set { _errorMessage = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
