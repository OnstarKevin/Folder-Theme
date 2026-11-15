using FolderThemeStudio.App.ViewModels;
using FolderThemeStudio.App.Performance;
using Xunit;

namespace FolderThemeStudio.Core.Tests.Application;

public sealed class BackgroundPerformanceTests
{
    [Fact]
    public async Task HiddenEditorDefersRenderingAndOnlyRendersDirtyStateOnReturn()
    {
        var fixture = new FolderThemeStudio.Core.Tests.TestSupport.MainViewModelFixture();
        using var model = new MainViewModel(fixture.Coordinator, fixture.Dialogs, fixture.Themes, fixture.Renderer,
            fixture.Delay, new FolderThemeStudio.Core.Tests.TestSupport.ImmediateViewModelDispatcher(), fixture.PreviewWorker,
            fixture.Images, fixture.Monitoring, fixture.MonitoringAssets, activatePreviews: false);
        await model.WaitForPreviewAsync(); Assert.Empty(fixture.Renderer.Calls);
        model.SetPreviewActivity(true); await model.WaitForPreviewAsync(); Assert.Equal(4, fixture.Renderer.Calls.Count);
        model.SetPreviewActivity(false); model.GradientStart = "#153759";
        await model.WaitForPreviewAsync(); Assert.Equal(4, fixture.Renderer.Calls.Count);
        model.SetPreviewActivity(true); await model.WaitForPreviewAsync(); Assert.Equal(8, fixture.Renderer.Calls.Count);
        model.SetPreviewActivity(false); model.SetPreviewActivity(true);
        await model.WaitForPreviewAsync(); Assert.Equal(8, fixture.Renderer.Calls.Count);
    }

    [Fact]
    public async Task SuspendingPendingPreviewCancelsWithoutRenderingOrDisposedTokenFailure()
    {
        var fixture = new FolderThemeStudio.Core.Tests.TestSupport.MainViewModelFixture(useControlledDelay: true);
        using var model = new MainViewModel(fixture.Coordinator, fixture.Dialogs, fixture.Themes, fixture.Renderer,
            fixture.Delay, new FolderThemeStudio.Core.Tests.TestSupport.ImmediateViewModelDispatcher(), fixture.PreviewWorker,
            fixture.Images, fixture.Monitoring, fixture.MonitoringAssets, activatePreviews: false);
        model.SetPreviewActivity(true); model.SetPreviewActivity(false);
        await model.WaitForPreviewAsync(); Assert.Empty(fixture.Renderer.Calls); Assert.False(model.IsPreviewRendering);
        model.SetPreviewActivity(true); fixture.Delay.CompleteLatest();
        await model.WaitForPreviewAsync(); Assert.Equal(4, fixture.Renderer.Calls.Count);
    }
    [Fact]
    public async Task PreviewThreadStartsOnlyForWorkAndKeepsStaExecution()
    {
        using var worker = new StaPreviewWorkScheduler();
        Assert.False(worker.IsStarted);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker.RunAsync(() => 1, cancellation.Token));
        Assert.False(worker.IsStarted);
        Assert.Equal(ApartmentState.STA, await worker.RunAsync(() => Thread.CurrentThread.GetApartmentState(), CancellationToken.None));
        Assert.True(worker.IsStarted);
    }

    [Fact]
    public void CpuCalculationHandlesNewAndRemovedProcessesWithoutNegativeOrSpikingValues()
    {
        var sampler = new ProcessPerformanceSampler(2);
        var first = sampler.Calculate(10, [new(1, 100, 200, TimeSpan.FromSeconds(2))]);
        Assert.Null(first.CpuPercent);
        var second = sampler.Calculate(12, [new(1, 110, 220, TimeSpan.FromSeconds(3)), new(2, 10, 20, TimeSpan.FromSeconds(40))]);
        Assert.Equal(25, second.CpuPercent); Assert.Equal(120, second.WorkingSetBytes); Assert.Equal(240, second.PrivateBytes); Assert.Equal(2, second.ProcessCount);
        var third = sampler.Calculate(14, [new(2, 10, 20, TimeSpan.FromSeconds(41))]);
        Assert.Equal(25, third.CpuPercent);
        var reset = sampler.Calculate(15, [new(2, 10, 20, TimeSpan.Zero)]);
        Assert.Equal(0, reset.CpuPercent);
    }
}
