using System.IO;
using System.Text.Json;
using FolderThemeStudio.App.DynamicIsland.Plugins;
using FolderThemeStudio.App.Settings;
using FolderThemeStudio.Core.Rendering;
using FolderThemeStudio.Core.Tests.TestSupport;
using Xunit;

namespace FolderThemeStudio.Core.Tests.Application;
public sealed class ExampleAndIconsTests
{
    [Fact]
    public void InstallButtonBuildsTheConfigurableScheduleExample()
    {
        using var temp = new TemporaryDirectory(); var path = Path.Combine(temp.Path, "example.islandpkg");
        IslandPluginManagerView.WriteExample(path);
        using var manager = new IslandPluginManager(Path.Combine(temp.Path, "plugins"));
        var manifest = manager.InspectPackage(path);
        Assert.Equal("studio.custom-settings", manifest.Id); Assert.Equal(10, manifest.Settings.Length);
        Assert.Contains(manifest.Settings, item => item.Type == "time");
    }
    [Fact]
    public async Task FiveBundledIconsMaterializeAndImportWithoutDesktopChanges()
    {
        using var temp = new TemporaryDirectory(); var service = new ImportedImageService(Path.Combine(temp.Path, "normalized"));
        Assert.Equal(5, BundledFolderIcons.All.Count);
        foreach (var icon in BundledFolderIcons.All)
        {
            var path = BundledFolderIcons.Materialize(icon.Id, Path.Combine(temp.Path, "source"));
            var imported = await service.ImportAsync(path, CancellationToken.None);
            Assert.True(imported.Success, imported.Error); Assert.NotNull(imported.Preview);
        }
    }
    [Theory]
    [InlineData("00:00", true)] [InlineData("23:59", true)] [InlineData("24:00", false)] [InlineData("9:00", false)]
    public void TimeSettingRequiresExactLocalClockFormat(string value, bool valid)
    {
        var schema = new PluginSettingsSchema([new("time", "时间", "time", JsonSerializer.SerializeToElement("09:00"))]);
        var input = new Dictionary<string, JsonElement> { ["time"] = JsonSerializer.SerializeToElement(value) };
        if (valid) Assert.Equal(value, schema.Resolve(input)["time"].GetString());
        else Assert.Throws<InvalidDataException>(() => { schema.Resolve(input); });
    }
    [Fact]
    public void DailyReminderFiresOncePerMinuteAndCountdownCompletes()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var source = File.ReadAllText(Path.Combine(root, "plugins-sdk", "examples", "custom-settings", "main.js"));
        var runtime = new PluginScriptRuntime(source, "test.schedule");
        runtime.Invoke("init", new { message="计划", reminderEnabled=true, reminderTime="09:00", reminderText="开工", focusEnabled=false });
        var at = new DateTimeOffset(DateTime.Today.AddHours(9)).ToUnixTimeMilliseconds();
        Assert.Equal("开工", runtime.Invoke("tick", at)!.Text);
        Assert.Equal("计划", runtime.Invoke("tick", at + 11000)!.Text);
        var focus = new PluginScriptRuntime(source, "test.focus");
        Assert.Contains("01:00", focus.Invoke("init", new { message="计划", focusEnabled=true, focusMinutes=1 })!.Text);
        Assert.Contains("专注完成", focus.Invoke("tick", DateTimeOffset.Now.AddMinutes(2).ToUnixTimeMilliseconds())!.Text);
    }
    [Fact]
    public void PromptAsksForRequirementsBeforeGeneratingCode()
    {
        Assert.Contains("第一次回复，只能逐字回答：好的，现在请你发送你的软件需求", IslandPluginGuide.AiPrompt);
    }
}
