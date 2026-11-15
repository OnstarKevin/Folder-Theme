using System.IO;
using System.Runtime.ExceptionServices;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Data;
using System.Windows.Threading;
using Xunit;

namespace FolderThemeStudio.Core.Tests.Application;

public sealed class IslandHoverBindingTests
{
    [Fact]
    public void ActualHoverProgressMarkupReadsReadonlyValueWithoutCreatingAnyWindow()
    {
        // Exercise the shipped markup and WPF binding engine on an STA thread.
        // No Application.Run, Window, native handle, input, or real media source.
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var document = XDocument.Load(Path.Combine(root, "src", "FolderThemeStudio.App", "DynamicIsland", "IslandHoverWindow.xaml"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var markup = Assert.Single(document.Descendants(wpf + "ProgressBar")).ToString();
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var progress = (ProgressBar)XamlReader.Parse(markup);
                var property = System.Windows.Controls.Primitives.RangeBase.ValueProperty;
                var shipped = BindingOperations.GetBinding(progress, property)!;
                // Explicit source activates immediately without showing/measuring a window.
                BindingOperations.SetBinding(progress, property, new Binding(shipped.Path.Path) { Mode = shipped.Mode, Source = new ReadonlyProgress() });
                Assert.Equal(.25, progress.Value);
                BindingOperations.SetBinding(progress, property, new Binding(shipped.Path.Path) { Mode = shipped.Mode, Source = new ReadonlyProgress { Value = .75 } });
                Assert.Equal(.75, progress.Value);
                BindingOperations.ClearBinding(progress, property);
            }
            catch (Exception ex) { error = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Background binding check timed out.");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    public sealed class ReadonlyProgress
    {
        public double Value { get; init; } = .25;
        public double MusicProgress => Value;
    }
}
