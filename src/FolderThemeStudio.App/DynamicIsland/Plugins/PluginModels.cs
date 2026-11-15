using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace FolderThemeStudio.App.DynamicIsland.Plugins;

public sealed record IslandPluginManifest
{
 public string Id {get;init;} = "";
 public string Name {get;init;} = "";
 public string Description {get;init;} = "";
 public string Version {get;init;} = "";
 public int ApiVersion {get;init;}
 public string MinHostVersion {get;init;} = "";
 public string Entry {get;init;} = "";
 public string[] Permissions {get;init;} = [];
 public string[] NetworkAllowlist {get;init;} = [];
 public PluginSettingDefinition[] Settings {get;init;} = [];
 internal void Validate()
 {
  if(Id is null || Name is null || Description is null || Version is null || MinHostVersion is null || Entry is null)throw new InvalidDataException("manifest 字段不能为 null。");
  if (!Regex.IsMatch(Id, @"^[a-z0-9][a-z0-9.-]{2,63}$") || Id.Contains("..") || !Id.Contains('.') || Name.Length is <1 or >80 || Description.Length>1000)
   throw new InvalidDataException("插件标识或名称无效。");
  if (!System.Version.TryParse(Version,out _) || ApiVersion!=1 || Entry!="main.js") throw new InvalidDataException("仅支持 API v1、数字版本和 main.js 入口。");
  if (!TryVersion(MinHostVersion, out var required) || !TryVersion(typeof(IslandPluginManifest).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute),false).OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion?.Split('+')[0] ?? "",out var current) || required.CompareTo(current)>0)
   throw new InvalidDataException("插件要求不兼容的宿主版本。");
  if (Permissions is null || Permissions.Length>3 || Permissions.Distinct(StringComparer.Ordinal).Count()!=Permissions.Length || Permissions.Any(p=>p is not ("media.read" or "clipboard.read" or "notifications.read")))
   throw new InvalidDataException("包含未知或尚不支持的权限；此版本不开放网络。");
  if (NetworkAllowlist is null || NetworkAllowlist.Length!=0) throw new InvalidDataException("此版本不开放网络。");
  if (Settings is null) throw new InvalidDataException("设置定义不能为 null。");
  if (Settings.Length > 0 && required.CompareTo((24,6)) < 0) throw new InvalidDataException("带设置的插件要求至少 beta.24-preview.6。");
  if (Settings.Any(s => s?.Type == "time") && required.CompareTo((24,8)) < 0) throw new InvalidDataException("时间设置要求至少 beta.24-preview.8。");
  _ = new PluginSettingsSchema(Settings);
 }
 private static bool TryVersion(string value,out (int Beta,int Preview) version)
 {
  version=default; var match=Regex.Match(value,@"^0\.1\.0-beta\.(\d+)(?:-preview\.(\d+))?$");
  if(!match.Success || !int.TryParse(match.Groups[1].Value,out var beta))return false;
  var preview=int.MaxValue;
  if(match.Groups[2].Success && !int.TryParse(match.Groups[2].Value,out preview))return false;
  version=(beta,preview);return true;
 }
}
public sealed record IslandPluginContent(string Text, string Icon="", string Details="", int TtlMs=10000);
public sealed record InstalledIslandPlugin(IslandPluginManifest Manifest, bool Enabled, string[] GrantedPermissions, string Status="已停用")
{
 public Dictionary<string, JsonElement> Settings { get; init; } = new(StringComparer.Ordinal);
}
public sealed class IslandPluginContentChangedEventArgs(string pluginId, IslandPluginContent? content) : EventArgs
{
 public string PluginId {get;}=pluginId;
 public IslandPluginContent? Content {get;}=content;
}
internal static class PluginJson
{
 internal static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy=JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive=true, MaxDepth=16 };
 internal static string Serialize<T>(T value)=>JsonSerializer.Serialize(value,Options);
 internal static T Read<T>(string text)
 {
  try{return JsonSerializer.Deserialize<T>(text,Options) ?? throw new InvalidDataException("无效插件 JSON。");}
  catch(JsonException ex){throw new InvalidDataException("插件 JSON 字段类型或格式无效。",ex);}
 }
}


