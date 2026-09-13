using Microsoft.UI.Xaml;

namespace KGMusicConverter;

public sealed partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();

        // 崩溃可诊断：未处理异常先把原因写进日志，再按原样结束。
        // 没有这一层的话，用户遇到闪退只能看到"程序没了"，日志里干干净净 —— 无从查起。
        UnhandledException += (_, e) =>
            AppLog.Log($"FATAL XAML 未处理异常 (HRESULT=0x{e.Exception.HResult:X8}): {e.Exception}\n{e.Exception.StackTrace}");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppLog.Log($"FATAL 未处理异常: {e.ExceptionObject}");

        TaskScheduler.UnobservedTaskException += (_, e) =>
            AppLog.Log($"未观察的任务异常: {e.Exception}");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
