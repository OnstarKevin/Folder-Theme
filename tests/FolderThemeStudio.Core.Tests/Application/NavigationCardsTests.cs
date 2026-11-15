using System.IO;
using System.Xml.Linq;
using Xunit;

namespace FolderThemeStudio.Core.Tests.Application;

public sealed class NavigationCardsTests
{
    [Fact]
    public void MainNavigationHasDirectPluginsAndSettingsCardsCoverAllModules()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var main = XDocument.Load(Path.Combine(root, "src", "FolderThemeStudio.App", "MainWindow.xaml"));
        Assert.Contains(main.Descendants(), e => (string?)e.Attribute(x + "Name") == "PluginsNavButton" && (string?)e.Attribute("Click") == "PluginsNav_Click");
        Assert.DoesNotContain(main.Descendants(), e => (string?)e.Attribute(x + "Name") == "SettingsNavigation");
        var workspace = XDocument.Load(Path.Combine(root, "src", "FolderThemeStudio.App", "SettingsWorkspace.xaml"));
        var cards = workspace.Descendants().Where(e => (string?)e.Attribute("Click") == "Module_Click").Select(e => (string?)e.Attribute("Tag")).ToArray();
        Assert.Equal(new[] { "general", "features", "island", "appearance", "music", "messages", "performance" }, cards);
    }
}
