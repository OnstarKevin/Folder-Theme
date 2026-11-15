using System.Text.Json;
using System.IO;
using FolderThemeStudio.App.DynamicIsland.Plugins;
using Xunit;
using System.IO.Compression;

namespace FolderThemeStudio.Core.Tests.Application;

public sealed class PluginSettingsSchemaTests
{
    [Fact]
    public void LegacyInitContextKeepsOriginalShape()
    {
        var runtime = new PluginScriptRuntime("function onInit(c){island.publish({text:Object.keys(c).join(',')});}", "test.legacy");
        Assert.Equal("id,apiVersion", runtime.Invoke("init", null)!.Text);
    }
    [Fact]
    public void ScriptReceivesSettingsDuringInitAndChange()
    {
        var script = new PluginScriptRuntime("function onInit(c){island.publish({text:c.settings.title});} function onEvent(e){if(e.name==='settings.changed')island.publish({text:e.payload.title});}", "sample.item");
        Assert.Equal("初始", script.Invoke("init", new { title = "初始" })!.Text);
        Assert.Equal("更新", script.Invoke("event", new { name = "settings.changed", payload = new { title = "更新" } })!.Text);
    }

    [Fact]
    public async Task ConfigPersistsAndCompatibleValuesSurviveUpdate()
    {
        var root = Path.Combine(Path.GetTempPath(), "fts-settings-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string Package(string version, object[] settings)
            {
                var path = Path.Combine(root, Guid.NewGuid() + ".islandpkg");
                using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
                using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open())) writer.Write(JsonSerializer.Serialize(new { id="test.settings", name="设置", version, apiVersion=1, minHostVersion="0.1.0-beta.24-preview.6", entry="main.js", permissions=Array.Empty<string>(), settings }));
                using (var writer = new StreamWriter(zip.CreateEntry("main.js").Open())) writer.Write("function onInit(c){}");
                return path;
            }
            var settings = new object[] { new { key="title", label="标题", type="string", @default="默认" } };
            await using (var manager = new IslandPluginManager(root))
            {
                await manager.StopAsync(); await manager.InstallAsync(Package("1.0.0", settings), []);
                await manager.UpdateSettingsAsync("test.settings", new Dictionary<string, JsonElement> { ["title"] = JsonSerializer.SerializeToElement("已保存") });
                manager.List()[0].Settings["title"] = JsonSerializer.SerializeToElement("外部修改");
                Assert.Equal("已保存", manager.GetSettings("test.settings")["title"].GetString());
                await manager.InstallAsync(Package("2.0.0", settings), []);
            }
            await using var reopened = new IslandPluginManager(root);
            Assert.Equal("已保存", reopened.GetSettings("test.settings")["title"].GetString());
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void BundledExampleUsesInitialAndUpdatedValues()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var source = File.ReadAllText(Path.Combine(root, "plugins-sdk", "examples", "custom-settings", "main.js"));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "plugins-sdk", "examples", "custom-settings", "manifest.json")));
        Assert.Equal(10, manifest.RootElement.GetProperty("settings").GetArrayLength());
        Assert.NotNull(typeof(IslandPluginManagerView).Assembly.GetManifestResourceInfo("fts.example.settings.manifest"));
        Assert.NotNull(typeof(IslandPluginManagerView).Assembly.GetManifestResourceInfo("fts.example.settings.script"));
        var runtime = new PluginScriptRuntime(source, "studio.custom-settings");
        Assert.Equal("你好", runtime.Invoke("init", JsonSerializer.SerializeToElement(new { message = "你好", showIcon = true, duration = 10 }))!.Text);
        Assert.Equal("已修改", runtime.Invoke("event", new { name = "settings.changed", payload = new { message = "已修改", showIcon = false, duration = 5 } })!.Text);
    }
    [Fact]
    public void SettingsRequireNewHostAndFutureHostIsRejected()
    {
        var root = Path.Combine(Path.GetTempPath(), "fts-version-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string Package(string minimum)
            {
                var path=Path.Combine(root,Guid.NewGuid()+".islandpkg"); using var zip=ZipFile.Open(path,ZipArchiveMode.Create);
                using(var writer=new StreamWriter(zip.CreateEntry("manifest.json").Open()))writer.Write(JsonSerializer.Serialize(new {id="test.version",name="Version",version="1.0.0",apiVersion=1,minHostVersion=minimum,entry="main.js",permissions=Array.Empty<string>(),settings=new[]{new {key="title",label="标题",type="string",@default="x"}}}));
                using(var writer=new StreamWriter(zip.CreateEntry("main.js").Open()))writer.Write("function onInit(c){}"); return path;
            }
            using var manager=new IslandPluginManager(root);
            Assert.Throws<InvalidDataException>(() => manager.InspectPackage(Package("0.1.0-beta.23")));
            Assert.Throws<InvalidDataException>(() => manager.InspectPackage(Package("0.1.0-beta.24-preview.9")));
            Assert.Equal("test.version",manager.InspectPackage(Package("0.1.0-beta.24-preview.6")).Id);
        }
        finally { Directory.Delete(root,true); }
    }
    [Fact]
    public void SchemaValidatesValuesAndFillsDefaults()
    {
        var schema = new PluginSettingsSchema([new("title", "标题", "string", JsonSerializer.SerializeToElement("你好")), new("enabled", "开启", "boolean", JsonSerializer.SerializeToElement(true))]);
        var values = schema.Resolve(new Dictionary<string, JsonElement> { ["title"] = JsonSerializer.SerializeToElement("自定义") });
        Assert.Equal("自定义", values["title"].GetString()); Assert.True(values["enabled"].GetBoolean());
        Assert.Throws<InvalidDataException>(() => { schema.Resolve(new Dictionary<string, JsonElement> { ["enabled"] = JsonSerializer.SerializeToElement("yes") }); });
    }

    [Fact]
    public void DuplicateAndUnsafeKeysAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => { _ = new PluginSettingsSchema([new("../x", "X", "string", JsonSerializer.SerializeToElement(""))]); });
        Assert.Throws<InvalidDataException>(() => { _ = new PluginSettingsSchema([new("x", "X", "boolean", JsonSerializer.SerializeToElement(true)), new("x", "X", "boolean", JsonSerializer.SerializeToElement(false))]); });
    }
}
