using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KGMusicConverter;

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

    /// <summary>
    /// 工作区里使用的基础名。默认等于 <see cref="BaseName"/>，只有"不同目录的同名文件"
    /// 才会被改成带序号的名字。
    ///
    /// 为什么需要它：解密器按<b>基名</b>命名产物，工作区里同名输入还会互相覆盖 ——
    /// 不改名的话，同名不同目录的两个文件只有一个能拿回成品。
    /// </summary>
    public string WorkBaseName { get; set; } = "";

    /// <summary>工作区里的文件名（工作基名 + 当前扩展名）。</summary>
    public string WorkFileName =>
        (WorkBaseName.Length > 0 ? WorkBaseName : BaseName) + Extension;
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
