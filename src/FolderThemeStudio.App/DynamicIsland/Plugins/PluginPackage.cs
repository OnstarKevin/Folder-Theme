using System.IO;
using System.IO.Compression;
namespace FolderThemeStudio.App.DynamicIsland.Plugins;
internal sealed class PluginPackage : IDisposable
{
 internal const long MaximumSize=8*1024*1024;
 private readonly ZipArchive archive;
 internal IslandPluginManifest Manifest {get;}
 internal PluginPackage(string path)
 {
  if(!string.Equals(Path.GetExtension(path),".islandpkg",StringComparison.OrdinalIgnoreCase) || new FileInfo(path).Length>MaximumSize) throw new InvalidDataException("插件包格式或大小无效。");
  archive=ZipFile.OpenRead(path);
  try {
   if(archive.Entries.Count>128) throw new InvalidDataException("插件包文件过多。");
   long total=0; var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var e in archive.Entries)
   {
    var name=e.FullName;
    if(name.Length>180 || name.Contains('\\') || name.Contains(':') || name.StartsWith('/') || name.Split('/').Any(p=>p is "." or "..") || name.Any(c=>char.IsControl(c)||"<>\"|?*".Contains(c)) || name.TrimEnd('/').Split('/').Any(p=>p.EndsWith('.')||p.EndsWith(' ')) || !names.Add(name)) throw new InvalidDataException("插件包路径不安全。");
    if((e.ExternalAttributes>>16 & 0xF000)==0xA000) throw new InvalidDataException("插件包不允许符号链接。");
    if(name!="manifest.json" && name!="main.js" && !(name.StartsWith("assets/",StringComparison.Ordinal) && (name.EndsWith('/') || new[]{".png",".jpg",".jpeg",".svg",".webp",".txt",".json"}.Contains(Path.GetExtension(name),StringComparer.OrdinalIgnoreCase)))) throw new InvalidDataException("插件包包含不支持的文件。");
    if(e.Length>MaximumSize || (total+=e.Length)>MaximumSize) throw new InvalidDataException("插件包解压大小超限。");
   }
   var manifest=archive.GetEntry("manifest.json") ?? throw new InvalidDataException("缺少 manifest.json。");
   if(manifest.Length>32768) throw new InvalidDataException("manifest 过大。");
   using var reader=new StreamReader(manifest.Open()); Manifest=PluginJson.Read<IslandPluginManifest>(ReadBounded(reader,32768)); Manifest.Validate();
   var script=archive.GetEntry("main.js") ?? throw new InvalidDataException("缺少 main.js。");
   if(script.Length>262144) throw new InvalidDataException("脚本过大。");
  } catch {archive.Dispose();throw;}
 }
 internal void Extract(string destination)
 {
  PluginStoragePaths.EnsureNoReparsePoints(destination);Directory.CreateDirectory(destination);
  long total=0;
  foreach(var e in archive.Entries)
  {
   var path=Path.GetFullPath(Path.Combine(destination,e.FullName));
   if(!path.StartsWith(Path.GetFullPath(destination)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("插件路径越界。");
   PluginStoragePaths.EnsureNoReparsePoints(path);
   if(e.FullName.EndsWith('/')){Directory.CreateDirectory(path);continue;}
   Directory.CreateDirectory(Path.GetDirectoryName(path)!);
   using var input=e.Open();using var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write);var buffer=new byte[8192];int count;
   while((count=input.Read(buffer))>0){if((total+=count)>MaximumSize)throw new InvalidDataException("插件包解压大小超限。");output.Write(buffer,0,count);}
  }
 }
 private static string ReadBounded(StreamReader reader,int maximum)
 {
  var buffer=new char[maximum+1];var count=reader.ReadBlock(buffer,0,buffer.Length);if(count>maximum)throw new InvalidDataException("manifest 过大。");return new string(buffer,0,count);
 }
 public void Dispose()=>archive.Dispose();
}




