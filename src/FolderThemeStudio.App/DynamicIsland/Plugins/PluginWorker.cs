using System.IO;
using System.Text.Json;
using Jint;
namespace FolderThemeStudio.App.DynamicIsland.Plugins;

/// <summary>Bounded JavaScript interpreter. Production executes this only in a disposable worker process.</summary>
public sealed class PluginScriptRuntime
{
 private readonly Engine engine;
 private readonly string id;
 public IslandPluginContent? CurrentContent {get;private set;}
 public bool LastChanged {get;private set;}
 public PluginScriptRuntime(string source,string pluginId)
 {
  if(source.Length>262144)throw new InvalidDataException("脚本过大。");
  id=pluginId;
  engine=new Engine(o=>o.MaxStatements(25000).LimitMemory(16_000_000).TimeoutInterval(TimeSpan.FromMilliseconds(150)).LimitRecursion(64));
  // No CLR object, delegate or assembly is projected into JavaScript. Output crosses only as JSON.
  engine.Execute("'use strict'; var __islandOutput=null; var __islandSequence=0; var island=Object.freeze({publish:function(c){__islandOutput=JSON.stringify(c);__islandSequence++;},clear:function(){__islandOutput=null;__islandSequence++;}});");
  engine.Execute(source);
 }
 public IslandPluginContent? Invoke(string operation,object? payload)
 {
  var function=operation switch {"init"=>"onInit","event"=>"onEvent","tick"=>"onTick","dispose"=>"onDispose",_=>throw new InvalidDataException("未知生命周期消息。")};
  var argument=operation=="init" ? (payload is null ? PluginJson.Serialize(new {id,apiVersion=1}) : PluginJson.Serialize(new {id,apiVersion=1,settings=payload})) : PluginJson.Serialize(payload);
  var sequence=engine.Evaluate("__islandSequence").AsNumber();
  engine.Execute($"if(typeof {function}==='function') {function}({argument});");
  LastChanged=sequence!=engine.Evaluate("__islandSequence").AsNumber();
  var output=engine.Evaluate("__islandOutput");
  if(output.IsNull() || output.IsUndefined()){CurrentContent=null;return null;}
  var json=output.AsString(); if(json.Length>16384)throw new InvalidDataException("插件输出超限。");
  var content=PluginJson.Read<IslandPluginContent>(json);
  if(content.Text is null || content.Text.Length>160 || content.Icon is null || content.Icon.Length>16 || content.Details is null || content.Details.Length>4000)throw new InvalidDataException("插件内容超限。");
  return CurrentContent=content with {TtlMs=Math.Clamp(content.TtlMs,250,60000)};
 }
}
internal sealed record WorkerRequest(string Operation,string? Source=null,string? Id=null,JsonElement? Payload=null);
internal sealed record WorkerReply(bool Ok,IslandPluginContent? Content=null,string? Error=null,bool Changed=true);
public static class PluginWorker
{
 public const string Argument="--island-plugin-worker";
 public static bool RunIfRequested(string[] args)
 {
  if(!args.Contains(Argument,StringComparer.Ordinal))return false;
  try { Task.Run(RunAsync).GetAwaiter().GetResult(); } catch { /* No UI, exception dialogs, or unbounded diagnostics in workers. */ }
  return true;
 }
 private static async Task RunAsync()
 {
  PluginScriptRuntime? runtime=null;
  using var input=new StreamReader(Console.OpenStandardInput(),System.Text.Encoding.UTF8);
  using var output=new StreamWriter(Console.OpenStandardOutput(),new System.Text.UTF8Encoding(false)){AutoFlush=true};
  while(await ReadBoundedLineAsync(input,300000,CancellationToken.None) is { } line)
  {
   try {
    var request=PluginJson.Read<WorkerRequest>(line);
    if(request.Operation=="init")runtime=new PluginScriptRuntime(request.Source ?? "",request.Id ?? "");
    if(runtime is null)throw new InvalidDataException("工作进程尚未初始化。");
    var content=runtime.Invoke(request.Operation,request.Payload);
    await output.WriteLineAsync(PluginJson.Serialize(new WorkerReply(true,content,Changed:runtime.LastChanged)));
    if(request.Operation=="dispose")break;
   } catch { await output.WriteLineAsync(PluginJson.Serialize(new WorkerReply(false,Error:"插件执行失败或资源超限。"))); break; }
  }
 }
 internal static async Task<string?> ReadBoundedLineAsync(StreamReader reader,int maximum,CancellationToken cancellation)
 {
  var builder=new System.Text.StringBuilder(); var buffer=new char[1];
  while(true){var count=await reader.ReadAsync(buffer.AsMemory(),cancellation).ConfigureAwait(false);if(count==0)return builder.Length==0?null:builder.ToString();if(buffer[0]=='\n')return builder.ToString();if(buffer[0]!='\r')builder.Append(buffer[0]);if(builder.Length>maximum)throw new InvalidDataException("IPC 消息超限。");}
 }
}



