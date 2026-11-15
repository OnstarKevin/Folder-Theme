using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FolderThemeStudio.App.DynamicIsland.Plugins;

/// <summary>插件设置的声明式数据模型；不执行插件提供的界面代码。</summary>
public sealed record PluginSettingDefinition(string Key, string Label, string Type, JsonElement Default);

public sealed class PluginSettingsSchema
{
    private readonly PluginSettingDefinition[] definitions;
    public IReadOnlyList<PluginSettingDefinition> Definitions => definitions;
    public PluginSettingsSchema(IEnumerable<PluginSettingDefinition> definitions)
    {
        this.definitions = definitions.Take(65).ToArray();
        if (this.definitions.Length > 64) throw new InvalidDataException("插件设置项不能超过64项。");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in this.definitions)
        {
            if (item is null || item.Key is null || !Regex.IsMatch(item.Key, "^[a-zA-Z][a-zA-Z0-9_]{0,63}$") || !keys.Add(item.Key)
                || string.IsNullOrWhiteSpace(item.Label) || item.Label.Length > 120 || !Valid(item, item.Default))
                throw new InvalidDataException("插件设置定义无效。");
        }
    }
    public IReadOnlyDictionary<string, JsonElement> Resolve(IReadOnlyDictionary<string, JsonElement> values)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var item in definitions)
        {
            var value = values.TryGetValue(item.Key, out var existing) ? existing : item.Default;
            if (!Valid(item, value)) throw new InvalidDataException($"设置项 {item.Key} 的值无效。");
            result.Add(item.Key, value.Clone());
        }
        if (values.Keys.Any(key => !result.ContainsKey(key))) throw new InvalidDataException("包含未知设置项。");
        return result;
    }
    private static bool Valid(PluginSettingDefinition item, JsonElement value) => item.Type switch
    {
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "string" => value.ValueKind == JsonValueKind.String && (value.GetString()?.Length ?? 0) <= 4096,
        "time" => value.ValueKind == JsonValueKind.String && Regex.IsMatch(value.GetString() ?? "", @"^(?:[01]\d|2[0-3]):[0-5]\d$"),
        "number" => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number),
        _ => false
    };
}
