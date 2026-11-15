using System.Diagnostics;
using System.IO;
using System.Text.Json;
namespace FolderThemeStudio.App.DynamicIsland.Plugins;
internal sealed class PluginWorkerClient : IAsyncDisposable
{
 private readonly Process process;
 private readonly SemaphoreSlim gate=new(1,1);
 private bool dead;
 internal bool LastChanged {get;private set;}
 internal PluginWorkerClient()
 {
  var path=Environment.ProcessPath ?? throw new InvalidOperationException("找不到宿主程序。");
  if(!string.Equals(Path.GetFileNameWithoutExtension(path),"FolderThemeStudio.App",StringComparison.OrdinalIgnoreCase))
   path=Path.Combine(AppContext.BaseDirectory,"FolderThemeStudio.App.exe");
  process=Process.Start(new ProcessStartInfo(path,PluginWorker.Argument){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true}) ?? throw new InvalidOperationException("无法启动插件工作进程。");
  // Never wait for redirected stderr to fill. Workers emit no diagnostics there.
  process.ErrorDataReceived+=(_,_)=>{};process.BeginErrorReadLine();
 }
 internal async Task<IslandPluginContent?> RequestAsync(string operation,string? source=null,string? id=null,object? payload=null)
 {
  await gate.WaitAsync().ConfigureAwait(false);
  try {
   if(dead)throw new InvalidOperationException("插件工作进程已停止。");
   using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(operation=="init"?5:2));
   var element=payload is null?(JsonElement?)null:JsonSerializer.SerializeToElement(payload,PluginJson.Options);
   var json=PluginJson.Serialize(new WorkerRequest(operation,source,id,element));
   if(json.Length>300000)throw new InvalidDataException("IPC 消息超限。");
   await process.StandardInput.WriteLineAsync(json.AsMemory(),timeout.Token).ConfigureAwait(false);
   await process.StandardInput.FlushAsync(timeout.Token).ConfigureAwait(false);
   var line=await PluginWorker.ReadBoundedLineAsync(process.StandardOutput,20000,timeout.Token).ConfigureAwait(false) ?? throw new IOException("插件工作进程已退出。");
   var reply=PluginJson.Read<WorkerReply>(line);
   if(!reply.Ok)throw new InvalidDataException(reply.Error);
   LastChanged=reply.Changed;
   return reply.Content;
  }catch{Kill();throw;}finally{gate.Release();}
 }
 private void Kill(){dead=true;try{if(!process.HasExited)process.Kill(entireProcessTree:true);}catch(InvalidOperationException){} }
 public async ValueTask DisposeAsync()
 {
  if(!dead){try{await RequestAsync("dispose").ConfigureAwait(false);}catch{} }
  Kill();process.Dispose();
 }
}



