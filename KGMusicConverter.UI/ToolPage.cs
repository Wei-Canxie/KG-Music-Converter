using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace KGMusicConverter;

/// <summary>
/// 页面基类：记录挂在页面上的事件处理器，页面被丢弃时统一注销，
/// 并提供"滚动位置"的存取（切页回来能回到原处）。
///
/// 页面会因为导航、应用更改、主题切换被反复重建。没有这层登记，
/// 每次重建都会留下一个活着的订阅，回调还会打向已经没人看得见的控件。
/// </summary>
internal abstract class ToolPage : UserControl, IDisposable
{
    private readonly List<Action> _unsubscribes = new();

    /// <summary>登记一个"页面销毁时要执行的解绑动作"。</summary>
    protected void RegisterUnsubscribe(Action unsubscribe) => _unsubscribes.Add(unsubscribe);

    /// <summary>当前页第一个纵向 ScrollViewer 的滚动位置。</summary>
    internal double? FindScrollOffset()
    {
        var viewer = FindScrollViewer(this);
        return viewer is null ? null : viewer.VerticalOffset;
    }

    /// <summary>恢复滚动位置。</summary>
    internal void ScrollToOffset(double offset)
    {
        var viewer = FindScrollViewer(this);
        viewer?.ChangeView(null, offset, null, true);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject? parent)
    {
        if (parent is null) return null;

        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer viewer &&
                viewer.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled)
            {
                return viewer;
            }

            var nested = FindScrollViewer(child);
            if (nested is not null) return nested;
        }

        return null;
    }

    public void Dispose()
    {
        foreach (var unsubscribe in _unsubscribes)
        {
            try { unsubscribe(); }
            catch (Exception ex) { AppLog.Log($"Page unsubscribe failed: {ex.Message}"); }
        }

        _unsubscribes.Clear();
    }
}
