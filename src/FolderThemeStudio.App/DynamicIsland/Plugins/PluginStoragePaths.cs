using System.IO;
namespace FolderThemeStudio.App.DynamicIsland.Plugins;

internal static class PluginStoragePaths
{
 // Revalidate before reads and mutations: a manually replaced junction must never redirect plugin storage.
 internal static void EnsureNoReparsePoints(string path)
 {
  string? current=Path.GetFullPath(path);
  while(current is not null)
  {
   try{if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("插件存储不允许符号链接或目录联接。");}
   catch(FileNotFoundException){}
   catch(DirectoryNotFoundException){}
   current=Path.GetDirectoryName(current);
  }
 }
 internal static void ValidateTree(string directory)
 {
  EnsureNoReparsePoints(directory);
  if(!Directory.Exists(directory))return;
  var pending=new Stack<string>();pending.Push(directory);var count=0;
  while(pending.TryPop(out var next))
  {
   foreach(var entry in Directory.EnumerateFileSystemEntries(next))
   {
    if(++count>512)throw new InvalidDataException("插件存储包含过多文件。");
    EnsureNoReparsePoints(entry);
    if(Directory.Exists(entry))pending.Push(entry);
   }
  }
 }
 internal static string ReadBoundedText(string path,int maximum)
 {
  EnsureNoReparsePoints(path);
  using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
  if(stream.Length>maximum)throw new InvalidDataException("插件存储文件过大。");
  using var reader=new StreamReader(stream);var buffer=new char[maximum+1];var count=reader.ReadBlock(buffer,0,buffer.Length);
  if(count>maximum)throw new InvalidDataException("插件存储文件过大。");return new string(buffer,0,count);
 }
 internal static void DeleteTree(string directory)
 {
  ValidateTree(directory);if(Directory.Exists(directory))Directory.Delete(directory,true);
 }
}
