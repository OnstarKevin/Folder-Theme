using Xunit;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using FolderThemeStudio.App.DynamicIsland.Plugins;
namespace FolderThemeStudio.Core.Tests;
public sealed class IslandPluginTests
{
 [Fact] public async Task InstallationRequiresGrantsAndPreservesRejectedUpdate()
 {
  using var temp = new PluginTemp(); await using var manager = new IslandPluginManager(temp.Root);
  await manager.InstallAsync(temp.Package("1.0.0", []), []);
  await Assert.ThrowsAsync<UnauthorizedAccessException>(() => manager.InstallAsync(temp.Package("2.0.0", ["clipboard.read"]), []));
  Assert.Equal("1.0.0", Assert.Single(manager.List()).Manifest.Version);
  await manager.InstallAsync(temp.Package("2.0.0", ["clipboard.read"]), ["clipboard.read"]);
  Assert.Equal("2.0.0", Assert.Single(manager.List()).Manifest.Version);
  await manager.UninstallAsync("example.clock"); Assert.Empty(manager.List());
 }
 [Theory] [InlineData("../escape.js")] [InlineData("assets/../../escape.js")] [InlineData("assets/plugin.dll")] [InlineData("assets/name:stream")] [InlineData("assets/.. /escape.png")]
 public void RejectsUnsafeEntries(string entry)
 { using var temp = new PluginTemp(); using var manager = new IslandPluginManager(temp.Root); Assert.Throws<InvalidDataException>(() => manager.InspectPackage(temp.Package("1.0.0", [], entry))); }
 [Theory] [InlineData("system.exec")] [InlineData("network")]
 public void RejectsUnsupportedPermission(string permission)
 { using var temp = new PluginTemp(); using var manager = new IslandPluginManager(temp.Root); Assert.Throws<InvalidDataException>(() => manager.InspectPackage(temp.Package("1.0.0", [permission]))); }
 [Fact] public void EngineRunsLifecycleWithoutClrAndBoundsLoops()
 { var engine = new PluginScriptRuntime("function onInit(c){island.publish({text:typeof System,details:c.id});} function onTick(n){while(true){}}", "example.clock");
   Assert.Equal("undefined", engine.Invoke("init", null)!.Text); Assert.Equal("example.clock", engine.CurrentContent!.Details); Assert.ThrowsAny<Exception>(() => engine.Invoke("tick", 1)); }
 [Fact] public void EngineKeepsStateAndClears()
 { var engine = new PluginScriptRuntime("let n=0; function onEvent(e){island.publish({text:e.name+ ++n,ttlMs:999999});} function onDispose(){island.clear();}", "example.clock");
   Assert.Equal("ping1", engine.Invoke("event", new {name="ping"})!.Text); Assert.Equal(60000, engine.CurrentContent!.TtlMs);
   Assert.Equal("ping2", engine.Invoke("event", new {name="ping"})!.Text); engine.Invoke("dispose", null); Assert.Null(engine.CurrentContent); }
 [Fact] public async Task GrantsAndDisabledStateSurviveReloadAndCannotBeMutatedThroughList()
 {
  using var temp=new PluginTemp();
  await using(var manager=new IslandPluginManager(temp.Root))
  {
   await manager.InstallAsync(temp.Package("1.0.0",["clipboard.read"]),["clipboard.read"]);
   var item=Assert.Single(manager.List());item.GrantedPermissions[0]="system.exec";
   Assert.Equal("clipboard.read",Assert.Single(manager.List()).GrantedPermissions[0]);
  }
  await using var reopened=new IslandPluginManager(temp.Root);
  var saved=Assert.Single(reopened.List());Assert.False(saved.Enabled);Assert.Equal("clipboard.read",Assert.Single(saved.GrantedPermissions));
 }
 [Fact] public void EngineRejectsOversizedOutput()
 {
  var engine=new PluginScriptRuntime("function onInit(){island.publish({text:'x'.repeat(161)});}","example.clock");
  Assert.Throws<InvalidDataException>(()=>engine.Invoke("init",null));
 }
 [Fact] public void NoOpTickDoesNotRepublishOrExtendContentLifetime()
 {
  var engine=new PluginScriptRuntime("function onInit(){island.publish({text:'once'});}","example.clock");
  engine.Invoke("init",null);Assert.True(engine.LastChanged);
  engine.Invoke("tick",123);Assert.False(engine.LastChanged);
 }
 [Fact] public async Task RealWorkerSupportsLifecycleAndFiltersSensitiveEvents()
 {
  using var temp=new PluginTemp();await using var manager=new IslandPluginManager(temp.Root);
  var received=new System.Collections.Concurrent.ConcurrentQueue<IslandPluginContent?>();
  manager.ContentChanged+=(_,e)=>received.Enqueue(e.Content);
  const string source="function onInit(){island.publish({text:'ready'});} function onEvent(e){island.publish({text:e.name,details:e.payload.text});}";
  await manager.InstallAsync(temp.Package("1.0.0",[],source:source),[]);
  await manager.EnableAsync("example.clock",true).WaitAsync(TimeSpan.FromSeconds(8));
  Assert.Contains(received,c=>c?.Text=="ready");
  await Task.Delay(150);
  await manager.SendEventAsync("clipboard.changed",new {text="secret"});
  Assert.DoesNotContain(received,c=>c?.Details=="secret");
  await manager.StopAsync();Assert.True(Assert.Single(manager.List()).Enabled);Assert.Null(received.Last());
  await manager.StartEnabledAsync().WaitAsync(TimeSpan.FromSeconds(8));Assert.Equal("运行中",Assert.Single(manager.List()).Status);
  await manager.InstallAsync(temp.Package("2.0.0",["clipboard.read"],source:source),["clipboard.read"]);
  await Task.Delay(150);
  await manager.SendEventAsync("clipboard.changed",new {text="granted"}).WaitAsync(TimeSpan.FromSeconds(5));
  Assert.Contains(received,c=>c?.Text=="clipboard.changed"&&c.Details=="granted");
  await manager.EnableAsync("example.clock",false);Assert.False(Assert.Single(manager.List()).Enabled);Assert.Null(received.Last());
 }
 [Fact] public async Task RealWorkerFaultIsBoundedAndDoesNotStopManager()
 {
  using var temp=new PluginTemp();await using var manager=new IslandPluginManager(temp.Root);
  await manager.InstallAsync(temp.Package("1.0.0",[],source:"function onInit(){while(true){}}"),[]);
  await Assert.ThrowsAnyAsync<Exception>(()=>manager.EnableAsync("example.clock",true).WaitAsync(TimeSpan.FromSeconds(8)));
  Assert.False(Assert.Single(manager.List()).Enabled);Assert.Equal("故障已停用",Assert.Single(manager.List()).Status);
  await manager.InstallAsync(temp.Package("2.0.0",[]),[]);
  await manager.EnableAsync("example.clock",true).WaitAsync(TimeSpan.FromSeconds(8));
  Assert.Equal("运行中",Assert.Single(manager.List()).Status);
 }
 [Fact] public async Task InfiniteTickDisablesOnlyFaultingWorker()
 {
  using var temp=new PluginTemp();await using var manager=new IslandPluginManager(temp.Root);
  await manager.InstallAsync(temp.Package("1.0.0",[],source:"function onInit(){island.publish({text:'running'});} function onTick(){while(true){}}"),[]);
  await manager.EnableAsync("example.clock",true).WaitAsync(TimeSpan.FromSeconds(8));
  var until=DateTime.UtcNow.AddSeconds(6);
  while(Assert.Single(manager.List()).Enabled&&DateTime.UtcNow<until)await Task.Delay(100);
  Assert.False(Assert.Single(manager.List()).Enabled);Assert.Equal("故障已停用",Assert.Single(manager.List()).Status);
 }
 [Fact] public async Task FailedEnabledUpdateRollsBackPackageAndRestartsOldWorker()
 {
  using var temp=new PluginTemp();await using var manager=new IslandPluginManager(temp.Root);
  await manager.InstallAsync(temp.Package("1.0.0",[]),[]);await manager.EnableAsync("example.clock",true).WaitAsync(TimeSpan.FromSeconds(8));
  await Assert.ThrowsAnyAsync<Exception>(()=>manager.InstallAsync(temp.Package("2.0.0",[],source:"function onInit(){throw new Error('bad update');}"),[]).WaitAsync(TimeSpan.FromSeconds(12)));
  var retained=Assert.Single(manager.List());Assert.Equal("1.0.0",retained.Manifest.Version);Assert.True(retained.Enabled);Assert.Equal("运行中",retained.Status);
 }
 [Fact] public async Task SuspendedManagerDoesNotLaunchOrTickUntilResumed()
 {
  using var temp=new PluginTemp();await using var manager=new IslandPluginManager(temp.Root);
  var content=new System.Collections.Concurrent.ConcurrentQueue<string>();
  manager.ContentChanged+=(_,e)=>{if(e.Content is not null)content.Enqueue(e.Content.Text);};
  await manager.StopAsync();
  await manager.InstallAsync(temp.Package("1.0.0",[],source:"function onInit(){island.publish({text:'started'});} function onTick(){island.publish({text:'tick'});}"),[]);
  await manager.EnableAsync("example.clock",true);
  await Task.Delay(1250);Assert.Empty(content);Assert.True(Assert.Single(manager.List()).Enabled);
  await manager.StartEnabledAsync().WaitAsync(TimeSpan.FromSeconds(8));Assert.Contains("started",content);
  await manager.StopAsync();var count=content.Count;await Task.Delay(1250);Assert.Equal(count,content.Count);Assert.True(Assert.Single(manager.List()).Enabled);
 }
 [Fact] public async Task EnabledPluginLimitRejectsNinthPluginEvenWhileSuspended()
 {
  using var temp=new PluginTemp();await using var manager=new IslandPluginManager(temp.Root);await manager.StopAsync();
  for(var i=0;i<9;i++)
  {
   var id="example.plugin"+i;
   await manager.InstallAsync(temp.Package("1.0.0",[],id:id),[]);
   if(i<8)await manager.EnableAsync(id,true);
   else {var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>manager.EnableAsync(id,true));Assert.Contains("8",error.Message);}
  }
  Assert.Equal(8,manager.List().Count(p=>p.Enabled));
 }
 [Fact] public async Task ConcurrentDisposeIsIdempotentAndPreventsRestart()
 {
  using var temp=new PluginTemp();var manager=new IslandPluginManager(temp.Root);
  await Task.WhenAll(manager.DisposeAsync().AsTask(),manager.DisposeAsync().AsTask());
  await manager.StopAsync();await Assert.ThrowsAsync<ObjectDisposedException>(()=>manager.StartEnabledAsync());
 }
 [Theory] [InlineData("id")] [InlineData("name")] [InlineData("description")] [InlineData("version")] [InlineData("minHostVersion")] [InlineData("entry")] [InlineData("permissions")] [InlineData("networkAllowlist")]
 public void RejectsExplicitNullManifestFields(string field)
 {
  using var temp=new PluginTemp();using var manager=new IslandPluginManager(temp.Root);
  var package=temp.Package("1.0.0",[]);
  using(var zip=ZipFile.Open(package,ZipArchiveMode.Update))
  {
   var entry=zip.GetEntry("manifest.json")!;System.Text.Json.Nodes.JsonObject manifest;
   using(var reader=new StreamReader(entry.Open()))manifest=System.Text.Json.Nodes.JsonNode.Parse(reader.ReadToEnd())!.AsObject();
   manifest[field]=null;entry.Delete();using var writer=new StreamWriter(zip.CreateEntry("manifest.json").Open());writer.Write(manifest.ToJsonString());
  }
  Assert.Throws<InvalidDataException>(()=>manager.InspectPackage(package));
 }
 [Theory] [InlineData("{\"manifest\":null,\"grantedPermissions\":[]}")] [InlineData("{\"manifest\":{},\"grantedPermissions\":null}")] [InlineData("{\"enabled\":\"yes\"}")] [InlineData("not-json")]
 public void CorruptedInstalledStateDoesNotCrashManager(string json)
 {
  using var temp=new PluginTemp();var directory=Path.Combine(temp.Root,"example.clock");Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"state.json"),json);
  using var manager=new IslandPluginManager(temp.Root);Assert.Empty(manager.List());
 }
 [Fact] public async Task NullGrantedPermissionsInOtherwiseValidStateAreIgnored()
 {
  using var temp=new PluginTemp();await using(var manager=new IslandPluginManager(temp.Root))await manager.InstallAsync(temp.Package("1.0.0",[]),[]);
  var path=Path.Combine(temp.Root,"example.clock","state.json");var state=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();state["grantedPermissions"]=null;File.WriteAllText(path,state.ToJsonString());
  using var reopened=new IslandPluginManager(temp.Root);Assert.Empty(reopened.List());
 }
 private sealed class PluginTemp : IDisposable
 {
  public string Root {get;} = Path.Combine(Path.GetTempPath(), "island-plugin-test-"+Guid.NewGuid().ToString("N"));
  public PluginTemp() => Directory.CreateDirectory(Root);
  public string Package(string version, string[] permissions, string? extra=null,string? source=null,string id="example.clock")
  { var path=Path.Combine(Root,Guid.NewGuid()+".islandpkg"); using var zip=ZipFile.Open(path,ZipArchiveMode.Create);
    using(var w=new StreamWriter(zip.CreateEntry("manifest.json").Open())) w.Write(JsonSerializer.Serialize(new {id,name="Clock",version,apiVersion=1,minHostVersion="0.1.0-beta.23",entry="main.js",permissions}));
    using(var w=new StreamWriter(zip.CreateEntry("main.js").Open())) w.Write(source ?? "function onInit(){island.publish({text:'clock'});}");
    if(extra is not null) using(var w=new StreamWriter(zip.CreateEntry(extra).Open())) w.Write("x"); return path; }
  public void Dispose()=>Directory.Delete(Root,true);
 }
}







