using System.Windows;
using System.Windows.Threading;

namespace FolderThemeStudio.App.Performance;

public partial class PerformanceView : System.Windows.Controls.UserControl
{
    private readonly ProcessPerformanceSampler sampler = new(Environment.ProcessorCount);
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private Window? owner;
    private bool active, sampling, resetNext = true, released;
    private int generation;
    private SoftwarePerformanceSnapshot? latest;
    private CancellationTokenSource? delayed;
    private string? delayedSummary;
    public PerformanceView()
    {
        InitializeComponent();
        timer.Tick += Tick;
        IsVisibleChanged += (_, _) => UpdateActivity();
        Loaded += (_, _) =>
        {
            DetachOwner(); owner = Window.GetWindow(this);
            if (owner is not null) owner.StateChanged += OwnerStateChanged;
            UpdateActivity();
        };
        Unloaded += (_, _) => { Stop(); DetachOwner(); };
    }
    private void OwnerStateChanged(object? sender, EventArgs e) => UpdateActivity();
    private void DetachOwner() { if (owner is not null) owner.StateChanged -= OwnerStateChanged; owner = null; }
    private void UpdateActivity()
    {
        var next = !released && IsVisible && owner?.WindowState != WindowState.Minimized;
        if (!next) { Stop(); return; }
        if (active) return;
        active = true; resetNext = true; latest = null;
        CpuValue.Text = "采样中"; WorkingSetValue.Text = PrivateValue.Text = ProcessCountValue.Text = "—";
        StatusText.Text = "正在读取软件进程统计…"; timer.Start(); Tick(this, EventArgs.Empty);
    }
    private void Stop() { active = false; timer.Stop(); resetNext = true; generation++; }
    public void Release() { released = true; delayed?.Cancel(); Stop(); DetachOwner(); timer.Tick -= Tick; }
    private async void Delayed_Click(object sender, RoutedEventArgs e)
    {
        if (delayed is not null || released) return;
        var request = new CancellationTokenSource(); var token = request.Token;
        delayed = request; DelayedButton.IsEnabled = false; CopyDelayedButton.IsEnabled = false;
        var host = Window.GetWindow(this);
        bool Background() => host is not null && (!host.IsVisible || host.WindowState == WindowState.Minimized);
        DelayedText.Text = "等待10秒：请手动收起主窗口，完成后再回来查看。";
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), token);
            var wasBackground = Background();
            var probe = new ProcessPerformanceSampler(Environment.ProcessorCount);
            await Task.Run(probe.Read, token);
            await Task.Delay(TimeSpan.FromSeconds(2), token);
            var snapshot = await Task.Run(probe.Read, token);
            token.ThrowIfCancellationRequested();
            var state = wasBackground && Background() ? "后台快照（开始与结束时主窗口已收起）" : "延迟快照（未确认主窗口收起）";
            delayedSummary = $"{state}\n时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\nCPU: {snapshot.CpuPercent:0.0}%\n工作集合计: {snapshot.WorkingSetBytes / 1048576d:0.0} MiB\n私有内存合计: {snapshot.PrivateBytes / 1048576d:0.0} MiB\n同路径软件进程数: {snapshot.ProcessCount}";
            DelayedText.Text = delayedSummary; CopyDelayedButton.IsEnabled = true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        { if (!released) DelayedText.Text = "本次快照未完成，请稍后重试。"; }
        finally { if (ReferenceEquals(delayed, request)) delayed = null; request.Dispose(); if (!released) DelayedButton.IsEnabled = true; }
    }
    private void CopyDelayed_Click(object sender, RoutedEventArgs e)
    {
        if (delayedSummary is null) return;
        try { System.Windows.Clipboard.SetText(delayedSummary); DelayedText.Text = "已复制。\n" + delayedSummary; }
        catch (System.Runtime.InteropServices.ExternalException) { DelayedText.Text = "剪贴板忙，请稍后重试。"; }
    }
    private async void Tick(object? sender, EventArgs e)
    {
        if (!active || sampling) return;
        sampling = true; RefreshButton.IsEnabled = false;
        var revision = generation; var reset = resetNext; resetNext = false;
        try
        {
            var snapshot = await Task.Run(() => { if (reset) sampler.Reset(); return sampler.Read(); });
            if (!active || revision != generation || released) return;
            latest = snapshot;
            CpuValue.Text = snapshot.CpuPercent is { } cpu ? $"{cpu:0.0}%" : "采样中";
            WorkingSetValue.Text = $"{snapshot.WorkingSetBytes / 1048576d:0.0} MiB";
            PrivateValue.Text = $"{snapshot.PrivateBytes / 1048576d:0.0} MiB";
            ProcessCountValue.Text = snapshot.ProcessCount.ToString();
            StatusText.Text = "更新于 " + DateTime.Now.ToString("HH:mm:ss");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        { if (active && !released) StatusText.Text = "暂时无法读取软件进程统计。"; }
        finally { sampling = false; if (!released) RefreshButton.IsEnabled = true; }
    }
    private void Refresh_Click(object sender, RoutedEventArgs e) => Tick(sender, e);
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (latest is null) { StatusText.Text = "请先等待一次采样。"; return; }
        var text = $"Folder Theme Studio {typeof(App).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false).OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion}\nCPU: {CpuValue.Text}\n工作集合计: {WorkingSetValue.Text}\n私有内存合计: {PrivateValue.Text}\n同路径软件进程数: {ProcessCountValue.Text}\n时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        try { System.Windows.Clipboard.SetText(text); StatusText.Text = "已复制统计，可将不同状态的数据发来比较。"; }
        catch (System.Runtime.InteropServices.ExternalException) { StatusText.Text = "剪贴板忙，请稍后重试。"; }
    }
}
