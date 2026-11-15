using System.IO;
using System.Text.Json;
namespace FolderThemeStudio.App.DynamicIsland.Plugins;

/// <summary>Owns installed plugin packages and disposable JavaScript workers. Process separation is not a full OS sandbox.</summary>
public sealed class IslandPluginManager : IDisposable,IAsyncDisposable
{
 private readonly string root;
 private readonly SemaphoreSlim gate=new(1,1);
 private readonly Dictionary<string,InstalledIslandPlugin> installed=new(StringComparer.Ordinal);
 private readonly Dictionary<string,PluginWorkerClient> workers=new(StringComparer.Ordinal);
 private readonly CancellationTokenSource lifetime=new();
 private readonly Task ticker;
 private readonly SemaphoreSlim workerAvailable=new(0,1);
 private readonly object disposeSync=new();
 private Task? disposeTask;
 public const int MaximumEnabledPlugins=8;
 private readonly Dictionary<string,long> lastPublish=new(StringComparer.Ordinal);
 private bool suspended;
 private bool disposed;
 public event EventHandler<IslandPluginContentChangedEventArgs>? ContentChanged;
 public IslandPluginManager(string? root=null)
 {
  this.root=Path.GetFullPath(root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"FolderThemeStudio","island-plugins"));
  PluginStoragePaths.EnsureNoReparsePoints(this.root);Directory.CreateDirectory(this.root);
  foreach(var directory in Directory.EnumerateDirectories(this.root).Where(p=>!Path.GetFileName(p).StartsWith('.')))
  {
   try {
    if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)continue;
    var state=PluginJson.Read<InstalledIslandPlugin>(PluginStoragePaths.ReadBoundedText(Path.Combine(directory,"state.json"),32768));
    if(state.Manifest is null || state.Settings is null || state.GrantedPermissions is null || state.GrantedPermissions.Length>3 || state.GrantedPermissions.Any(p=>p is null) || state.GrantedPermissions.Distinct(StringComparer.Ordinal).Count()!=state.GrantedPermissions.Length)continue;
    state.Manifest.Validate();
    state = state with { Settings = new(new PluginSettingsSchema(state.Manifest.Settings).Resolve(state.Settings), StringComparer.Ordinal) };
    if(Path.GetFileName(directory)!=state.Manifest.Id || state.GrantedPermissions.Any(p=>!state.Manifest.Permissions.Contains(p)) || state.Manifest.Permissions.Except(state.GrantedPermissions).Any())continue;
    installed[state.Manifest.Id]=state with {Status=state.Enabled?"等待启动":"已停用"};
   }catch(Exception ex)when(ex is InvalidDataException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException){ }
  }
  ticker=Task.Run(TickLoopAsync);
 }
 public IReadOnlyList<InstalledIslandPlugin> List(){lock(installed)return installed.Values.OrderBy(p=>p.Manifest.Name).Select(p=>p with {Manifest=p.Manifest with {Permissions=p.Manifest.Permissions.ToArray(),NetworkAllowlist=p.Manifest.NetworkAllowlist.ToArray(),Settings=p.Manifest.Settings.ToArray()},GrantedPermissions=p.GrantedPermissions.ToArray(),Settings=new(p.Settings,StringComparer.Ordinal)}).ToArray();}
 public IReadOnlyDictionary<string,JsonElement> GetSettings(string id)
 {
  lock(installed) return installed.TryGetValue(id,out var state) ? new Dictionary<string,JsonElement>(state.Settings,StringComparer.Ordinal) : throw new KeyNotFoundException(id);
 }
 public async Task UpdateSettingsAsync(string id,IReadOnlyDictionary<string,JsonElement> values)
 {
  await gate.WaitAsync().ConfigureAwait(false);
  try {
   ThrowIfDisposed(); InstalledIslandPlugin state; lock(installed)state=installed.TryGetValue(id,out var found)?found:throw new KeyNotFoundException(id);
   var resolved=new PluginSettingsSchema(state.Manifest.Settings).Resolve(values);
   if(PluginJson.Serialize(resolved).Length>16000)throw new InvalidDataException("插件设置总量过大。");
   var next=state with {Settings=new(resolved,StringComparer.Ordinal)}; Save(next);
   if(workers.TryGetValue(id,out var worker))try {var content=await worker.RequestAsync("event",payload:new {name="settings.changed",payload=resolved}).ConfigureAwait(false);if(worker.LastChanged)Publish(id,content,true);}catch(Exception ex){await FaultCoreAsync(id).ConfigureAwait(false);throw new InvalidOperationException("设置已保存，但插件响应失败并已停用。",ex);}
  } finally {gate.Release();}
 }
 public IslandPluginManifest InspectPackage(string path){using var package=new PluginPackage(path);return package.Manifest;}
 public async Task InstallAsync(string path,IEnumerable<string> grantedPermissions)
 {
  using var package=new PluginPackage(path);
  var manifest=package.Manifest;
  var grants=grantedPermissions.Distinct(StringComparer.Ordinal).ToArray();
  if(manifest.Permissions.Except(grants,StringComparer.Ordinal).Any())throw new UnauthorizedAccessException("请明确授权插件请求的权限。");
  if(grants.Except(manifest.Permissions,StringComparer.Ordinal).Any())throw new UnauthorizedAccessException("不能授予插件未请求的权限。");
  await gate.WaitAsync().ConfigureAwait(false);
  string? staging=null,backup=null; bool moved=false,transactionStarted=false;
  InstalledIslandPlugin? previous=null;
  try {
   ThrowIfDisposed();PluginStoragePaths.ValidateTree(Path.Combine(root,manifest.Id));transactionStarted=true;lock(installed)installed.TryGetValue(manifest.Id,out previous);
   staging=Path.Combine(root,".stage-"+Guid.NewGuid().ToString("N")); package.Extract(staging);
   var migrated=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
   foreach(var definition in manifest.Settings)
   {
    var one=new PluginSettingsSchema([definition]);
    if(previous?.Settings.TryGetValue(definition.Key,out var old)==true)
     try{migrated[definition.Key]=one.Resolve(new Dictionary<string,JsonElement>{{definition.Key,old}})[definition.Key];continue;}catch(InvalidDataException){}
    migrated[definition.Key]=definition.Default.Clone();
   }
   if(PluginJson.Serialize(migrated).Length>16000)throw new InvalidDataException("插件设置总量过大。");
   var state=new InstalledIslandPlugin(manifest,previous?.Enabled ?? false,grants,previous?.Enabled==true?"等待启动":"已停用") { Settings=migrated };
   var stagedState=Path.Combine(staging,"state.json");PluginStoragePaths.EnsureNoReparsePoints(stagedState);File.WriteAllText(stagedState,SerializeState(state));
   var destination=Path.Combine(root,manifest.Id);
   await StopWorkerCoreAsync(manifest.Id).ConfigureAwait(false);
   PluginStoragePaths.ValidateTree(destination);PluginStoragePaths.ValidateTree(staging);
   if(Directory.Exists(destination)){backup=Path.Combine(root,".rollback-"+Guid.NewGuid().ToString("N"));Directory.Move(destination,backup);}
   Directory.Move(staging,destination); staging=null;moved=true;
   lock(installed)installed[manifest.Id]=state;
   if(state.Enabled&&!suspended)await StartWorkerCoreAsync(state).ConfigureAwait(false);
   if(backup is not null){try{PluginStoragePaths.DeleteTree(backup);}catch(InvalidDataException){}catch(IOException){}catch(UnauthorizedAccessException){}backup=null;}
  } catch {
   if(!transactionStarted)throw;
   await StopWorkerCoreAsync(manifest.Id).ConfigureAwait(false);
   var destination=Path.Combine(root,manifest.Id);
   if(moved && Directory.Exists(destination))PluginStoragePaths.DeleteTree(destination);
   if(backup is not null && Directory.Exists(backup))Directory.Move(backup,destination);
   lock(installed){if(previous is null)installed.Remove(manifest.Id);else installed[manifest.Id]=previous;}
   if(previous?.Enabled==true&&!suspended){try{await StartWorkerCoreAsync(previous).ConfigureAwait(false);}catch{await FaultCoreAsync(previous.Manifest.Id).ConfigureAwait(false);}}
   throw;
  } finally {try{if(staging is not null&&Directory.Exists(staging))PluginStoragePaths.DeleteTree(staging);}finally{gate.Release();}}
 }
 public async Task EnableAsync(string id,bool enabled)
 {
  await gate.WaitAsync().ConfigureAwait(false);
  try {
   ThrowIfDisposed(); InstalledIslandPlugin state;lock(installed)state=installed.TryGetValue(id,out var found)?found:throw new KeyNotFoundException(id);
   await StopWorkerCoreAsync(id).ConfigureAwait(false);
   if(enabled){lock(installed){if(installed.Values.Count(p=>p.Enabled&&p.Manifest.Id!=id)>=MaximumEnabledPlugins)throw new InvalidOperationException("最多同时启用 8 个插件，请先停用其他插件。");}}
   state=state with {Enabled=enabled,Status=enabled?"等待启动":"已停用"};
   Save(state);
   if(enabled&&!suspended){try{await StartWorkerCoreAsync(state).ConfigureAwait(false);}catch{await FaultCoreAsync(id).ConfigureAwait(false);throw;}}
  }finally{gate.Release();}
 }
 public async Task UninstallAsync(string id)
 {
  await gate.WaitAsync().ConfigureAwait(false);try{
   ThrowIfDisposed();lock(installed){if(!installed.ContainsKey(id))return;}
   await StopWorkerCoreAsync(id).ConfigureAwait(false);PluginStoragePaths.DeleteTree(Path.Combine(root,id));lock(installed)installed.Remove(id);
  }finally{gate.Release();}
 }
 public async Task StartEnabledAsync()
 {
  await gate.WaitAsync().ConfigureAwait(false);try{ThrowIfDisposed();suspended=false;foreach(var state in List().Where(p=>p.Enabled)){if(workers.ContainsKey(state.Manifest.Id))continue;try{await StartWorkerCoreAsync(state).ConfigureAwait(false);}catch{await FaultCoreAsync(state.Manifest.Id).ConfigureAwait(false);}}}finally{gate.Release();}
 }
 public async Task StopAsync()
 {
  await gate.WaitAsync().ConfigureAwait(false);try{if(disposed)return;suspended=true;foreach(var id in workers.Keys.ToArray())await StopWorkerCoreAsync(id).ConfigureAwait(false);}finally{gate.Release();}
 }
 public async Task SendEventAsync(string name,object? payload=null)
 {
  var permission=name switch {"media.changed"=>"media.read","clipboard.changed"=>"clipboard.read","notification.received"=>"notifications.read",_=>null};
  // Unknown events are not forwarded: a future sensitive event must opt into the grant boundary.
  if(permission is null)return;
  await gate.WaitAsync().ConfigureAwait(false);try{
   if(disposed)return;
   foreach(var id in workers.Keys.ToArray()){
    InstalledIslandPlugin state;lock(installed)state=installed[id];
    if(!state.GrantedPermissions.Contains(permission,StringComparer.Ordinal))continue;
    try{var worker=workers[id];var content=await worker.RequestAsync("event",payload:new {name,payload}).ConfigureAwait(false);if(worker.LastChanged)Publish(id,content);}catch{await FaultCoreAsync(id).ConfigureAwait(false);}
   }
  }finally{gate.Release();}
 }
 private async Task StartWorkerCoreAsync(InstalledIslandPlugin state)
 {
  PluginStoragePaths.ValidateTree(Path.Combine(root,state.Manifest.Id));
  if(workers.Count>=MaximumEnabledPlugins)throw new InvalidOperationException("最多同时运行 8 个插件，请先停用其他插件。");
  var worker=new PluginWorkerClient();
  try {
   var content=await worker.RequestAsync("init",PluginStoragePaths.ReadBoundedText(Path.Combine(root,state.Manifest.Id,"main.js"),262144),state.Manifest.Id,state.Manifest.Settings.Length==0?null:state.Settings).ConfigureAwait(false);
   Save(state with {Status="运行中"});workers.Add(state.Manifest.Id,worker);
   if(workerAvailable.CurrentCount==0)workerAvailable.Release();Publish(state.Manifest.Id,content);
  }catch{await worker.DisposeAsync().ConfigureAwait(false);throw;}
 }
 private async Task StopWorkerCoreAsync(string id)
 {
  if(workers.Remove(id,out var worker))await worker.DisposeAsync().ConfigureAwait(false);
  lock(installed){if(installed.TryGetValue(id,out var state))installed[id]=state with {Status=state.Enabled?"已暂停":"已停用"};}
  Publish(id,null);
 }
 private async Task FaultCoreAsync(string id)
 {
  await StopWorkerCoreAsync(id).ConfigureAwait(false);
  InstalledIslandPlugin? state;lock(installed)installed.TryGetValue(id,out state);
  if(state is not null)
  {
   var faulted=state with {Enabled=false,Status="故障已停用"};
   try{Save(faulted);}
   catch(Exception ex)when(ex is InvalidDataException or IOException or UnauthorizedAccessException){lock(installed)installed[id]=faulted;}
  }
 }
 private void Save(InstalledIslandPlugin state)
 {
  var path=Path.Combine(root,state.Manifest.Id,"state.json");var temporary=path+".tmp";
  PluginStoragePaths.EnsureNoReparsePoints(path);PluginStoragePaths.EnsureNoReparsePoints(temporary);
  File.WriteAllText(temporary,SerializeState(state));File.Move(temporary,path,true);lock(installed)installed[state.Manifest.Id]=state;
 }
 private static string SerializeState(InstalledIslandPlugin state)
 {
  var json=PluginJson.Serialize(state);
  if(System.Text.Encoding.UTF8.GetByteCount(json)>32768)throw new InvalidDataException("插件配置过大，无法安全保存。");
  return json;
 }
 private void Publish(string id,IslandPluginContent? content,bool force=false)
 {
  if(content is not null){var now=Environment.TickCount64;if(!force&&lastPublish.TryGetValue(id,out var previous)&&now-previous<100)return;lastPublish[id]=now;}else lastPublish.Remove(id);
  try{ContentChanged?.Invoke(this,new(id,content));}catch{ /* Subscriber failures must not stop the worker. */ }
 }
 private async Task TickLoopAsync()
 {
  try
  {
   while(true)
   {
    await gate.WaitAsync(lifetime.Token).ConfigureAwait(false);
    bool active;
    try{active=workers.Count>0;}finally{gate.Release();}
    if(!active){await workerAvailable.WaitAsync(lifetime.Token).ConfigureAwait(false);continue;}
    await Task.Delay(TimeSpan.FromSeconds(1),lifetime.Token).ConfigureAwait(false);
    await gate.WaitAsync(lifetime.Token).ConfigureAwait(false);
    try
    {
     foreach(var id in workers.Keys.ToArray())
     {
      try
      {
       var worker=workers[id];
       var content=await worker.RequestAsync("tick",payload:DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).ConfigureAwait(false);
       if(worker.LastChanged)Publish(id,content);
      }
      catch{await FaultCoreAsync(id).ConfigureAwait(false);}
     }
    }
    finally{gate.Release();}
   }
  }
  catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}
 }
 private void ThrowIfDisposed()=>ObjectDisposedException.ThrowIf(disposed,this);
 public ValueTask DisposeAsync()
 {
  lock(disposeSync)return new ValueTask(disposeTask??=DisposeCoreAsync());
 }
 private async Task DisposeCoreAsync()
 {
  await gate.WaitAsync().ConfigureAwait(false);
  try
  {
   disposed=true;suspended=true;lifetime.Cancel();
   foreach(var id in workers.Keys.ToArray())await StopWorkerCoreAsync(id).ConfigureAwait(false);
  }
  finally{gate.Release();}
  await ticker.ConfigureAwait(false);workerAvailable.Dispose();lifetime.Dispose();
 }
 public void Dispose()=>Task.Run(async ()=>await DisposeAsync()).GetAwaiter().GetResult();
}







